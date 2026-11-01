"""Keep bundled manual links local; point source-only references at the release tag."""
import re
import subprocess
import sys
from pathlib import Path
from urllib.parse import quote, unquote


def bundle(emu, output, version):
    emu, output = Path(emu).resolve(), Path(output).resolve()
    repo = emu.parent
    tracked = set(subprocess.check_output(['git', '-C', str(repo), 'ls-files'], text=True).splitlines())
    pattern = re.compile(r'(!?\[[^\]]*\]\()([^\)]+)(\))')
    changed = 0
    for packaged in [output / 'README.md', *(output / 'docs').glob('*.md')]:
        source = emu / packaged.relative_to(output)
        if not source.is_file():
            raise ValueError(f'No source for bundled manual: {packaged.name}')

        def rewrite(match):
            nonlocal changed
            target = match[2].strip('<>')
            path, separator, fragment = target.partition('#')
            if not path or re.match(r'[A-Za-z][\w+.-]*:', path):
                return match[0]
            if (packaged.parent / unquote(path)).exists():
                return match[0]
            original = (source.parent / unquote(path)).resolve()
            if not original.is_relative_to(repo) or not original.exists():
                raise ValueError(f'Unresolved manual link: {source.name}: {target}')
            relative = original.relative_to(repo).as_posix()
            if relative not in tracked and not any(p.startswith(relative + '/') for p in tracked):
                raise ValueError(f'Local-only manual link: {relative}')
            if match[1].startswith('!'):
                url = f'https://raw.githubusercontent.com/mauroalvare/RetroFusion/v{version}/{quote(relative, safe="/")}'
            else:
                kind = 'tree' if original.is_dir() else 'blob'
                url = f'https://github.com/mauroalvare/RetroFusion/{kind}/v{version}/{quote(relative, safe="/")}'
            changed += 1
            return match[1] + url + (separator + fragment if separator else '') + match[3]

        text = packaged.read_text(encoding='utf-8-sig')
        packaged.write_text(pattern.sub(rewrite, text), encoding='utf-8')
    print(f'Manual: {changed} source references pinned to v{version}')


if __name__ == '__main__':
    bundle(*sys.argv[1:])
