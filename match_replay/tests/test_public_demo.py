import hashlib
import json
from pathlib import Path
import sqlite3
import tempfile
import unittest

from match_replay.tools.cli import connect,validate
from match_replay.tools.make_demo import generate
from match_replay.tools.import_resources import inspect,install


class PublicDemoTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name);self.source=self.root/'source';generate(self.source)

    def test_generated_matches_are_synthetic_valid_and_deterministic(self):
        second=self.root/'second';generate(second)
        catalog=json.loads((self.source/'catalog.json').read_text())
        self.assertEqual(catalog['source'],'synthetic-demo')
        self.assertEqual(len(catalog['matches']),2)
        for entry in catalog['matches']:
            p=self.source/entry['file'];validate(json.loads(p.read_text()))
            self.assertEqual(p.read_bytes(),(second/entry['file']).read_bytes())
            self.assertEqual(hashlib.sha256(p.read_bytes()).hexdigest(),entry['sha256'])
        manifest=json.loads((self.source/'demo_manifest.json').read_text())
        for name,digest in manifest['files'].items():
            self.assertEqual(hashlib.sha256((self.source/name).read_bytes()).hexdigest(),digest)

    def test_local_import_preserves_replaced_demo(self):
        target=self.root/'installed';generate(target)
        before=(target/'catalog.json').read_bytes()
        report=install(self.source,target,replace_demo=True)
        self.assertEqual(report['matches'],2)
        self.assertEqual((Path(report['backup'])/'catalog.json').read_bytes(),before)
        self.assertEqual(inspect(target)['fields'],2)

    def test_external_pack_cannot_be_overwritten(self):
        target=self.root/'external';generate(target)
        (target/'resource_profile.json').write_text('{"schema":1,"kind":"external-full"}')
        with self.assertRaises(ValueError):install(self.source,target,replace_demo=True)
        self.assertEqual(json.loads((target/'resource_profile.json').read_text())['kind'],'external-full')

    def test_path_escape_and_symlinks_rejected(self):
        catalog=json.loads((self.source/'catalog.json').read_text())
        catalog['matches'][0]['file']='../outside.json'
        (self.source/'catalog.json').write_text(json.dumps(catalog))
        with self.assertRaises(ValueError):inspect(self.source)
        catalog['matches'][0]['file']='matches/demo-1.json'
        (self.source/'catalog.json').write_text(json.dumps(catalog))
        (self.source/'secret-link').symlink_to(self.root)
        with self.assertRaises(ValueError):inspect(self.source)

    def test_duplicate_events_rejected(self):
        data=json.loads((self.source/'matches/demo-1.json').read_text())
        data['events'].insert(1,dict(data['events'][0]))
        with self.assertRaises(ValueError):validate(data)

    def test_source_database_is_read_only(self):
        db=self.root/'example.sqlite'
        with sqlite3.connect(db) as c:c.execute('create table example(value integer)')
        with connect(db) as c:
            with self.assertRaises(sqlite3.OperationalError):c.execute('insert into example values (1)')


if __name__=='__main__':unittest.main()
