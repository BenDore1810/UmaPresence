"""Identify the installed Unity IL2CPP binding table without a fixed upstream RVA.

Read-only PE analysis. No attach, injection or game invocation. GPL-3.0-derived
symbol ordering: standalone/upstream/hachimi-edge/src/windows/symbols_impl.rs.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import struct
from inspect_finish_code import PEImage

PIN = '3c48a46925a8f943a80515877a52309e447d2c7f'


def compatibility_hash(data):
    """Match the native and companion read-only PE equivalence check."""
    size = len(data)
    if not 64 <= size <= 0x40000000 or data[:2] != b'MZ':
        raise ValueError('Invalid PE length or header')
    u16 = lambda offset: struct.unpack_from('<H', data, offset)[0]
    u32 = lambda offset: struct.unpack_from('<I', data, offset)[0]
    pe = u32(60)
    if not 64 <= pe <= size - 24 or u32(pe) != 0x4550 or u16(pe + 4) != 0x8664:
        raise ValueError('Invalid PE machine or offset')
    optional, optional_size, count = pe + 24, u16(pe + 20), u16(pe + 6)
    if not 152 <= optional_size <= size - optional or not 1 <= count <= 96 or u16(optional) != 0x20b or u32(optional + 108) < 5:
        raise ValueError('Invalid PE optional header')
    table, headers = optional + optional_size, u32(optional + 60)
    if not table + count * 40 <= headers <= size:
        raise ValueError('Invalid PE section table')
    end = headers
    for index in range(count):
        section = table + index * 40
        length, offset = u32(section + 16), u32(section + 20)
        if length:
            if offset < headers or offset + length > size:
                raise ValueError('Invalid PE raw section')
            end = max(end, offset + length)
    checksum, directory = optional + 64, optional + 144
    certificate, length = u32(directory), u32(directory + 4)
    if certificate or length:
        if certificate < end or certificate % 8 or length < 8 or certificate + length != size:
            raise ValueError('Invalid PE certificate directory')
        cursor = certificate
        while cursor < size:
            remaining = size - cursor
            if remaining < 8:
                raise ValueError('Truncated certificate record')
            record = u32(cursor)
            aligned = (record + 7) & ~7
            if record <= 8 or aligned > remaining or u16(cursor + 4) != 0x200 or u16(cursor + 6) != 2:
                raise ValueError('Invalid certificate record')
            if any(data[cursor + record:cursor + aligned]):
                raise ValueError('Invalid certificate padding')
            cursor += aligned
    view = memoryview(data)
    digest = hashlib.sha256(view[:checksum])
    digest.update(view[checksum + 4:directory])
    digest.update(view[directory + 8:certificate or size])
    return digest.hexdigest()


class Image:
    def __init__(self, path):
        self.path = Path(path)
        self.data = self.path.read_bytes()
        with self.path.open('rb') as stream:
            pe = PEImage(stream)
            self.sections, self.timestamp, self.size = pe.sections, pe.timestamp, pe.image_size
        self.pe = struct.unpack_from('<I', self.data, 0x3c)[0]

    def offset(self, rva, size=1):
        sections = [s for s in self.sections if s['rva'] <= rva and rva + size <= s['rva'] + s['raw_size']]
        if len(sections) != 1:
            raise ValueError('RVA is not in one backed PE section')
        offset = sections[0]['raw_offset'] + rva - sections[0]['rva']
        if offset + size > len(self.data):
            raise ValueError('RVA exceeds file')
        return offset

    def string(self, rva):
        start = self.offset(rva)
        end = self.data.find(b'\0', start, min(start + 256, len(self.data)))
        if end < 0:
            raise ValueError('Unterminated export name')
        return self.data[start:end].decode('ascii')

    def exports(self):
        rva, size = struct.unpack_from('<II', self.data, self.pe + 24 + 112)
        off = self.offset(rva, 40)
        _, _, _, _, _, _, functions, names, table, name_table, ordinals = struct.unpack_from('<IIHHIIIIIII', self.data, off)
        if not 1 <= names <= functions <= 100000:
            raise ValueError('Invalid export directory')
        result = {}
        for i in range(names):
            name_rva = struct.unpack_from('<I', self.data, self.offset(name_table + 4 * i, 4))[0]
            ordinal = struct.unpack_from('<H', self.data, self.offset(ordinals + 2 * i, 2))[0]
            if ordinal >= functions:
                raise ValueError('Invalid export ordinal')
            target = struct.unpack_from('<I', self.data, self.offset(table + ordinal * 4, 4))[0]
            if rva <= target < rva + size:
                continue  # No forwarders in callable runtime API.
            if not any(s['flags'] & 0x20000000 and s['rva'] <= target < s['rva'] + s['virtual_size'] for s in self.sections):
                continue
            result[self.string(name_rva)] = target
        return result


def identify(unity, assembly, symbols):
    exports = assembly.exports()
    matches = []
    for section in unity.sections:
        if not section['flags'] & 0x20000000:
            continue
        start, end = section['raw_offset'], section['raw_offset'] + section['raw_size']
        cursor = start
        while True:
            at = unity.data.find(b'\x48\x8d\x15', cursor, end)
            if at < 0:
                break
            cursor = at + 1
            first = section['rva'] + at - start + 3
            mapping, position = {}, first
            try:
                for index, symbol in enumerate(symbols):
                    offset = unity.offset(position, 4)
                    if unity.data[offset - 3:offset] != b'\x48\x8d\x15':
                        raise ValueError('Binding instruction mismatch')
                    displacement = struct.unpack_from('<i', unity.data, offset)[0]
                    name = unity.string(position + 4 + displacement)
                    if name not in exports or name in mapping.values():
                        raise ValueError('Binding is missing or duplicated')
                    mapping[symbol] = name
                    position += 0x28 if index == 0 else 0x26
                matches.append((first, mapping))
            except (ValueError, UnicodeError, struct.error):
                continue
    if len(matches) != 1:
        raise ValueError(f'Expected one complete verified binding table, found {len(matches)}. No profile generated.')
    return matches[0]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--game', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    source = (root / 'standalone/upstream/hachimi-edge/src/windows/symbols_impl.rs').read_text(encoding='utf-8')
    symbols = re.findall(r'"(il2cpp_[A-Za-z0-9_]+)"', source.split('const SYMBOL_LIST:')[1].split('];', 1)[0])
    unity, assembly = Image(args.game / 'UnityPlayer.dll'), Image(args.game / 'GameAssembly.dll')
    location, mapping = identify(unity, assembly, symbols)
    profile = {'format': 1, 'upstreamCommit': PIN, 'bindingRva': location, 'symbols': mapping}
    for key, image in [('unity', unity), ('assembly', assembly)]:
        profile[key] = {
            'sha256': hashlib.sha256(image.data).hexdigest(),
            'timestamp': image.timestamp,
            'imageSize': image.size,
            'compatibilitySha256': compatibility_hash(image.data),
        }
    args.output.mkdir(parents=True, exist_ok=True)
    (args.output / 'game-profile.json').write_text(json.dumps(profile, indent=2), encoding='utf-8')
    lines = ['/* Generated only after complete PE binding verification. */', '#define PROFILE_FORMAT 1']
    for key in ['unity', 'assembly']:
        for field, value in profile[key].items():
            constant = f'PROFILE_{key}_{field}'.upper()
            lines.append(f'#define {constant} ' + (json.dumps(value) if isinstance(value, str) else f'{value}u'))
    lines += ['typedef struct { const char *name; const char *export_name; } RuntimeSymbol;', 'static const RuntimeSymbol runtime_symbols[] = {']
    lines += ['    {' + json.dumps(name) + ',' + json.dumps(value) + '},' for name, value in mapping.items()]
    lines += ['};']
    (args.output / 'game-profile.h').write_text('\n'.join(lines) + '\n', encoding='utf-8')
    print(f'Verified {len(mapping)} bindings at discovered RVA 0x{location:x}; profile generated.')


if __name__ == '__main__':
    main()
