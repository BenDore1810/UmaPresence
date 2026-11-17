"""Inspect bounded windows from an on-disk PE image using !finish-code RVAs.

Reads files only. Does not attach to, inject into, invoke, or restart the game.
The windows are research material, not evidence of a finish-margin formula.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import struct

METHODS = {'GetHorseLengthByOrder': 1, 'GetHorseLengthString': 2,
           'GetHorseLengthAmericaString': 1, 'GetHorseLengthFrenchString': 1,
           'InitFinishInfo': 5}

def parse_report(text):
    match = re.search(r'(?:^|\n)QUERY !finish-code\r?\n(.*?)(?=\r?\nQUERY |\Z)', text, re.S)
    if not match:
        raise ValueError('No !finish-code response block; the loaded bridge may predate this diagnostic')
    block = match[1]
    if not re.search(r'^MODULE GameAssembly\.dll\r?$', block, re.M):
        raise ValueError('Finish-code module locator is unavailable in this response')
    def number(name):
        values = re.findall(r'^' + name + r' (0x[0-9A-Fa-f]{8})\r?$', block, re.M)
        if len(values) != 1:
            raise ValueError('Missing or ambiguous module ' + name)
        return int(values[0], 16)
    methods = {}
    for name, argc, value in re.findall(r'^FINISH_METHOD (\w+) \((\d+)\) (RVA 0x[0-9A-Fa-f]{8}|UNAVAILABLE)\r?$', block, re.M):
        if name not in METHODS or int(argc) != METHODS[name] or name in methods:
            raise ValueError('Unexpected or duplicate finish-method identity')
        methods[name] = int(value[4:], 16) if value.startswith('RVA ') else None
    if not methods:
        raise ValueError('No supported finish-method locations')
    return {'timestamp': number('TIMESTAMP'), 'image_size': number('IMAGE_SIZE'), 'methods': methods}

class PEImage:
    def __init__(self, stream):
        self.stream = stream
        stream.seek(0, 2)
        self.file_size = stream.tell()
        def read(offset, size):
            if offset < 0 or size < 0 or offset + size > self.file_size:
                raise ValueError('Truncated PE image')
            stream.seek(offset)
            data = stream.read(size)
            if len(data) != size:
                raise ValueError('Truncated PE image')
            return data
        self.read = read
        if read(0, 2) != b'MZ':
            raise ValueError('Not a DOS/PE image')
        pe = struct.unpack('<I', read(0x3c, 4))[0]
        if read(pe, 4) != b'PE\0\0':
            raise ValueError('Invalid PE signature')
        machine, count, self.timestamp = struct.unpack('<HHI', read(pe + 4, 8))
        optional_size = struct.unpack('<H', read(pe + 20, 2))[0]
        if machine != 0x8664 or not 1 <= count <= 96 or optional_size < 112:
            raise ValueError('Unsupported PE architecture or header')
        if struct.unpack('<H', read(pe + 24, 2))[0] != 0x20b:
            raise ValueError('Expected a PE32+ image')
        self.image_size = struct.unpack('<I', read(pe + 24 + 56, 4))[0]
        self.sections = []
        for i in range(count):
            raw = read(pe + 24 + optional_size + i * 40, 40)
            virtual_size, rva, raw_size, raw_offset = struct.unpack_from('<4I', raw, 8)
            flags = struct.unpack_from('<I', raw, 36)[0]
            self.sections.append(dict(name=raw[:8].rstrip(b'\0').decode('ascii', 'replace'),
                virtual_size=virtual_size, rva=rva, raw_size=raw_size, raw_offset=raw_offset, flags=flags))

    def window(self, rva, count):
        if not 4096 <= rva < self.image_size or not 1 <= count <= 4096:
            raise ValueError('RVA or window size outside supported bounds')
        matches = [s for s in self.sections if s['flags'] & 0x20000000 and s['rva'] <= rva < s['rva'] + s['virtual_size']]
        if len(matches) != 1:
            raise ValueError('RVA is not inside one executable section')
        section = matches[0]
        delta = rva - section['rva']
        size = min(count, section['raw_size'] - delta, section['virtual_size'] - delta)
        if size <= 0:
            raise ValueError('RVA has no on-disk code bytes')
        offset = section['raw_offset'] + delta
        return offset, self.read(offset, size), section['name']

    def verify(self, report):
        if self.timestamp != report['timestamp'] or self.image_size != report['image_size']:
            raise ValueError('Loaded-module header does not match the on-disk image; do not interpret these RVAs')

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--report', type=Path, required=True)
    parser.add_argument('--module', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--window', type=int, default=2048, choices=range(1, 4097))
    args = parser.parse_args()
    report = parse_report(args.report.read_text(encoding='utf-8-sig'))
    output = []
    # Validate every requested location before writing any research output.
    with args.module.open('rb') as stream:
        pe = PEImage(stream)
        pe.verify(report)
        for name, rva in report['methods'].items():
            if rva is None:
                continue
            offset, data, section = pe.window(rva, args.window)
            output.append((name, rva, offset, data, section))
    digest = hashlib.sha256()
    with args.module.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(chunk)
    args.output.mkdir(parents=True, exist_ok=True)
    manifest = dict(module=str(args.module.resolve()), module_sha256=digest.hexdigest(),
        timestamp=report['timestamp'], image_size=report['image_size'], window_is_not_function_size=True, methods=[])
    for name, rva, offset, data, section in output:
        filename = name + '.bin'
        (args.output / filename).write_bytes(data)
        manifest['methods'].append(dict(name=name, rva=rva, file_offset=offset, section=section, bytes=len(data), file=filename))
    (args.output / 'manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    print(f'Exported {len(output)} bounded code windows; no conversion rule inferred')

if __name__ == '__main__':
    main()
