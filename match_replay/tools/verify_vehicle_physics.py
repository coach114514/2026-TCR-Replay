"""Run native scene regressions against a specific candidate player.

The player performs real PhysicsScene simulation, rendering and projectile
queries. This runner only launches suites and records their exit statuses.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import time


def main():
    root = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--binary", type=Path, default=root / "builds/linux/TCRReplay.x86_64")
    parser.add_argument("--output", type=Path, default=root / "artifacts/vehicle-physics/native-final")
    parser.add_argument("--suites", nargs="+", default=["physics", "game", "base-visual", "camera-aim", "rune-auto", "rune-precision", "replay"])
    parser.add_argument("--timeout", type=float, default=240)
    args = parser.parse_args()
    binary, output = args.binary.resolve(), args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    env = os.environ.copy()
    for name in ("GTK_PATH", "GIO_EXTRA_MODULES", "LD_LIBRARY_PATH"):
        env.pop(name, None)
    if subprocess.run(["nvidia-smi", "-L"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL).returncode == 0:
        env.update(__NV_PRIME_RENDER_OFFLOAD="1", __GLX_VENDOR_LIBRARY_NAME="nvidia", __VK_LAYER_NV_optimus="NVIDIA_only")
    assembly = binary.parent / (binary.stem+"_Data") / "Managed/Assembly-CSharp.dll"
    report = {"binary": str(binary), "sha256": hashlib.sha256(binary.read_bytes()).hexdigest(), "assemblySha256": hashlib.sha256(assembly.read_bytes()).hexdigest(), "suites": []}
    for suite in args.suites:
        directory = output / suite
        directory.mkdir(exist_ok=True)
        # A graceful shutdown or a crash after an earlier run must not make a
        # partial or stale acceptance report look like a successful suite.
        for stale in directory.glob("*.json"):
            stale.unlink()
        log = directory / "player.log"
        command = [str(binary), "-force-glcore", "-screen-fullscreen", "0", "-screen-width", "1600", "-screen-height", "900", "-logFile", str(log), f"--{suite}-smoke", str(directory)]
        start = time.monotonic()
        print(f"Running {suite}", flush=True)
        with (directory / "stdout.log").open("wb") as stream:
            try:
                result = subprocess.run(command, env=env, stdout=stream, stderr=subprocess.STDOUT, timeout=args.timeout)
                code = result.returncode
            except subprocess.TimeoutExpired:
                code = -1
        reports = []
        final_reports = [directory / "report.json"]
        if suite == "game":
            final_reports = [directory / "runtime.json"]
        for path in final_reports:
            try:
                result = json.loads(path.read_text())
                reports.append((path.name, result))
            except (OSError, ValueError):
                pass
        markers = {"lidar-api": "LIDAR_API_AUDIT True", "replay-lidar": "REPLAY_LIDAR_AUDIT True", "lidar": "LIDAR_AUDIT True", "traction": "TRACTION_AUDIT True", "traction-safety": "TRACTION_SAFETY_AUDIT True", "physics": "VEHICLE_PHYSICS_AUDIT True", "game": "DEVELOPER_RUNTIME_OK", "base-visual": "BASE_VISUAL_SMOKE_OK", "camera-aim": "CAMERA_AIM_SMOKE_OK", "rune-auto": "RUNE_AUTO_SMOKE_OK", "rune-precision": "RUNE_PRECISION_SMOKE_OK", "replay": "REPLAY_SMOKE_OK"}
        log_text = log.read_text(errors="replace") if log.exists() else ""
        valid_report = bool(reports) and all(r.get("passed") is True and all(c.get("passed", True) is True for c in r.get("cases", [])) for _, r in reports)
        if suite == "game":
            valid_report = bool(reports) and not reports[0][1].get("error")
        if suite == "replay":
            valid_report = True  # This suite reports completion in the log.
        complete = suite not in markers or markers[suite] in log_text
        valid_report = valid_report and complete
        row = {"suite": suite, "passed": code == 0 and valid_report, "exitCode": code, "seconds": round(time.monotonic()-start, 2), "log": str(log.relative_to(output)), "reports": [name for name, _ in reports]}
        report["suites"].append(row)
        report["passed"] = all(r["passed"] for r in report["suites"])
        (output / "summary.json").write_text(json.dumps(report, ensure_ascii=False, indent=2)+"\n")
        print(json.dumps(row), flush=True)
        if not row["passed"]:
            raise SystemExit(1)


if __name__ == "__main__":
    main()
