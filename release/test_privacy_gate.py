import io
import unittest
import zipfile
from privacy_gate import scan_bytes

class PrivacyTests(unittest.TestCase):
    def test_windows_paths_and_encodings(self):
        text='C:'+chr(92)+'Users'+chr(92)+'FixtureUser'+chr(92)+'sample.txt'
        for encoding in ('utf-8','utf-16-le'):
            self.assertTrue(scan_bytes('sample',text.encode(encoding)))
        self.assertTrue(scan_bytes('sample',b'x'+text.encode('utf-16-le')))
    def test_nested_archive(self):
        inner=io.BytesIO()
        with zipfile.ZipFile(inner,'w') as z:z.writestr('secret', 'gh'+'p_'+'a'*36)
        outer=io.BytesIO()
        with zipfile.ZipFile(outer,'w') as z:z.writestr('nested.zip',inner.getvalue())
        self.assertTrue(scan_bytes('release.zip',outer.getvalue()))
    def test_unsafe_archive(self):
        data=io.BytesIO()
        with zipfile.ZipFile(data,'w') as z:z.writestr('../escape','sample')
        self.assertTrue(scan_bytes('release.zip',data.getvalue()))
    def test_public_id_is_allowed(self):
        self.assertFalse(scan_bytes('settings.json',b'{"ApplicationId":"1563519446503522344"}'))
    def test_helper_excluded(self):
        self.assertTrue(scan_bytes('standalone/apphelp.dll',b'fixture'))

if __name__=='__main__':unittest.main()
