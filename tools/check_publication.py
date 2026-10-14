"""Check the actual tracked source files, excluding generated/runtime folders."""
import json
from pathlib import Path
import re
import subprocess

ROOT=Path(__file__).resolve().parents[1]


def main():
    for name in ['LICENSE','NOTICE','THIRD_PARTY_NOTICES.md','KNOWN_ISSUES.md','ROADMAP.md','assets-manifest.json']:
        if not (ROOT/name).is_file():raise SystemExit(f'Missing required publication file: {name}')
    if '半成品' not in (ROOT/'README.md').read_text():raise SystemExit('README must show unfinished project status')
    manifest=json.loads((ROOT/'assets-manifest.json').read_text())
    if manifest['schema']!=1:raise SystemExit('Unknown asset manifest schema')
    result=subprocess.run(['git','ls-files','-z'],cwd=ROOT,capture_output=True,check=False)
    names=[n for n in result.stdout.decode().split('\0') if n] if result.returncode==0 else []
    if not names:
        names=[str(p.relative_to(ROOT)) for p in ROOT.rglob('*') if p.is_file() and not any(x in p.parts for x in ['Library','Temp','Logs','UserSettings','__pycache__','artifacts','builds','.git']) and 'StreamingAssets/replay/' not in str(p)]
    failures=[];total=0
    for name in names:
        p=ROOT/name;total+=p.stat().st_size
        if p.stat().st_size>=50*1024*1024:failures.append(f'Large source file: {name}')
        if any(x in Path(name).parts for x in ['Library','PackageCache','UserSettings','builds','artifacts']):failures.append(f'Private/generated directory tracked: {name}')
        if p.suffix.lower() in ['.sqlite','.db','.7z','.rar','.stp','.step','.sldasm','.sldprt','.ulf','.pem','.pfx','.key']:
            failures.append(f'Excluded resource/credential tracked: {name}')
        if p.suffix in ['.py','.cs','.md','.json','.yml','.yaml','.sh','.asset','.txt']:
            s=p.read_text(errors='replace')
            if re.search(r'/home/' + r'coach/|[A-Za-z]:\\Users\\|gh[pousr]_[A-Za-z0-9]{20,}|-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----',s):failures.append(f'Local path or credential pattern: {name}')
    if failures:raise SystemExit('\n'.join(failures))
    print(json.dumps(dict(passed=True,checkedFiles=len(names),sourceBytes=total,officialDataBundled=False,officialCadBundled=False),indent=2))


if __name__=='__main__':main()
