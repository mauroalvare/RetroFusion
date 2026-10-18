"""Safe extraction of game archives (.zip with nested zips, .rar/.7z via 7-Zip).

Nothing extracted is ever executed: files are only read as data.
"""

import io
import os
import shutil
import subprocess
import zipfile

JUNK_EXT = {".url", ".txt", ".nfo", ".diz", ".htm", ".html", ".pdf", ".jpg", ".png", ".gif", ".ini", ".bat"}
ARCHIVE_EXT = {".zip", ".rar", ".7z"}
MAX_TOTAL = 256 * 1024 * 1024   # refuse archive bombs


def find_7z():
    exe = shutil.which("7z") or shutil.which("7za") or shutil.which("7zz")
    if exe:
        return exe
    for p in (r"C:\Program Files\7-Zip\7z.exe", r"C:\Program Files (x86)\7-Zip\7z.exe",
              "/usr/bin/7z", "/usr/local/bin/7z", "/opt/homebrew/bin/7z"):
        if os.path.exists(p):
            return p
    return None


def _safe_name(name):
    name = name.replace("\\", "/")
    parts = [p for p in name.split("/") if p not in ("", ".", "..")]
    return "/".join(parts)


def _write(out_dir, rel, data, written):
    rel = _safe_name(rel)
    if not rel:
        return
    path = os.path.join(out_dir, *rel.split("/"))
    if not os.path.abspath(path).startswith(os.path.abspath(out_dir)):
        return
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as f:
        f.write(data)
    written.append(path)


def _extract_zip_bytes(data, out_dir, prefix, written, depth, budget):
    with zipfile.ZipFile(io.BytesIO(data)) as z:
        for info in z.infolist():
            if info.is_dir():
                continue
            budget[0] += info.file_size
            if budget[0] > MAX_TOTAL:
                raise ValueError("El archivo comprimido es demasiado grande (posible bomba zip).")
            blob = z.read(info)
            name = info.filename
            ext = os.path.splitext(name)[1].lower()
            if ext in JUNK_EXT:
                continue
            if ext == ".zip" and depth < 4:
                _extract_zip_bytes(blob, out_dir, prefix, written, depth + 1, budget)
                continue
            _write(out_dir, os.path.basename(name), blob, written)


def extract(path, out_dir):
    """Extract an archive (or copy a plain file) into out_dir.

    Returns the list of game files written. Nested zips are flattened and
    readme/link files are skipped.
    """
    os.makedirs(out_dir, exist_ok=True)
    written = []
    ext = os.path.splitext(path)[1].lower()
    if ext == ".zip":
        with open(path, "rb") as f:
            _extract_zip_bytes(f.read(), out_dir, "", written, 0, [0])
    elif ext in (".rar", ".7z"):
        exe = find_7z()
        if not exe:
            raise RuntimeError("Para abrir %s hace falta 7-Zip (https://www.7-zip.org) o p7zip en Linux." % ext)
        tmp = out_dir + "_tmp7z"
        shutil.rmtree(tmp, ignore_errors=True)
        os.makedirs(tmp)
        res = subprocess.run([exe, "x", "-y", "-o" + tmp, path], capture_output=True, text=True)
        if res.returncode != 0:
            raise RuntimeError("7-Zip no pudo extraer %s: %s" % (path, res.stderr.strip()[:300]))
        for root, _, files in os.walk(tmp):
            for name in files:
                src = os.path.join(root, name)
                e = os.path.splitext(name)[1].lower()
                if e in JUNK_EXT:
                    continue
                with open(src, "rb") as f:
                    blob = f.read()
                if e == ".zip":
                    _extract_zip_bytes(blob, out_dir, "", written, 1, [0])
                else:
                    rel = os.path.relpath(src, tmp)
                    _write(out_dir, rel, blob, written)
        shutil.rmtree(tmp, ignore_errors=True)
    else:
        with open(path, "rb") as f:
            _write(out_dir, os.path.basename(path), f.read(), written)
    return written
