"""Pack a publish folder into .tar.gz with Unix permissions.

Executables (no extension, or native .so/.dylib libraries) get mode 755, the rest 644,
so the package runs right after extracting on Linux or macOS.
usage: python make_tar.py <folder> <output.tar.gz>
"""

import os
import sys
import tarfile


def main(folder, output):
    base = os.path.basename(os.path.normpath(folder))

    def fix(info):
        name = os.path.basename(info.name)
        if info.isdir():
            info.mode = 0o755
        elif "." not in name or name.endswith((".so", ".dylib")) or ".so." in name:
            info.mode = 0o755
        else:
            info.mode = 0o644
        info.uid = info.gid = 0
        info.uname = info.gname = ""
        return info

    with tarfile.open(output, "w:gz") as tar:
        tar.add(folder, arcname=base, filter=fix)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
