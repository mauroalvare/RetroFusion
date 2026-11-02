"""Exercise POSIX preflight with fake OS commands and owned files; never runs an emulator or installer."""
import os
import importlib.util
from pathlib import Path
import shutil
import subprocess
import tarfile
import tempfile
import unittest
spec=importlib.util.spec_from_file_location('retro_make_tar',Path(__file__).with_name('make_tar.py'))
module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module);make_tar=module.main

class LinuxLauncherTests(unittest.TestCase):
    def setUp(self):
        self.sh = shutil.which('sh') or ('C:/Program Files/Git/bin/sh.exe' if os.name == 'nt' else None)
        if not self.sh or not Path(self.sh).is_file(): self.skipTest('No existing POSIX shell; no downloads.')
        self.temp = tempfile.TemporaryDirectory(prefix='retro-linux-launcher-')
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)/'own package with spaces'; self.root.mkdir()
        self.bin = Path(self.temp.name)/'bin'; self.bin.mkdir()
        self.script = self.root/'RetroEmu.sh'
        self.script.write_bytes(Path(__file__).with_name('RetroEmu.sh').read_bytes())
        self.write('uname', 'if [ "$1" = -m ]; then printf "x86_64\\n"; else printf "Linux\\n"; fi')
        self.write('getconf', 'printf "glibc 2.38\\n"')
        self.write('ldd', 'printf "libc.so.6 => /lib/libc.so.6\\n"')
        for f in ['libhostfxr.so','libhostpolicy.so','libcoreclr.so','RetroEmu.dll','Retro.Core.dll','libraylib.so','libcimgui.so']:
            (self.root/f).write_text('Own placeholder',encoding='utf-8')
        (self.root/'RetroEmu').write_text('#!/bin/sh\nprintf "%s\\n" "$@" > invoked.txt\n',encoding='utf-8',newline='\n')
        (self.root/'RetroEmu').chmod(0o755)
        self.env = dict(os.environ, DISPLAY=':99', PATH=str(self.bin)+os.pathsep+os.environ.get('PATH',''))
    def write(self, name, content):
        p=self.bin/name; p.write_text('#!/bin/sh\n'+content+'\n',encoding='utf-8',newline='\n');p.chmod(0o755)
    def run_check(self, *args):
        # Git for Windows prepends its own uname. Reapply the fixture PATH inside that shell.
        prefix='$(cygpath -u "$1")' if os.name=='nt' else '$1'
        wrapper='PATH="'+prefix+':$PATH"; export PATH; shift; exec /bin/sh "$@"'
        return subprocess.run([self.sh,'-c',wrapper,'qa',str(self.bin),str(self.script),*args],env=self.env,cwd=self.root,
            text=True,encoding='utf-8',capture_output=True,timeout=10)
    def test_syntax_and_complete_check_never_start_the_program(self):
        self.assertEqual(subprocess.run([self.sh,'-n',str(self.script)],capture_output=True).returncode,0)
        r=self.run_check('--check','--language','en')
        self.assertEqual(r.returncode,0,r.stderr+r.stdout);self.assertIn('were not checked',r.stdout)
        self.assertFalse((self.root/'invoked.txt').exists())
    def test_old_or_unknown_glibc_cannot_be_mistaken_for_a_usable_graphics_runtime(self):
        for version in ['2.35','2.37','unknown','2.8','']:
            with self.subTest(version=version):
                self.write('getconf',f'printf "glibc {version}\\n"')
                r=self.run_check('--check','--language','en')
                self.assertEqual(r.returncode,2);self.assertIn('2.38',r.stdout);self.assertFalse((self.root/'invoked.txt').exists())
    def test_other_architectures_require_another_package(self):
        self.write('uname','printf "aarch64\\n"');r=self.run_check('--check','--language','en')
        self.assertEqual(r.returncode,2);self.assertIn('x86_64',r.stdout)
    def test_missing_program_file_is_not_a_request_for_a_system_installer(self):
        (self.root/'libcimgui.so').unlink();r=self.run_check('--check','--language','en')
        self.assertEqual(r.returncode,2);self.assertIn('libcimgui.so',r.stdout);self.assertIn('complete copy',r.stdout)
    def test_missing_native_dependency_is_actionable_and_never_executes_output(self):
        self.write('ldd','printf "libX11.so.6 => not found; touch SHOULD_NOT_EXIST\\n"')
        r=self.run_check('--check','--language','en')
        self.assertEqual(r.returncode,2);self.assertIn('official distribution packages',r.stdout)
        self.assertFalse((self.root/'SHOULD_NOT_EXIST').exists());self.assertFalse((self.root/'invoked.txt').exists())
    def test_headless_wayland_alone_is_not_presented_as_a_checked_x11_desktop(self):
        self.env['DISPLAY']='';self.env['WAYLAND_DISPLAY']='wayland-0';r=self.run_check('--check','--language','en')
        self.assertEqual(r.returncode,2);self.assertIn('XWayland',r.stdout)
    def test_a_failed_dependency_checker_does_not_count_as_a_success(self):
        self.write('ldd','printf "Own invalid ELF diagnostic\\n"; exit 1')
        r=self.run_check('--check','--language','en');self.assertEqual(r.returncode,2)
        self.assertIn('could not be checked',r.stdout);self.assertFalse((self.root/'invoked.txt').exists())
    def test_missing_library_detection_does_not_depend_on_the_desktop_language(self):
        self.env['LANG']='es_UY.UTF-8'
        self.write('ldd','if [ "$LC_ALL" = C ]; then printf "libX11.so.6 => not found\\n"; else printf "libX11.so.6 => falta\\n"; fi')
        r=self.run_check('--check');self.assertEqual(r.returncode,2);self.assertIn('Faltan bibliotecas',r.stdout)
    def test_start_preserves_arguments_with_spaces_and_uses_its_own_folder(self):
        r=self.run_check('--language','en','--games-folder','own games with spaces','$(touch SHOULD_NOT_EXIST)')
        self.assertEqual(r.returncode,0,r.stderr+r.stdout)
        self.assertEqual((self.root/'invoked.txt').read_text().splitlines(),['--language','en','--games-folder','own games with spaces','$(touch SHOULD_NOT_EXIST)'])
        self.assertFalse((self.root/'SHOULD_NOT_EXIST').exists())
    def test_spanish_is_complete_and_the_package_keeps_posix_permissions(self):
        r=self.run_check('--comprobar');self.assertEqual(r.returncode,0);self.assertIn('No se inició',r.stdout)
        archive=Path(self.temp.name)/'own.tar.gz';make_tar(self.root,archive)
        with tarfile.open(archive) as tar:
            self.assertEqual(tar.getmember(self.root.name+'/RetroEmu.sh').mode,0o755)
            self.assertEqual(tar.getmember(self.root.name+'/RetroEmu').mode,0o755)
            self.assertEqual(tar.getmember(self.root.name+'/RetroEmu.dll').mode,0o644)

if __name__=='__main__':unittest.main()
