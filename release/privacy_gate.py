"""Fail-closed staging privacy checks. Findings never print matched secrets."""
import argparse
import io
import json
import re
import zipfile
from pathlib import Path, PurePosixPath

RULES = {
    'private-key': re.compile(r'-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----'),
    'github-credential': re.compile(r'\b(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{40,})\b'),
    'authorization-value': re.compile(r'(?i)authorization\s*[:=]\s*["\x27]?(?:bearer|basic)\s+[A-Za-z0-9+/_.=-]{16,}'),
    'discord-credential': re.compile(r'\b(?:mfa\.[A-Za-z0-9_-]{50,}|[A-Za-z0-9_-]{24}\.[A-Za-z0-9_-]{6}\.[A-Za-z0-9_-]{27,})\b'),
    'credential-assignment': re.compile(r'(?i)["\x27]?(?:api[_-]?key|access[_-]?token|refresh[_-]?token|client[_-]?secret|password|cookie)["\x27]?\s*[:=]\s*["\x27][^"\x27\r\n]{12,}["\x27]'),
    'absolute-windows-path': re.compile(r'(?i)\b[A-Z]:[\\/](?:[A-Za-z0-9_. -]+[\\/])+[A-Za-z0-9_. -]*'),
    'unc-personal-path': re.compile(r'\\\\[^\s\\]{2,}\\(?:Users|Downloads|Documents|Desktop)\\', re.I),
    'account-record': re.compile(r'(?i)["\x27](?:viewer_id|account_id|trainer_id|trainer_name|trainerName)["\x27]\s*:\s*["\x27]?[A-Za-z0-9\u3000-\u9fff]{3,}'),
}
FORBIDDEN_NAMES = { 'apphelp.dll', 'cellar.dll', 'cellar-v2.0.0.dll', 'gameassembly.dll', 'umamusume.exe' }

def scan_bytes(name, data, depth=0, budget=None):
    if budget is None: budget = [0]
    budget[0] += len(data)
    if budget[0] > 256*1024*1024 or depth > 3:
        return [name+': archive budget exceeded']
    findings=[]
    leaf=PurePosixPath(name).name.lower()
    if leaf in FORBIDDEN_NAMES or leaf.endswith(('.pdb','.log')):
        findings.append(name+': prohibited release file')
    # Both byte alignments cover embedded UTF-16 strings in native/managed files.
    # Keep invalid byte sequences as boundaries so compressed bytes cannot
    # manufacture a path or credential by joining unrelated fragments.
    views=[data.decode('utf-8',errors='replace'), data.decode('utf-16-le',errors='replace'),data[1:].decode('utf-16-le',errors='replace')]
    for rule, pattern in RULES.items():
        if any(pattern.search(view) for view in views): findings.append(name+': '+rule)
    if data.startswith(b'PK\x03\x04') or leaf.endswith('.zip'):
        try:
            with zipfile.ZipFile(io.BytesIO(data)) as archive:
                seen=set()
                for entry in archive.infolist():
                    parts=PurePosixPath(entry.filename).parts
                    if entry.filename in seen or entry.filename.startswith(('/', '\\')) or '\\' in entry.filename or '..' in parts or ':' in entry.filename or (entry.external_attr >> 16) & 0o170000 == 0o120000:
                        findings.append(name+': unsafe archive entry');continue
                    seen.add(entry.filename)
                    if entry.is_dir(): continue
                    if entry.file_size>64*1024*1024 or entry.flag_bits & 1:
                        findings.append(name+': unsupported archive entry');continue
                    findings.extend(scan_bytes(name+'!'+entry.filename,archive.read(entry),depth+1,budget))
        except (zipfile.BadZipFile, RuntimeError): findings.append(name+': invalid archive')
    return sorted(set(findings))

def scan_tree(root, allowed=None):
    root=Path(root)
    actual={p.relative_to(root).as_posix() for p in root.rglob('*') if p.is_file()}
    findings=[]
    if allowed is not None:
        for name in sorted(actual ^ set(allowed)): findings.append(name+': allowlist mismatch')
    for path in sorted(root.rglob('*')):
        if path.is_symlink(): findings.append(path.relative_to(root).as_posix()+': symlink prohibited')
        elif path.is_file(): findings.extend(scan_bytes(path.relative_to(root).as_posix(),path.read_bytes()))
    return sorted(set(findings))

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('path');parser.add_argument('--allowlist');args=parser.parse_args()
    path=Path(args.path)
    findings=scan_tree(path,json.loads(Path(args.allowlist).read_text()) if args.allowlist else None) if path.is_dir() else scan_bytes(path.name,path.read_bytes())
    for finding in findings: print(finding)
    print('Privacy gate: '+('FAILED' if findings else 'passed'))
    raise SystemExit(bool(findings))
