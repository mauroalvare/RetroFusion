"""Consolidate ignored build history into a verified, content-addressed ZIP.

Only emu/out and emu/dist are managed. Sources, Git and user libraries stay outside
the cleanup scope. An archive is fully checked before any original is removed.
"""
from __future__ import annotations
import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import stat
import time
import uuid
import zipfile

KEEP_DIRS = ("out/historial", "out/mantenimiento", "out/030-hardware", "out/8086-hardware", "out/035-paquete-qa")
KEEP_FILES = {
    "out/espacio-medir.py", "out/espacio-antes.json", "out/espacio-antes.log",
    "out/t038-cuarta-informe.json", "out/t038-ready-informe.json", "out/t025-informe.json",
    "out/t038-8086-informe.json", "out/t038-8086-intervalos-informe.json",
    "out/t038-oraculo-flags-informe.json", "out/t038-relevo-final-informe.json", "out/035-final-informe.json",
}

def digest(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()

def bounded(root: Path, relative: str) -> Path:
    parts = PurePosixPath(relative)
    if parts.is_absolute() or ".." in parts.parts or "\\" in relative or ":" in relative or not parts.parts:
        raise ValueError("Ruta de archivo inválida: " + relative)
    if parts.parts[0] not in ("out", "dist"):
        raise ValueError("La limpieza solo admite out y dist.")
    target = root.joinpath(*parts.parts)
    if not target.resolve().is_relative_to(root.resolve()) or target == root:
        raise ValueError("La ruta sale del espacio de trabajo.")
    for parent in [target, *target.parents]:
        if parent == root:
            break
        if parent.is_symlink() or parent.exists() and getattr(parent.lstat(), "st_file_attributes", 0) & 0x400:
            raise ValueError("No se siguen enlaces ni puntos de reanálisis: " + str(parent))
    return target

def is_kept(relative: str) -> bool:
    return relative in KEEP_FILES or any(relative == p or relative.startswith(p + "/") for p in KEEP_DIRS)

def inventory(root: Path) -> list[dict]:
    entries = []
    for name in ("out", "dist"):
        start = bounded(root, name)
        if not start.exists():
            continue
        stack = [start]
        while stack:
            folder = stack.pop()
            with os.scandir(folder) as children:
                for child in children:
                    path = Path(child.path); relative = path.relative_to(root).as_posix()
                    if is_kept(relative):
                        continue
                    info = child.stat(follow_symlinks=False)
                    if child.is_symlink() or getattr(info, "st_file_attributes", 0) & 0x400:
                        raise ValueError("No se archivan enlaces: " + relative)
                    if child.is_dir(follow_symlinks=False):
                        stack.append(path)
                    elif child.is_file(follow_symlinks=False):
                        entries.append(dict(Path=relative, Bytes=info.st_size, MtimeNs=info.st_mtime_ns,
                                            Mode=stat.S_IMODE(info.st_mode)))
    return sorted(entries, key=lambda row: row["Path"])

def verify(archive: Path) -> dict:
    with zipfile.ZipFile(archive) as store:
        if len(store.infolist()) != len({info.filename for info in store.infolist()}):
            raise ValueError("El archivo contiene entradas duplicadas.")
        index = json.loads(store.read("index.json"))
        if index.get("Format") != "RetroEmu.WorkspaceArchive/1":
            raise ValueError("Formato de archivo desconocido.")
        blobs = {}
        for row in index["Files"]:
            path = PurePosixPath(row["Path"])
            if not path.parts or path.is_absolute() or ".." in path.parts or "\\" in row["Path"] or ":" in row["Path"] or path.parts[0] not in ("out", "dist"):
                raise ValueError("El índice contiene una ruta inválida.")
            sha = row["Sha256"]
            if len(sha) != 64 or any(c not in "0123456789abcdef" for c in sha):
                raise ValueError("Identificador de contenido inválido.")
            if sha in blobs and blobs[sha] != row["Bytes"]:
                raise ValueError("Tamaños contradictorios para el mismo contenido.")
            blobs[sha] = row["Bytes"]
        for sha, expected in blobs.items():
            with store.open("blobs/" + sha) as stream:
                h = hashlib.sha256(); size = 0
                while data := stream.read(1024 * 1024):
                    h.update(data); size += len(data)
            if size != expected or h.hexdigest() != sha:
                raise ValueError("El contenido no coincide con su SHA-256: " + sha)
    return index

def archive_history(root: Path, apply: bool = False) -> dict:
    root = root.resolve(); files = inventory(root); total = sum(row["Bytes"] for row in files)
    if not apply:
        return dict(Apply=False, Files=len(files), Bytes=total, Scope=["out", "dist"], KeptDirectories=KEEP_DIRS)
    folder = bounded(root, "out/historial"); folder.mkdir(parents=True, exist_ok=True)
    archive = folder / (time.strftime("historial-%Y%m%d-%H%M%S-") + uuid.uuid4().hex[:8] + ".zip")
    temporary = archive.with_suffix(".partial")
    unique = set(); unique_bytes = 0
    try:
        with zipfile.ZipFile(temporary, "x", compression=zipfile.ZIP_DEFLATED, compresslevel=6, allowZip64=True) as store:
            for number, row in enumerate(files, 1):
                path = bounded(root, row["Path"]); info = path.stat()
                if info.st_size != row["Bytes"] or info.st_mtime_ns != row["MtimeNs"]:
                    raise ValueError("Un archivo cambió durante la preparación: " + row["Path"])
                sha = digest(path); row["Sha256"] = sha
                if sha not in unique:
                    store.write(path, "blobs/" + sha); unique.add(sha); unique_bytes += row["Bytes"]
                # Reading/hash and archiving must observe the same stable file.
                after = path.stat()
                if after.st_size != info.st_size or after.st_mtime_ns != info.st_mtime_ns:
                    raise ValueError("Un archivo cambió durante la lectura: " + row["Path"])
                if number % 5000 == 0:
                    print(f"Archivados {number}/{len(files)}; contenidos únicos {len(unique)}.", flush=True)
            index = dict(Format="RetroEmu.WorkspaceArchive/1", Files=files, OriginalBytes=total,
                         UniqueBytes=unique_bytes, UniqueFiles=len(unique), Scope=["out", "dist"])
            store.writestr("index.json", json.dumps(index, ensure_ascii=False).encode("utf-8"))
        print("Verificando todos los contenidos y SHA-256 del archivo...", flush=True)
        verify(temporary)
        # Publish the checked archive before deleting the first source.
        temporary.rename(archive)
    except BaseException:
        # A partial archive is retained for inspection; originals have not been deleted.
        raise
    deleted = 0; removed_bytes = 0; skipped = []
    for row in files:
        path = bounded(root, row["Path"])
        try:
            if digest(path) != row["Sha256"]:
                skipped.append(dict(Path=row["Path"], Reason="Cambió después de archivar")); continue
            path.unlink(); deleted += 1; removed_bytes += row["Bytes"]
        except OSError as ex:
            skipped.append(dict(Path=row["Path"], Reason=str(ex)))
    # Only empty, explicitly bounded directories are removed; no recursive delete.
    directories = {bounded(root, row["Path"]).parent for row in files}
    for parent in list(directories):
        while parent != root and parent.relative_to(root).as_posix() not in ("out", "dist"):
            directories.add(parent); parent = parent.parent
    for directory in sorted(directories, key=lambda p: len(p.parts), reverse=True):
        if directory == root or is_kept(directory.relative_to(root).as_posix()):
            continue
        bounded(root, directory.relative_to(root).as_posix())
        try:
            directory.rmdir()
        except OSError:
            pass
    report = dict(Result="PASS" if not skipped else "PARTIAL", Archive=str(archive), ArchiveSha256=digest(archive),
                  ArchiveBytes=archive.stat().st_size, OriginalBytes=total, UniqueBytes=unique_bytes,
                  ArchivedFiles=len(files), UniqueFiles=len(unique), DeletedFiles=deleted,
                  RemovedBytes=removed_bytes, NetBytesFreed=removed_bytes-archive.stat().st_size, Skipped=skipped,
                  VerifiedBeforeDeletion=True, Scope=["out", "dist"])
    report_path = archive.with_suffix(".json")
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2)+"\n", encoding="utf-8")
    return report

