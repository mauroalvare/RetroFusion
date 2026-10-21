"""Register package-owned files so updates can preserve everything supplied by the player."""
import hashlib
import json
import sys
from pathlib import Path

def main(folder, version, runtime):
    root = Path(folder).resolve()
    files = {}
    for path in sorted(root.rglob('*')):
        if path.is_symlink():
            raise ValueError('El paquete no puede contener enlaces')
        if not path.is_file() or path.name == 'actualizacion.json':
            continue
        files[path.relative_to(root).as_posix()] = hashlib.sha256(path.read_bytes()).hexdigest()
    manifest = {'format': 1, 'version': version, 'runtime': runtime, 'files': files}
    (root / 'actualizacion.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')

if __name__ == '__main__':
    main(*sys.argv[1:])
