"""Run the bounded fortress approach/holding acceptance against one player build."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys


def main():
    root = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--binary', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--resume', action='store_true')
    args = parser.parse_args()
    binary, output = args.binary.resolve(), args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    assembly = hashlib.sha256((binary.parent / (binary.stem+'_Data/Managed/Assembly-CSharp.dll')).read_bytes()).hexdigest()
    matrix = dict(passed=False, assemblySha256=assembly, runup=.9, assist=True, cases=[])
    rows = []
    for offset in (45, 315, 225, 90, 135, 180, 270, 0):
        step = 5 if offset == 0 else 45
        directory = output / f'body-{offset}'
        summary_path = directory / 'summary.json'
        audit_path = directory / 'traction/report.json'
        reusable = False
        if args.resume and summary_path.exists() and audit_path.exists():
            old, audit = json.loads(summary_path.read_text()), json.loads(audit_path.read_text())
            reusable = old.get('passed') is True and old.get('assemblySha256') == assembly and audit.get('passed') is True and len(audit['cases']) == 4*360//step and all(c['passed'] and abs(c['bodyOffset']-offset)<.001 and abs(c['runup']-.9)<.001 and c['assist'] for c in audit['cases'])
        if not reusable:
            env = os.environ.copy()
            env.update(TCR_TRACTION_ASSIST='1', TCR_TRACTION_BODY_OFFSET=str(offset), TCR_TRACTION_ANGLE_STEP=str(step), TCR_TRACTION_RUNUPS='0.9')
            for key in ('TCR_TRACTION_CASE', 'TCR_TRACTION_BURST'):
                env.pop(key, None)
            subprocess.run([sys.executable, str(root/'tools/verify_vehicle_physics.py'), '--binary', str(binary), '--output', str(directory), '--suites', 'traction', '--timeout', '300'], env=env, check=True)
        audit = json.loads(audit_path.read_text())
        assert audit['passed'] and len(audit['cases']) == 4*360//step
        rows.extend(audit['cases'])
        matrix['cases'].append(dict(bodyOffset=offset, worldAngleStep=step, passed=True, report=str(audit_path.relative_to(output))))
        (output/'matrix.json').write_text(json.dumps(matrix, indent=2)+'\n')
    assert len(rows) == 512 and all(c['passed'] for c in rows)
    matrix['passed'] = True
    (output/'matrix.json').write_text(json.dumps(matrix, indent=2)+'\n')
    result = dict(passed=True, assemblySha256=assembly, cases=len(rows), runup=.9, assist=True, capInitialState='fully charged', worldAngleStep=5, otherBodyOffsetsAngleStep=45, maxLateral=max(c['lateral'] for c in rows), maxHoldDrift=max(c['holdDrift'] for c in rows), maxFinalSpeed=max(c['finalSpeed'] for c in rows), maxPower=max(c['peakPower'] for c in rows), matrix='matrix.json')
    (output/'summary.json').write_text(json.dumps(result, indent=2)+'\n')
    print(json.dumps(result), flush=True)


if __name__ == '__main__':
    main()