def restore(root: Path, archive: Path, prefix: str) -> dict:
    bounded(root, prefix); index = verify(archive)
    selected = [row for row in index["Files"] if row["Path"] == prefix or row["Path"].startswith(prefix.rstrip("/") + "/")]
    if not selected:
        raise ValueError("No hay archivos bajo ese prefijo.")
    count = 0
    with zipfile.ZipFile(archive) as store:
        for row in selected:
            target = bounded(root, row["Path"])
            if target.exists():
                if digest(target) == row["Sha256"]:
                    continue
                raise FileExistsError("La restauración no sobrescribe archivos diferentes: " + str(target))
            target.parent.mkdir(parents=True, exist_ok=True)
            bounded(root, row["Path"])
            temporary = target.with_name(target.name + ".restoring-" + uuid.uuid4().hex)
            with store.open("blobs/" + row["Sha256"]) as source, temporary.open("xb") as dest:
                while data := source.read(1024 * 1024): dest.write(data)
            if digest(temporary) != row["Sha256"]:
                raise ValueError("Falló la verificación de la restauración.")
            temporary.rename(target)  # Fails if a Windows destination appeared meanwhile.
            os.utime(target, ns=(row["MtimeNs"], row["MtimeNs"])); count += 1
    return dict(Result="PASS", RestoredFiles=count, Prefix=prefix)

def main() -> None:
    parser = argparse.ArgumentParser(description="Consolidar temporales sin perder el historial ni datos del usuario.")
    parser.add_argument("command", choices=["archive", "verify", "restore"])
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1], help="Raíz emu del repositorio")
    parser.add_argument("--apply", action="store_true", help="Archivar, verificar y quitar copias sueltas; por defecto solo informa")
    parser.add_argument("--archive", type=Path); parser.add_argument("--prefix", help="Ruta original out/... o dist/... para restaurar")
    args = parser.parse_args(); root = args.root.resolve()
    if args.command == "archive":
        if not (root / "RetroEmu.slnx").is_file():
            parser.error("--root debe señalar la raíz emu del repositorio.")
        result = archive_history(root, args.apply)
    elif args.command == "verify":
        if args.archive is None: parser.error("Falta --archive")
        index = verify(args.archive); result = dict(Result="PASS", Files=len(index["Files"]), UniqueFiles=index["UniqueFiles"])
    else:
        if args.archive is None or args.prefix is None: parser.error("Faltan --archive y --prefix")
        result = restore(root, args.archive, args.prefix)
    print(json.dumps(result, ensure_ascii=False, indent=2))

if __name__ == "__main__":
    main()
