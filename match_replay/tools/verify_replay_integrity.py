"""Compare packaged replay data/visuals with the previous release and source assets."""
import argparse
import hashlib
import json
from pathlib import Path


def files(root):
    return {str(p.relative_to(root)): hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(root.rglob('*')) if p.is_file() and not p.name.endswith('.meta')}


def main():
    root = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--candidate', type=Path, required=True)
    parser.add_argument('--previous', type=Path, default=root/'builds/linux/TCRReplay_Data/StreamingAssets/replay')
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    source = files(root/'unity/Assets/StreamingAssets/replay')
    before = files(args.previous)
    changes = sorted(name for name in set(source)|set(before) if source.get(name) != before.get(name))
    intentional = [name for name in changes if name.startswith('assets/developer/')]
    unexpected = [name for name in changes if not name.startswith('assets/developer/')]
    match = all(files(args.candidate/platform/'TCRReplay_Data/StreamingAssets/replay') == source for platform in ('linux','windows'))
    report = dict(passed=not unexpected and match, dataFiles=sum(not name.startswith('assets/') for name in before), historicalDataUnchanged=not any(not name.startswith('assets/') for name in unexpected), replayAssetsUnchanged=not any(name.startswith('assets/') for name in unexpected), candidatesMatchSource=match, comparison='Previously published replay data and historical visual assets byte-for-byte; developer rig collision assets intentionally updated', candidate=args.candidate.name, intentionalDeveloperAssetChanges=intentional, unexpectedChanges=unexpected)
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n')
    print(json.dumps({k:v for k,v in report.items() if not isinstance(v,list)}),flush=True)
    if not report['passed']:raise SystemExit(1)


if __name__ == '__main__':
    main()
