"""Verify modal input through real X11 events against an opt-in player probe.

Run from any directory. No pip dependencies; requires libX11/libXtst and a
graphical session. Only the test's own player receives keyboard/mouse input.
"""
import argparse
import ctypes as c
import ctypes.util
import hashlib
import json
import math
import os
from pathlib import Path
import subprocess
import time


def main():
    root = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--binary", type=Path, default=root / "match_replay/builds/linux/TCRReplay.x86_64")
    parser.add_argument("--output", type=Path, default=root / "match_replay/artifacts/ui-modal/input")
    parser.add_argument("--size", type=int, nargs=2, default=[1600,900], metavar=("WIDTH","HEIGHT"))
    parser.add_argument("--aim-rune", action="store_true", help="Verify right-click, ROI, rune single/loop buttons and startup delay")
    parser.add_argument("--rune-auto", action="store_true", help="Verify real right-click rune auto-trigger, two frames, manual shot and popup/release/off interlocks")
    parser.add_argument("--drive-recovery", action="store_true", help="Verify actual unstuck HUD button, modal ownership, console actor selection and reset")
    parser.add_argument("--replay-lidar", action="store_true", help="Enable full-density point clouds directly in a fresh replay before developer physics initializes")
    parser.add_argument("--driver-libraries", type=Path, help="Optional process-local graphics driver libraries for an updated-driver session")
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    state = args.output.resolve() / "state.json"
    state.unlink(missing_ok=True)
    x = c.CDLL(ctypes.util.find_library("X11"))
    xt = c.CDLL(ctypes.util.find_library("Xtst"))
    ptr, word = c.c_void_p, c.c_ulong
    signatures = {
        "XOpenDisplay": ([c.c_char_p], ptr),
        "XDefaultRootWindow": ([ptr], word),
        "XInternAtom": ([ptr, c.c_char_p, c.c_int], word),
        "XGetWindowProperty": ([ptr, word, word, c.c_long, c.c_long, c.c_int, word,
                                c.POINTER(word), c.POINTER(c.c_int), c.POINTER(word),
                                c.POINTER(word), c.POINTER(ptr)], c.c_int),
        "XFetchName": ([ptr, word, c.POINTER(ptr)], c.c_int),
        "XFree": ([ptr], c.c_int),
        "XRaiseWindow": ([ptr, word], c.c_int),
        "XSetInputFocus": ([ptr, word, c.c_int, word], c.c_int),
        "XStringToKeysym": ([c.c_char_p], word),
        "XKeysymToKeycode": ([ptr, word], c.c_ubyte),
        "XTranslateCoordinates": ([ptr, word, word, c.c_int, c.c_int,
                                   c.POINTER(c.c_int), c.POINTER(c.c_int), c.POINTER(word)], c.c_int),
        "XFlush": ([ptr], c.c_int),
        "XCloseDisplay": ([ptr], c.c_int),
    }
    for name, (params, result) in signatures.items():
        fn = getattr(x, name)
        fn.argtypes, fn.restype = params, result
    for name, params in {
        "XTestFakeKeyEvent": [ptr, c.c_uint, c.c_int, word],
        "XTestFakeButtonEvent": [ptr, c.c_uint, c.c_int, word],
        "XTestFakeMotionEvent": [ptr, c.c_int, c.c_int, c.c_int, word],
        "XTestFakeRelativeMotionEvent": [ptr, c.c_int, c.c_int, word],
    }.items():
        fn = getattr(xt, name)
        fn.argtypes, fn.restype = params, c.c_int
    display = x.XOpenDisplay(None)
    if not display:
        raise RuntimeError("No X11 display")
    desktop = x.XDefaultRootWindow(display)
    held_keys, held_buttons = set(), set()
    binary = args.binary.resolve()
    assembly = binary.parent / (binary.stem+"_Data") / "Managed/Assembly-CSharp.dll"
    report = {"binary": str(binary), "sha256": hashlib.sha256(binary.read_bytes()).hexdigest(),
              "assemblySha256": hashlib.sha256(assembly.read_bytes()).hexdigest(),
              "platform": "Linux X11/Xwayland", "checks": []}
    window, process, old_fps = None, None, None

    def prop(window_id, name):
        data, actual, count, remaining, fmt = ptr(), word(), word(), word(), c.c_int()
        x.XGetWindowProperty(display, window_id, x.XInternAtom(display, name.encode(), 0), 0, 4096, 0, 0,
                             c.byref(actual), c.byref(fmt), c.byref(count), c.byref(remaining), c.byref(data))
        result = list(c.cast(data, c.POINTER(word))[:count.value]) if data and fmt.value == 32 else []
        if data:
            x.XFree(data)
        return result

    def own_window():
        for item in prop(desktop, "_NET_CLIENT_LIST"):
            if prop(item, "_NET_WM_PID") == [process.pid]:
                name = ptr()
                if x.XFetchName(display, item, c.byref(name)) and name:
                    title = c.string_at(name).decode(errors="replace")
                    x.XFree(name)
                    if title == "2026 TCR Replay":
                        return item
        return None

    def snapshot():
        deadline = time.monotonic() + 15
        while time.monotonic() < deadline:
            if process.poll() is not None:
                raise RuntimeError(f"Player exited {process.returncode}")
            try:
                return json.loads(state.read_text())
            except (FileNotFoundError, json.JSONDecodeError):
                time.sleep(.02)
        raise TimeoutError("No probe state")

    def wait_for(predicate):
        deadline = time.monotonic() + 10
        while time.monotonic() < deadline:
            value = snapshot()
            if predicate(value):
                return value
            time.sleep(.04)
        raise TimeoutError(f"State not reached: {snapshot()}")

    def settled():
        return wait_for(lambda s: s["backgroundAllowed"])

    def key(name, pressed):
        code = x.XKeysymToKeycode(display, x.XStringToKeysym(name.encode()))
        if not code:
            raise RuntimeError(f"No keysym {name}")
        xt.XTestFakeKeyEvent(display, code, pressed, 0)
        x.XFlush(display)
        (held_keys.add if pressed else held_keys.discard)(name)

    def tap(name):
        key(name, True)
        time.sleep(.08)
        key(name, False)
        time.sleep(.16)

    def button(number, pressed):
        xt.XTestFakeButtonEvent(display, number, pressed, 0)
        x.XFlush(display)
        (held_buttons.add if pressed else held_buttons.discard)(number)

    def pointer(px, py):
        s = snapshot()
        rx, ry, child = c.c_int(), c.c_int(), word()
        x.XTranslateCoordinates(display, window, desktop, round(px*s["width"]/1600), round(py*s["height"]/900),
                                c.byref(rx), c.byref(ry), c.byref(child))
        xt.XTestFakeMotionEvent(display, -1, rx.value, ry.value, 0)
        x.XFlush(display)
        time.sleep(.025)

    def click(px, py, number=1):
        pointer(px, py)
        button(number, True)
        time.sleep(.09)
        button(number, False)
        time.sleep(.22)

    def drag(start, end, number=1):
        pointer(*start)
        button(number, True)
        time.sleep(.08)
        for i in range(1, 9):
            pointer(start[0]+(end[0]-start[0])*i/8, start[1]+(end[1]-start[1])*i/8)
        button(number, False)
        time.sleep(.18)

    def wheel(px, py, up=True):
        pointer(px, py)
        for _ in range(3):
            button(4 if up else 5, True)
            button(4 if up else 5, False)
            time.sleep(.05)
        time.sleep(.2)

    def text(value):
        for character in value:
            symbol={" ":"space",".":"period",",":"comma","-":"minus",";":"semicolon","/":"slash"}.get(character,character.lower())
            if character.isupper():key("Shift_L",True)
            tap(symbol)
            if character.isupper():key("Shift_L",False)

    def check(condition, name, **evidence):
        if not condition:
            raise AssertionError(f"{name}: {evidence}; {snapshot()}")
        report["checks"].append({"check": name, "passed": True, **evidence})
        (args.output / "report.json").write_text(json.dumps(report, ensure_ascii=False, indent=2))
        print("PASS", name, flush=True)

    def same(before, after, fields, name):
        check(all(before[field] == after[field] for field in fields), name,
              fields=fields, before={f: before[f] for f in fields}, after={f: after[f] for f in fields})

    def screenshot(name):
        image = Path(str(state)+".png")
        request = Path(str(state)+".capture")
        image.unlink(missing_ok=True)
        # Allow the newly observed input state to reach the rendered frame;
        # Unity's screenshot request and PNG write complete asynchronously.
        time.sleep(.25)
        request.touch()
        deadline = time.monotonic()+15
        while (request.exists() or not image.exists()) and time.monotonic()<deadline:
            if process.poll() is not None:
                raise RuntimeError(f"Player exited {process.returncode} while capturing {name}")
            time.sleep(.05)
        if request.exists() or not image.exists():
            raise TimeoutError("Screenshot did not complete: "+name)
        time.sleep(.25)
        image.replace(args.output / (name+".png"))

    def replay_lidar_checks():
        click(430,350)
        wait_for(lambda s:s["mode"]=="replay")
        settled()
        click(920,244)
        wait_for(lambda s:s["lidar"])
        time.sleep(1)
        check(snapshot()["lidar"],"fresh replay full-density scan starts without developer-mode warmup")
        screenshot("fresh-replay-cloud")
        if snapshot()["lidarView"]==1:
            tap("Escape")
            settled()
        click(1090,244)
        wait_for(lambda s:s["panel"]=="LidarSettings")
        click(1200,255)  # Both fixed radar stations, default full density.
        click(900,302)   # 20 Hz.
        click(1250,176)
        settled()
        click(75,808)
        start=snapshot()["replayTime"]
        deadline=time.monotonic()+30
        last_frame=snapshot()["frame"]
        while time.monotonic()<deadline:
            time.sleep(1)
            now=snapshot()
            if now["error"] or now["frame"]<=last_frame:
                raise RuntimeError("Replay lidar error or stalled player: "+repr(now["error"]))
            last_frame=now["frame"]
        check(snapshot()["replayTime"]>start+20,"fresh replay dual-station 20Hz remains alive during playback")
        screenshot("fresh-replay-dual-cloud")
        click(920,244)
        wait_for(lambda s:not s["lidar"])
        check(True,"fresh replay scanner toggles off cleanly")

    def aim_rune_checks():
        click(430,475)
        wait_for(lambda s:s["mode"]=="game")
        settled()
        click(802,36)
        wait_for(lambda s:s["running"])
        click(800,450)
        wait_for(lambda s:s["captured"])
        time.sleep(.3)
        candidate=snapshot()["aimCandidate"]
        check(bool(candidate),"manual mode presents a current ROI candidate",candidate=candidate)
        button(3,True);time.sleep(.09);button(3,False)
        state_now=wait_for(lambda s:s["autoAim"] and s["aimLock"]==candidate)
        check(True,"right-click locks exactly the displayed ROI candidate",target=state_now["aimLock"])
        wait_for(lambda s:s["aimValid"])
        screenshot("vehicle-green-ekf")
        button(3,True);time.sleep(.09);button(3,False)
        wait_for(lambda s:not s["autoAim"])
        check(not snapshot()["aimValid"] and snapshot()["aimLock"]=="","second right-click removes lock and prediction")
        screenshot("manual-roi-candidate")
        tap("F6")
        wait_for(lambda s:s["panel"]=="Editor")
        # Scroll via real wheel events, then derive button locations from the
        # native editor scroll offset. Rows before the rune section stay fixed.
        for _ in range(16):
            scroll=snapshot()["editorScroll"]["y"]
            if 500<=scroll<=700:break
            wheel(1200,650,scroll>700)
        small_y=205+716-scroll+16;large_y=205+758-scroll+16
        check(225<small_y<680 and 225<large_y<680,"both separate rune buttons are visible in editor",scroll=scroll)
        click(1050,small_y)
        before=wait_for(lambda s:s["redRunePending"] and s["redRuneMode"]=="small")
        check(not before["redRuneActive"] and abs(before["redRuneStartsAt"]-before["gameTime"]-3)<.02,"small button schedules three seconds without immediate activation")
        click(1050,large_y)
        before=wait_for(lambda s:s["redRunePending"] and s["redRuneMode"]=="large")
        check(not before["redRuneActive"],"large button cancels pending small cycle and starts its own wait")
        click(1468,large_y)
        wait_for(lambda s:s["redLargeLoop"])
        click(1345,large_y)
        wait_for(lambda s:not s["redLargeLoop"])
        check(True,"single and loop selectors beside large button work")
        click(1468,small_y)
        wait_for(lambda s:s["redSmallLoop"])
        click(1050,small_y)
        before=wait_for(lambda s:s["redRuneMode"]=="small" and s["redRunePending"])
        check(before["redSmallLoop"] and not before["redLargeLoop"],"small and large retain independent loop selections")
        click(1150,675,3)
        time.sleep(.3)
        check(not snapshot()["autoAim"] and snapshot()["gameTime"]==before["gameTime"],"editor right-click cannot enable aim or advance startup timer")
        screenshot("separate-rune-single-loop")
        tap("Escape")
        settled()
        wait_for(lambda s:s["redRuneActive"])
        now=snapshot()
        check(now["gameTime"]>=before["redRuneStartsAt"] and not now["redRunePending"],"closing editor resumes startup at simulation-time three seconds",started=before["redRuneStartsAt"],time=now["gameTime"])
        tap("F6")
        wait_for(lambda s:s["panel"]=="Editor")
        scroll=snapshot()["editorScroll"]["y"];small_y=205+716-scroll+16
        click(1050,small_y)
        wait_for(lambda s:not s["redRuneEnabled"])
        tap("Escape")
        settled()
        time.sleep(.6)
        check(not snapshot()["redRunePending"] and not snapshot()["redRuneActive"],"off button cancels both running and future loop restart")

    def rune_auto_checks():
        def command(value):
            if value.startswith("set player gimbalYaw "):
                text("set player gim")
                tap("Tab")
                text(value.rsplit(" ",1)[1])
            else:text(value)
            if " ".join(snapshot()["command"].split())!=value:
                raise AssertionError("Console text differs: "+repr(snapshot()["command"])+" expected "+repr(value))
            tap("Return")
            time.sleep(.12)

        def capture():
            # Let synthetic absolute motion drain before locking the cursor.
            # A brief down/up captures it without also holding manual fire.
            pointer(800,450)
            time.sleep(.35)
            button(1,True)
            button(1,False)
            wait_for(lambda s:s["captured"])
            time.sleep(.3)

        click(430,475)
        wait_for(lambda s:s["mode"]=="game")
        settled()
        tap("grave")
        wait_for(lambda s:s["panel"]=="Console")
        anchor=snapshot()["redRuneAnchor"];normal=snapshot()["redRuneNormal"]
        px=anchor["x"]+normal["x"]*8;pz=anchor["z"]+normal["z"]*8
        yaw=math.degrees(math.atan2(-normal["x"],-normal["z"]))
        pitch=math.degrees(math.atan2(anchor["y"]-.29,8))
        command(f"set player position {px:.5f},0,{pz:.5f}")
        command(f"set player gimbalYaw {yaw:.5f}")
        command(f"set player pitch {pitch:.5f}")
        command("control player")
        command("supply player 100")
        command("rune red large on")
        command("start")
        check(snapshot()["redRunePending"] and snapshot()["shots"]==0,"large startup in console cannot fire before three simulated seconds")
        tap("Escape")
        settled()
        capture()
        check(snapshot()["aimCandidate"]=="rune_red","actual first-person R mark is the ROI candidate")
        button(3,True);time.sleep(.09);button(3,False)
        wait_for(lambda s:s["autoAim"] and s["aimLock"]=="rune_red")
        wait_for(lambda s:s["runeAutoShots"]>0 and s["runeFrames"]==2)
        check(not held_buttons,"right-click alone starts automatic rune shots with no held left button",shots=snapshot()["runeAutoShots"])
        screenshot("automatic-large-two-frames")
        tap("F6")
        wait_for(lambda s:s["panel"]=="Editor")
        before=snapshot()
        click(400,450,3)
        time.sleep(.3)
        same(before,snapshot(),["shots","runeAutoShots","gameTime","autoAim","aimLock"],"editor blocks automatic trigger and background right-click while retaining lock")
        tap("Escape")
        settled()
        before=snapshot()
        time.sleep(.4)
        same(before,snapshot(),["shots","runeAutoShots","autoAim","aimLock"],"released mouse suspends automatic firing without closing aim")
        capture()
        wait_for(lambda s:s["redRuneSuccesses"]>=1)
        now=snapshot()
        check(now["redRuneHits"]>=5 and now["redRuneLastRing"]>=1,"automatic trigger completes all five large groups and publishes actual ring",hits=now["redRuneHits"],ring=now["redRuneLastRing"])
        screenshot("large-success-and-ring")
        button(3,True);time.sleep(.09);button(3,False)
        wait_for(lambda s:not s["autoAim"])
        check(snapshot()["runeFrames"]==0,"right-click off clears both prediction frames and automatic trigger")
        tap("grave")
        wait_for(lambda s:s["panel"]=="Console")
        command("set player heat 0")
        command("rune red small on")
        tap("Escape")
        settled()
        capture()
        before=snapshot();button(1,True);time.sleep(.13);button(1,False)
        check(snapshot()["shots"]>before["shots"],"manual left-click remains available during rune startup")

    def drive_recovery_checks():
        def command(value):
            text(value)
            check(snapshot()["command"]==value,"recovery command typed exactly",command=value)
            tap("Return")
            wait_for(lambda s:s["command"]=="")

        click(430,475)
        wait_for(lambda s:s["mode"]=="game")
        settled()
        tap("grave")
        wait_for(lambda s:s["panel"]=="Console")
        command("set player position 5.2,0.4,10.7")
        before=snapshot()
        click(1430,108)
        same(before,snapshot(),["recoveryCount","playerPosition","gameTime"],"console owns input and blocks unstuck HUD")
        tap("Escape")
        settled()
        click(1430,108)
        now=wait_for(lambda s:s["recoveryCount"]==before["recoveryCount"]+1 and not s["recovering"])
        check(now["playerPosition"]!=before["playerPosition"],"unstuck button relocates obstructed vehicle",before=before["playerPosition"],after=now["playerPosition"],status=now["recoveryStatus"])
        same(before,now,["hp","ammo","shots","gameTime"],"paused rescue preserves combat state and does not fire")
        screenshot("rescue-button-working")
        tap("F6")
        wait_for(lambda s:s["panel"]=="Editor")
        before=snapshot();click(1430,108)
        same(before,snapshot(),["recoveryCount","playerPosition"],"editor also blocks background rescue")
        tap("Escape");settled()
        tap("grave");wait_for(lambda s:s["panel"]=="Console")
        command("unstuck target1")
        prior=now["recoveryCount"]
        wait_for(lambda s:s["recoveryCount"]==prior+1)
        check(snapshot()["playerPosition"]==now["playerPosition"],"console can rescue target without moving controlled player")
        command("reset")
        check(snapshot()["recoveryStatus"]=="" and not snapshot()["recovering"],"reset clears rescue status and stale operation")
        tap("Escape");settled()

    env = dict(os.environ, DISPLAY=os.environ.get("DISPLAY", ":0"))
    # Allow an explicit backend for diagnostics and updated-driver sessions.
    if "__GLX_VENDOR_LIBRARY_NAME" not in env:
        try:
            available = subprocess.run(["nvidia-smi", "-L"], stdout=subprocess.DEVNULL,
                                       stderr=subprocess.DEVNULL, timeout=5).returncode == 0
        except (FileNotFoundError, subprocess.TimeoutExpired):
            available = False
        if available:
            env.update(__NV_PRIME_RENDER_OFFLOAD="1", __GLX_VENDOR_LIBRARY_NAME="nvidia")
        else:
            env.update(__GLX_VENDOR_LIBRARY_NAME="mesa", LIBGL_ALWAYS_SOFTWARE="1")
    for name in ("GTK_PATH", "LD_LIBRARY_PATH", "GIO_EXTRA_MODULES"):
        env.pop(name, None)
    if args.driver_libraries:
        env["LD_LIBRARY_PATH"] = str(args.driver_libraries.resolve(strict=True))
    try:
        with (args.output / "stdout.log").open("w") as output:
            process = subprocess.Popen([str(args.binary.resolve()), "-screen-fullscreen", "0", "-screen-width", "1600", "-screen-height", "900",
                                        "-force-glcore", "--ui-probe", str(state), "--ui-size", *map(str,args.size), "-logFile", str(args.output.resolve()/"player.log")],
                                       env=env, stdout=output, stderr=output)
        snapshot()
        deadline = time.monotonic()+12
        while window is None and time.monotonic()<deadline:
            window = own_window()
            time.sleep(.1)
        if window is None:
            raise RuntimeError("Test-owned player window not found")
        x.XRaiseWindow(display, window)
        x.XSetInputFocus(display, window, 2, 0)
        x.XFlush(display)
        wait_for(lambda s: s["focused"])
        old_fps = snapshot()["fps"]
        settled()
        if args.rune_auto or args.aim_rune or args.drive_recovery or args.replay_lidar:
            if args.replay_lidar:replay_lidar_checks()
            elif args.drive_recovery:drive_recovery_checks()
            elif args.rune_auto:rune_auto_checks()
            else:aim_rune_checks()
            report["passed"],report["error"]=True,""
            return
        tap("F8")
        wait_for(lambda s: s["panel"] == "Display")
        click(135, 475)
        tap("F11")
        check(snapshot()["mode"] == "menu" and snapshot()["panel"] == "Display" and not snapshot()["fullscreen"],
              "menu popup blocks mode buttons and fullscreen shortcut")
        tap("Escape")
        settled()
        click(430, 475)
        wait_for(lambda s: s["mode"] == "game")
        settled()
        click(630, 108)
        wait_for(lambda s: s["ammo"] >= 100)
        click(802, 36)
        wait_for(lambda s: s["running"])
        click(800, 450)
        wait_for(lambda s: s["captured"])
        key("w", True)
        time.sleep(.3)
        key("w", False)
        before = snapshot()
        tap("F6")
        wait_for(lambda s: s["panel"] == "Editor" and not s["captured"])
        baseline = snapshot()
        for point in ((802,36),(1010,36),(1300,36),(630,108),(1430,108),(400,450)):
            click(*point)
        click(400,450,3)
        for shortcut in ("p","F7","F9","v","F8","Tab","grave","2","F11"):
            tap(shortcut)
        key("w", True)
        key("Control_L", True)
        key("Shift_L", True)
        time.sleep(.2)
        for name in ("w","Control_L","Shift_L"):
            key(name, False)
        same(baseline, snapshot(), ["mode","panel","gameTime","ammo","shots","running","preview","allLabels","autoAim","playerPosition","gimbalYaw","gimbalPitch"],
             "editor blocks all background buttons, driving, firing and unrelated hotkeys")
        click(1000, 258)
        wait_for(lambda s: s["selection"] == "player")
        click(1000, 300)
        wait_for(lambda s: s["selection"] == "target1")
        spin = snapshot()["targetSpin"]
        click(1440, 300)
        wait_for(lambda s: s["targetSpin"] != spin)
        check(True, "active editor selection and spin controls remain usable")
        screenshot("editor-exclusive")
        click(1530, 171)
        settled()
        check(not snapshot()["captured"] and snapshot()["shots"] == baseline["shots"], "editor close click cannot capture mouse or fire")

        tap("Tab")
        wait_for(lambda s: s["panel"] == "Map")
        baseline = snapshot()
        click(500,400)
        wait_for(lambda s: s["pathPoints"] == baseline["pathPoints"]+1)
        wheel(500,400)
        check(snapshot()["mapZoom"] > baseline["mapZoom"], "active map click and wheel edit points and zoom")
        drag((600,430),(670,460),2)
        check(snapshot()["mapPan"] != baseline["mapPan"], "active map middle drag pans")
        baseline = snapshot()
        wheel(40,450)
        click(800,36)
        for shortcut in ("F6","F8","grave","p","F9"):
            tap(shortcut)
        same(baseline,snapshot(),["panel","mapZoom","mapPan","gameTime","running","allLabels","gimbalYaw","gimbalPitch"], "map ignores backdrop wheel, clicks and competing panel shortcuts")
        screenshot("map-exclusive")
        pointer(600,430)
        button(2,True)
        tap("Escape")
        pointer(250,350)
        time.sleep(.2)
        check(not snapshot()["captured"] and not snapshot()["backgroundAllowed"], "closing during drag waits for mouse release")
        button(2,False)
        settled()

        tap("grave")
        wait_for(lambda s: s["panel"] == "Console" and s["focus"] == "developer-command")
        baseline = snapshot()
        text("set player hp 123")
        wait_for(lambda s: s["command"] == "set player hp 123")
        for shortcut in ("F8","F6","F9","F7","F11"):
            tap(shortcut)
        click(1010,36)
        same(baseline,snapshot(),["panel","gameTime","running","preview","allLabels","shots"], "console typing and hotkeys never operate background")
        tap("Return")
        wait_for(lambda s: s["hp"] == 123 and s["command"] == "")
        tap("Up")
        wait_for(lambda s: s["command"] == "set player hp 123")
        tap("Down")
        wait_for(lambda s: s["command"] == "")
        text("hel")
        tap("Tab")
        wait_for(lambda s: s["command"] == "help ")
        check(snapshot()["panel"] == "Console", "console executes commands, history and Tab completion without opening map")
        screenshot("console-exclusive")
        tap("Escape")
        settled()
        check(snapshot()["keyboard"] == 0 and snapshot()["focus"] != "developer-command", "console close clears stale text focus")

        tap("F8")
        wait_for(lambda s: s["panel"] == "Display")
        baseline = snapshot()
        click(1300,36)
        click(630,108)
        for shortcut in ("F6","Tab","grave","F9","p"):
            tap(shortcut)
        same(baseline,snapshot(),["mode","panel","gameTime","ammo","running","allLabels"], "display popup blocks all other panels and HUD")
        click(1030,390)
        wait_for(lambda s: s["fps"] == 240)
        check(True, "display popup own FPS control remains functional")
        fps_index = [60,120,144,165,240,0].index(old_fps)
        click(380+fps_index*163,390)
        wait_for(lambda s: s["fps"] == old_fps)
        click(1160,280)
        wait_for(lambda s: s["panel"] == "LidarSettings")
        check(True, "explicit display-to-Lidar navigation transfers exclusive focus")
        click(530,818)  # Runtime defaults, without saving preferences.
        if not snapshot()["lidar"]:
            click(430,255)
            wait_for(lambda s: s["lidar"])
        click(1160,304)  # Use reduced scan density for input regression.
        click(420,302)
        wait_for(lambda s: s["lidarView"] == 1)
        baseline = snapshot()
        click(1030,36)
        click(1300,36)
        for shortcut in ("F6","F8","Tab","grave","F9","p"):
            tap(shortcut)
        same(baseline,snapshot(),["mode","panel","running","gameTime","allLabels"], "Lidar settings blocks HUD and unrelated shortcuts")
        screenshot("lidar-settings-exclusive")
        click(1250,176)
        wait_for(lambda s: s["panel"] == "LidarView")
        baseline = snapshot()
        click(800,36)
        click(1300,36)
        click(400,220)
        for shortcut in ("F6","F8","Tab","grave","F9","p"):
            tap(shortcut)
        same(baseline,snapshot(),["panel","running","gameTime","allLabels","shots","captured"], "independent point-cloud window also owns all background input")
        drag((400,440),(470,470))
        wheel(500,470)
        check(snapshot()["lidarYaw"] != baseline["lidarYaw"] and snapshot()["lidarDistance"] != baseline["lidarDistance"], "point-cloud orbit and zoom remain functional")
        click(540,305)
        wait_for(lambda s: s["panel"] == "LidarSettings")
        old_yaw = snapshot()["lidarYaw"]
        drag((100,430),(150,470))
        check(snapshot()["lidarYaw"] == old_yaw, "underlying point-cloud window cannot orbit while settings is active")
        tap("Escape")
        wait_for(lambda s: s["panel"] == "LidarView")
        screenshot("lidar-view-exclusive")
        tap("Escape")
        settled()
        check(snapshot()["lidar"] and snapshot()["lidarView"] == 0, "Escape exits observation window to scene overlay without stopping scan")
        click(1010,108)
        wait_for(lambda s: not s["lidar"])
        click(1300,36)
        wait_for(lambda s: s["mode"] == "menu")
        settled()
        click(430,350)
        wait_for(lambda s: s["mode"] == "replay")
        settled()

        click(1310,39)
        wait_for(lambda s: s["panel"] == "Matches")
        baseline = snapshot()
        for point in ((75,808),(350,808),(1405,808),(1520,808),(1480,83)):
            click(*point)
        drag((700,811),(1100,811))
        for shortcut in ("p","space","Left","Right","F9","2","F8","F10","F11"):
            tap(shortcut)
        same(baseline,snapshot(),["mode","panel","playing","replayTime","camera","captured","allLabels","topView","speed"], "match chooser blocks timeline, slider, camera and shortcuts")
        click(460,190)
        text("jiageng")
        wait_for(lambda s: s["query"] == "jiageng" and 0 < s["results"] < 613)
        check(True, "active match chooser search still accepts text and filters")
        screenshot("matches-exclusive")
        click(1470,250)
        wait_for(lambda s: s["panel"] == "None")
        settled()
        check(not snapshot()["captured"] and not snapshot()["playing"], "loading match does not click through to scene or playback")
        click(1000,167)
        wait_for(lambda s: s["panel"] == "Landmarks")
        baseline = snapshot()
        click(245,167)
        click(1320,39)
        click(75,808)
        for shortcut in ("2","F8","p","F9"):
            tap(shortcut)
        same(baseline,snapshot(),["panel","camera","playing","replayTime","allLabels"], "landmark chooser blocks reset, other chooser and playback")
        click(230,335)
        wait_for(lambda s: s["panel"] == "None" and s["camera"] == "Attached")
        settled()
        check(not snapshot()["captured"], "landmark selection changes view without reusing its click for capture")
        pointer(800,811)
        button(1,True)
        time.sleep(.15)
        tap("F8")
        wait_for(lambda s: s["panel"] == "Display")
        baseline = snapshot()
        pointer(1150,811)
        wheel(1500,680,False)
        same(baseline,snapshot(),["replayTime","eventScroll","playing"], "opening a popup cancels background slider capture and masks scroll views")
        tap("Escape")
        pointer(650,811)
        same(baseline,snapshot(),["replayTime","playing"], "closing while button is held cannot resume the old background drag")
        button(1,False)
        settled()
        tap("F10")
        wait_for(lambda s: s["panel"] == "None")
        tap("F8")
        wait_for(lambda s: s["panel"] == "Display")
        baseline = snapshot()
        click(1420,33)
        click(1420,112)
        for shortcut in ("F10","F9","2","p"):
            tap(shortcut)
        same(baseline,snapshot(),["panel","camera","allLabels","playing","replayTime"], "clean field view obeys the same popup focus")
        tap("F8")
        settled()
        check(not snapshot()["captured"], "current panel toggle closes it without capturing the background")
        report["passed"] = True
        report["error"] = ""
        (args.output / "failure.png").unlink(missing_ok=True)
    except Exception as exc:
        report["passed"], report["error"] = False, str(exc)
        if process is not None and process.poll() is None:
            try:
                screenshot("failure")
            except Exception:
                pass
        raise
    finally:
        for name in list(held_keys):
            key(name, False)
        for number in list(held_buttons):
            button(number, False)
        if process is not None and process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=8)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait()
        x.XCloseDisplay(display)
        (args.output / "report.json").write_text(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
