"""Create local release candidates only. Never installs or publishes."""
import argparse
import hashlib
import json
import re
import shutil
import subprocess
import sys
import tempfile
import zipfile
from pathlib import Path
from privacy_gate import scan_tree, scan_bytes

VERSION='0.1.0-beta.2'
PREFIX='UmaPresence-v'+VERSION

def digest(data): return hashlib.sha256(data).hexdigest()
def put(path,data):
    path.parent.mkdir(parents=True,exist_ok=True)
    path.write_bytes(data)
def encoded(value): return (json.dumps(value,indent=2,ensure_ascii=False)+'\n').encode('utf-8')
def exact(root,relative):
    if not isinstance(relative,str) or '\\' in relative or ':' in relative or relative.startswith('/') or any(part in ('..','.') for part in relative.split('/')): raise ValueError('Invalid allowlist path')
    result=root/relative
    if any(p.is_symlink() for p in [result,*result.parents]): raise ValueError('Symlink in input')
    if not result.is_file(): raise ValueError('Missing allowlisted input: '+relative)
    return result

def require_clean(root,allowed):
    findings=scan_tree(root,allowed)
    if findings: raise ValueError('Privacy/staging gate failed:\n'+'\n'.join(findings))

def archive(root,target):
    with zipfile.ZipFile(target,'x',zipfile.ZIP_DEFLATED,compresslevel=9) as out:
        for p in sorted(root.rglob('*')):
            if p.is_file():
                info=zipfile.ZipInfo(p.relative_to(root).as_posix(),(2026,1,1,0,0,0));info.compress_type=zipfile.ZIP_DEFLATED;info.external_attr=0o100644<<16
                out.writestr(info,p.read_bytes())
    findings=scan_bytes(target.name,target.read_bytes())
    if findings: raise ValueError('Archive privacy gate failed:\n'+'\n'.join(findings))

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--root',type=Path,default=Path(__file__).resolve().parents[1])
    parser.add_argument('--output',type=Path)
    parser.add_argument('--hachimi-variant', choices=('V1','V2'))
    parser.add_argument('--hachimi-loader-sha256')
    parser.add_argument('--hachimi-apphelp-sha256')
    args=parser.parse_args()
    supplied=(args.hachimi_loader_sha256,args.hachimi_apphelp_sha256)
    if args.hachimi_variant=='V2':
        if not all(value and re.fullmatch('[a-fA-F0-9]{64}',value) for value in supplied):
            parser.error('V2 requires both observed Hachimi fingerprints')
    elif any(supplied):
        parser.error('Only V2 may override the bundled Hachimi reference')
    root=args.root.resolve();out=(args.output or root/'dist'/'public-candidate').resolve()
    if out.exists(): raise ValueError('Output must be a fresh directory; choose another --output')
    sources=json.loads((root/'release/source-files.json').read_text(encoding='utf-8-sig'))
    mappings=json.loads((root/'release/binary-files.json').read_text(encoding='utf-8-sig'))
    if len(sources)!=len(set(sources)): raise ValueError('Duplicate source input')
    source_hashes={name:digest(exact(root,name).read_bytes()) for name in sorted(sources)}
    revision=digest(encoded(source_hashes))
    out.mkdir(parents=True)
    source=out/'reviewed-source';binary=out/'binary-staging';source.mkdir();binary.mkdir()
    for name in sources: put(source/name,exact(root,name).read_bytes())
    # A fresh repository has no inherited private history.
    extras={'.gitignore':'.gitignore.public','README.md':'release/README.md','BUILD.md':'release/BUILD.md'}
    for dest,origin in extras.items(): put(source/dest,exact(root,origin).read_bytes())
    for dest,origin in mappings.items():
        exact(root,origin)
        if dest.startswith('/') or '..' in Path(dest).parts or '\\' in dest or ':' in dest: raise ValueError('Unsafe binary destination')
        put(binary/dest,exact(root,origin).read_bytes())
    reference=binary/'standalone/loader-provenance.json'
    provenance=json.loads(reference.read_text(encoding='utf-8-sig'))
    if args.hachimi_variant=='V2':
        provenance['verifiedHachimiSha256']=supplied[0].lower()
        provenance['verifiedHachimiHelperSha256']=supplied[1].lower()
    if args.hachimi_variant:
        put(reference,encoded(provenance))
    total=sum(p.stat().st_size for p in binary.rglob('*') if p.suffix.lower() in ('.dll','.exe'))
    if total>20*1024*1024: raise ValueError('20 MiB binary budget exceeded')
    files={p.relative_to(binary).as_posix():digest(p.read_bytes()) for p in sorted(binary.rglob('*')) if p.is_file()}
    profile=json.loads((binary/'standalone/game-profile.json').read_text(encoding='utf-8-sig'))
    checks=json.loads((root/'release/acceptance-template.json').read_text())
    policy=root/'dist/public-build/UmapyonPresence.Tests.exe'
    result=subprocess.run([str(policy),'--public-release'],capture_output=True,text=True,timeout=90)
    if result.returncode: raise ValueError('Public policy checks failed; inspect local test output')
    checks.insert(0,{'group':'automated','check':'Public defaults, capability gate and settings controls','status':'passed','evidence':{'testBinarySha256':digest(policy.read_bytes()),'output':result.stdout.splitlines()}})
    checks.insert(1,{'group':'automated','check':'Staged file allowlists, credentials, paths and nested archive privacy','status':'passed','evidence':'Packager fails before completion on any finding'})
    checks.insert(2,{'group':'automated','check':'Release binary size at most 20 MiB','status':'passed','evidence':{'bytes':total}})
    acceptance={'version':VERSION,'sourceRevision':revision,'publicationReady':False,'binaries':{n:h for n,h in files.items() if n.endswith(('.exe','.dll'))},'gameBuild':{'unity':profile['unity']['sha256'],'assembly':profile['assembly']['sha256']},'checks':checks,'notes':'Candidate only. Previous private-build live observations are historical and do not certify these hashes.'}
    if args.hachimi_variant:
        acceptance['hachimiReference']={
            'variant':args.hachimi_variant,
            'source':'locally observed' if args.hachimi_variant=='V2' else 'original Pisa reference',
            'unitySha256':provenance['verifiedHachimiSha256'],
            'apphelpSha256':provenance['verifiedHachimiHelperSha256'],
        }
    regressions=[
        ('--rotation-regression','Identity-aware race page rotation'),
        ('--compatibility-regression','Local Standalone compatibility and basic presence fallback'),
        ('--standalone-update-regression','Signed Standalone update, validation, interrupted install and rollback'),
        ('--help-regression','Help topic switching, layout and in-app navigation'),
    ]
    if (policy.parent/'dependencies/cellar-v2.0.0.dll').exists(): regressions.append(('--installer-regression','Simulated install, switch, interruption recovery and uninstall'))
    else: checks.append({'group':'automated','check':'Public installer fixtures with verified helper','status':'untested','evidence':None})
    native=json.loads((binary/'verification/native-policy-check.json').read_text(encoding='utf-8-sig'))
    if native['status']!='passed' or native['adapterSha256']!=source_hashes['standalone/playback.c'] or native['testSourceSha256']!=source_hashes['standalone/tests/playback-parameters-test.c']:raise ValueError('Missing or stale native policy checks')
    checks.append({'group':'automated','check':'Public native command and seek parameter adapter','status':'passed','evidence':native})
    for flag, description in regressions:
        result=subprocess.run([str(policy),flag],capture_output=True,text=True,timeout=120)
        status='passed' if result.returncode==0 else 'failed'
        # Output may contain a local path on failure; never copy exception text.
        checks.append({'group':'automated','check':description,'status':status,'evidence':{'testBinarySha256':digest(policy.read_bytes()),'passedAssertions':sum(line.startswith('PASS:') for line in result.stdout.splitlines()),'scope':'isolated fixture; not a real game launch'}})
        if result.returncode: raise ValueError('Automated regression failed: '+flag)
    put(binary/'acceptance.json',encoded(acceptance))
    put(binary/'release-manifest.json',encoded({'version':VERSION,'sourceRevision':revision,'binaryBytes':total,'files':files,'experimentalAudio':False,'duplicateCasting':True}))
    allowed=set(mappings)|{'acceptance.json','release-manifest.json','checksums.json'}
    put(binary/'checksums.json',encoded({p.relative_to(binary).as_posix():digest(p.read_bytes()) for p in sorted(binary.rglob('*')) if p.is_file()}))
    require_clean(source,set(sources)|set(extras))
    require_clean(binary,allowed)
    suffix='-'+args.hachimi_variant if args.hachimi_variant else ''
    binary_zip=out/(PREFIX+suffix+'-jp-dmm-win-x64.zip');source_zip=out/(PREFIX+'-source.zip')
    archive(binary,binary_zip);archive(source,source_zip)
    sums=''.join(digest(p.read_bytes())+'  '+p.name+'\n' for p in (binary_zip,source_zip))
    (out/'SHA256SUMS.txt').write_text(sums,encoding='utf-8')
    (out/'RELEASE-NOTES.md').write_bytes((root/'release/RELEASE-NOTES.md').read_bytes())
    with zipfile.ZipFile(binary_zip) as z: z.extractall(out/'extracted-smoke-test')
    require_clean(out/'extracted-smoke-test',allowed)
    smoke=out/'extracted-smoke-test'
    result=subprocess.run([str(smoke/'UmaPresence.exe'),'--release-self-test'],cwd=smoke,timeout=60)
    if result.returncode:raise ValueError('Extracted-package offline self-test failed')
    report=json.loads((smoke/'release-smoke.json').read_text())
    if report['status']!='passed':raise ValueError('Invalid package self-test result')
    put(out/'extracted-package-check.json',encoded(report))
    print(json.dumps({'artifacts':str(out),'sourceRevision':revision,'binaryBytes':total,'privacy':'passed','publicationReady':False},indent=2))

if __name__=='__main__':main()
