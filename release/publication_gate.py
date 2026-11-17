"""Validate an unpacked candidate before any upload. No publishing credentials."""
import argparse
import hashlib
import json
from pathlib import Path
from privacy_gate import scan_tree

def verify(root):
    findings=scan_tree(root)
    try:
        manifest=json.loads((root/'checksums.json').read_text())
        if {p.relative_to(root).as_posix() for p in root.rglob('*') if p.is_file()} != set(manifest)|{'checksums.json'}: findings.append('unexpected or missing package file')
        for name,expected in manifest.items():
            if name.startswith('/') or '..' in Path(name).parts or ':' in name or chr(92) in name: findings.append('unsafe checksum path');continue
            if hashlib.sha256((root/name).read_bytes()).hexdigest()!=expected:findings.append(name+': checksum mismatch')
        acceptance=json.loads((root/'acceptance.json').read_text())
        release=json.loads((root/'release-manifest.json').read_text())
        if acceptance['sourceRevision']!=release['sourceRevision']:findings.append('source revision mismatch')
        for name,expected in acceptance['binaries'].items():
            if manifest.get(name)!=expected:findings.append('acceptance binary fingerprint mismatch')
        if not acceptance['publicationReady']:findings.append('publication remains disabled')
        for check in acceptance['checks']:
            if check['status']!='passed' or not check.get('evidence'):findings.append(check['group']+': '+check['check']+' is not verified')
    except (ValueError,KeyError,OSError):findings.append('invalid or incomplete release metadata')
    return findings
if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('directory',type=Path);args=parser.parse_args()
    findings=verify(args.directory)
    for finding in findings:print(finding)
    print('Publication gate: '+('BLOCKED' if findings else 'passed'))
    raise SystemExit(bool(findings))
