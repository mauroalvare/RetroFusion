"""Baja las pruebas de conformidad del 6502 (no van en el repositorio):
los primeros N casos de cada archivo de SingleStepTests de Tom Harte (MIT) y la prueba
funcional de Klaus Dormann (GPL). Uso: python tools/bajar-pruebas-6502.py testdata/6502 [N]"""
import os
import sys
import urllib.request
import zlib

dest = sys.argv[1]
N = int(sys.argv[2]) if len(sys.argv) > 2 else 1000
os.makedirs(os.path.join(dest, "harte"), exist_ok=True)
os.makedirs(os.path.join(dest, "dormann"), exist_ok=True)


def first_cases(url, n):
    req = urllib.request.Request(url, headers={"Accept-Encoding": "gzip", "User-Agent": "RetroEmu-tests"})
    with urllib.request.urlopen(req, timeout=60) as resp:
        gz = resp.headers.get("Content-Encoding") == "gzip"
        dec = zlib.decompressobj(16 + zlib.MAX_WBITS) if gz else None
        out = []
        depth = 0
        in_str = False
        esc = False
        count = 0
        started = False
        transferred = 0
        while True:
            chunk = resp.read(65536)
            if not chunk:
                break
            transferred += len(chunk)
            text = (dec.decompress(chunk) if dec else chunk).decode("utf-8")
            for i, ch in enumerate(text):
                if in_str:
                    if esc:
                        esc = False
                    elif ch == "\\":
                        esc = True
                    elif ch == '"':
                        in_str = False
                    continue
                if ch == '"':
                    in_str = True
                elif ch in "[{":
                    depth += 1
                    started = True
                elif ch in "]}":
                    depth -= 1
                    if depth == 1 and ch == "}":
                        count += 1
                        if count == n:
                            out.append(text[: i + 1])
                            return "".join(out) + "\n]\n", transferred
            out.append(text)
        return "".join(out), transferred


total = 0
for op in range(256):
    name = f"{op:02x}.json"
    path = os.path.join(dest, "harte", name)
    if os.path.exists(path):
        continue
    url = f"https://raw.githubusercontent.com/SingleStepTests/65x02/main/6502/v1/{name}"
    data, transferred = first_cases(url, N)
    total += transferred
    with open(path, "w", encoding="utf-8") as f:
        f.write(data)
    if op % 32 == 31:
        print(f"{op + 1}/256 archivos, transferidos {total / 1e6:.1f} MB", flush=True)

for name in ["6502_functional_test.bin", "6502_functional_test.lst"]:
    path = os.path.join(dest, "dormann", name)
    if not os.path.exists(path):
        url = f"https://raw.githubusercontent.com/Klaus2m5/6502_65C02_functional_tests/master/bin_files/{name}"
        urllib.request.urlretrieve(url, path)
print("listo, transferidos", round(total / 1e6, 1), "MB")
