import hashlib
import json
from pathlib import Path
import tempfile
import unittest
import zipfile
from workspace_cleanup import archive_history, bounded, restore, verify

class ArchiveTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.root = Path(self.temp.name)
        (self.root/'out/caso').mkdir(parents=True); (self.root/'dist/old').mkdir(parents=True)
        self.content = bytes(range(256))*2048
        (self.root/'out/caso/binary.dll').write_bytes(self.content)
        (self.root/'dist/old/same.dll').write_bytes(self.content)
        (self.root/'out/caso/informe ñ.json').write_text('{"ok":true}',encoding='utf-8')
        (self.root/'src').mkdir(); (self.root/'src/source.cs').write_text('keep')
        (self.root/'out/8086-hardware').mkdir(); (self.root/'out/8086-hardware/suite.gz').write_bytes(b'keep corpus')
    def tearDown(self): self.temp.cleanup()
    def test_dry_run_never_removes_files(self):
        report=archive_history(self.root)
        self.assertFalse(report['Apply']);self.assertEqual(report['Files'],3)
        self.assertTrue((self.root/'out/caso/binary.dll').is_file())
        self.assertFalse((self.root/'out/historial').exists())
    def test_round_trip_preserves_every_byte_and_stores_duplicates_once(self):
        result=archive_history(self.root,True); archive=Path(result['Archive'])
        self.assertEqual(result['DeletedFiles'],3);self.assertEqual(result['UniqueFiles'],2)
        self.assertFalse((self.root/'out/caso').exists());self.assertFalse((self.root/'dist/old').exists())
        self.assertEqual((self.root/'src/source.cs').read_text(),'keep')
        self.assertEqual((self.root/'out/8086-hardware/suite.gz').read_bytes(),b'keep corpus')
        restore(self.root,archive,'out/caso');restore(self.root,archive,'dist/old')
        self.assertEqual((self.root/'out/caso/binary.dll').read_bytes(),self.content)
        self.assertEqual((self.root/'dist/old/same.dll').read_bytes(),self.content)
        self.assertEqual(json.loads((self.root/'out/caso/informe ñ.json').read_text(encoding='utf-8')),{'ok':True})
    def test_restore_refuses_to_overwrite_new_work(self):
        result=archive_history(self.root,True); path=self.root/'out/caso/binary.dll'
        path.parent.mkdir();path.write_bytes(b'new')
        with self.assertRaises(FileExistsError):restore(self.root,Path(result['Archive']),'out/caso')
        self.assertEqual(path.read_bytes(),b'new')
    def test_invalid_paths_are_rejected(self):
        for path in ['../out/test','out/../../src/source.cs','/out/test','C:/out/test','out\\..\\src','src/source.cs']:
            with self.subTest(path=path), self.assertRaises(ValueError):bounded(self.root,path)
    def test_tampered_content_is_not_accepted_as_verified(self):
        result=archive_history(self.root,True); path=Path(result['Archive'])
        with zipfile.ZipFile(path,'a') as store:
            sha=hashlib.sha256(self.content).hexdigest();store.writestr('blobs/'+sha,b'corrupt')
        with self.assertRaises(ValueError):verify(path)

if __name__=='__main__':unittest.main()
