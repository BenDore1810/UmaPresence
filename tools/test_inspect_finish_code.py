import io
import struct
import unittest
from inspect_finish_code import PEImage, parse_report

REPORT = '''QUERY !finish-code
MODULE GameAssembly.dll
TIMESTAMP 0x12345678
IMAGE_SIZE 0x00004000
FINISH_METHOD GetHorseLengthByOrder (1) RVA 0x00001000
FINISH_METHOD InitFinishInfo (5) UNAVAILABLE
'''

def fixture(flags=0x60000020):
    data = bytearray(0x240)
    data[:2] = b'MZ'; struct.pack_into('<I', data, 0x3c, 0x80)
    data[0x80:0x84] = b'PE\0\0'
    struct.pack_into('<HHI', data, 0x84, 0x8664, 1, 0x12345678)
    struct.pack_into('<H', data, 0x94, 0x70)
    struct.pack_into('<H', data, 0x98, 0x20b)
    struct.pack_into('<I', data, 0x98 + 56, 0x4000)
    data[0x108:0x110] = b'.text\0\0\0'
    struct.pack_into('<4I', data, 0x110, 0x40, 0x1000, 0x40, 0x200)
    struct.pack_into('<I', data, 0x108 + 36, flags)
    data[0x200:] = bytes(range(64))
    return io.BytesIO(data)

class FinishCodeTests(unittest.TestCase):
    def test_report_and_bounded_code(self):
        report = parse_report(REPORT)
        pe = PEImage(fixture()); pe.verify(report)
        offset, data, section = pe.window(report['methods']['GetHorseLengthByOrder'], 2048)
        self.assertEqual((offset, data, section), (0x200, bytes(range(64)), '.text'))
        self.assertIsNone(report['methods']['InitFinishInfo'])

    def test_build_mismatch(self):
        pe = PEImage(fixture())
        for report in (REPORT.replace('12345678', '12345679'), REPORT.replace('00004000', '00005000')):
            with self.assertRaises(ValueError): pe.verify(parse_report(report))

    def test_invalid_or_ambiguous_report(self):
        for report in (REPORT.replace('GetHorseLengthByOrder (1)', 'OtherMethod (1)'),
                       REPORT + 'FINISH_METHOD GetHorseLengthByOrder (1) RVA 0x00001000\n',
                       REPORT.replace('QUERY !finish-code', 'QUERY !RaceUtil'),
                       REPORT.replace('GetHorseLengthByOrder (1)', 'GetHorseLengthByOrder (2)')):
            with self.assertRaises(ValueError): parse_report(report)

    def test_noncode_and_bad_bounds(self):
        with self.assertRaises(ValueError): PEImage(fixture(0x40000040)).window(0x1000, 4)
        for rva, size in ((0xfff, 4), (0x4000, 4), (0x1040, 4), (0x1000, 4097), (0x1000, 0)):
            with self.assertRaises(ValueError): PEImage(fixture()).window(rva, size)

    def test_truncated_file(self):
        with self.assertRaises(ValueError): PEImage(io.BytesIO(b'MZ'))
        data = fixture().getvalue()[:-1]
        with self.assertRaises(ValueError): PEImage(io.BytesIO(data)).window(0x1000, 64)

if __name__ == '__main__': unittest.main()
