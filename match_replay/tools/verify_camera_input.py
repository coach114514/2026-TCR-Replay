"""Exercise the Linux player through real X11 keyboard/mouse events.

Run from the repository root. The player is launched and closed by the test.
No pip dependencies; requires an X11/Xwayland session and libX11/libXtst.
"""
import ctypes as c
import ctypes.util
import json
import math
from pathlib import Path
import subprocess
import tempfile
import time


def main():
    root = Path(__file__).resolve().parents[2]
    out = root / "match_replay/artifacts/camera"
    out.mkdir(parents=True, exist_ok=True)
    x = c.CDLL(ctypes.util.find_library("X11"))
    xt = c.CDLL(ctypes.util.find_library("Xtst"))
    pointer, word = c.c_void_p, c.c_ulong
    signatures = {
        "XOpenDisplay": ([c.c_char_p], pointer),
        "XDefaultRootWindow": ([pointer], word),
        "XInternAtom": ([pointer, c.c_char_p, c.c_int], word),
        "XGetWindowProperty": ([pointer, word, word, c.c_long, c.c_long, c.c_int, word,
                                c.POINTER(word), c.POINTER(c.c_int), c.POINTER(word),
                                c.POINTER(word), c.POINTER(pointer)], c.c_int),
        "XFetchName": ([pointer, word, c.POINTER(pointer)], c.c_int),
        "XFree": ([pointer], c.c_int),
        "XRaiseWindow": ([pointer, word], c.c_int),
        "XCreateSimpleWindow": ([pointer, word, c.c_int, c.c_int, c.c_uint, c.c_uint, c.c_uint, word, word], word),
        "XStoreName": ([pointer, word, c.c_char_p], c.c_int),
        "XMapWindow": ([pointer, word], c.c_int),
        "XDestroyWindow": ([pointer, word], c.c_int),
        "XSetInputFocus": ([pointer, word, c.c_int, word], c.c_int),
        "XStringToKeysym": ([c.c_char_p], word),
        "XKeysymToKeycode": ([pointer, word], c.c_ubyte),
        "XTranslateCoordinates": ([pointer, word, word, c.c_int, c.c_int,
                                   c.POINTER(c.c_int), c.POINTER(c.c_int), c.POINTER(word)], c.c_int),
        "XFlush": ([pointer], c.c_int),
        "XCloseDisplay": ([pointer], c.c_int),
    }
    for name, (args, result) in signatures.items():
        fn = getattr(x, name); fn.argtypes = args; fn.restype = result
    for name, args in {
        "XTestFakeKeyEvent": [pointer, c.c_uint, c.c_int, word],
        "XTestFakeButtonEvent": [pointer, c.c_uint, c.c_int, word],
        "XTestFakeMotionEvent": [pointer, c.c_int, c.c_int, c.c_int, word],
        "XTestFakeRelativeMotionEvent": [pointer, c.c_int, c.c_int, word],
    }.items():
        fn = getattr(xt, name); fn.argtypes = args; fn.restype = c.c_int
    display = x.XOpenDisplay(None)
    if not display:
        raise RuntimeError("No X11 display; run in the user's graphical session")
    desktop = x.XDefaultRootWindow(display)
    held = set()
    helper = None
    report = {"platform": "Linux X11/Xwayland", "checks": []}

    def windows():
        data = pointer(); actual = word(); count = word(); remaining = word(); fmt = c.c_int()
        x.XGetWindowProperty(display, desktop, x.XInternAtom(display, b"_NET_CLIENT_LIST", 0),
                             0, 4096, 0, 0, c.byref(actual), c.byref(fmt), c.byref(count),
                             c.byref(remaining), c.byref(data))
        items = list(c.cast(data, c.POINTER(word))[:count.value]) if data and fmt.value == 32 else []
        if data: x.XFree(data)
        return items

    def player_window():
        for window in windows():
            name = pointer()
            if x.XFetchName(display, window, c.byref(name)) and name:
                title = c.string_at(name).decode(errors="replace"); x.XFree(name)
                if title == "2026 TCR Replay": return window
        return None

    def focus(window):
        x.XRaiseWindow(display, window)
        x.XSetInputFocus(display, window, 2, 0); x.XFlush(display)

    def key(name, pressed):
        code = x.XKeysymToKeycode(display, x.XStringToKeysym(name.encode()))
        if not code: raise RuntimeError(f"Missing keysym {name}")
        xt.XTestFakeKeyEvent(display, code, pressed, 0); x.XFlush(display)
        if pressed: held.add(name)
        else: held.discard(name)

    def tap(name):
        key(name, True); time.sleep(.07); key(name, False); time.sleep(.18)

    def click(window, px, py):
        rx = c.c_int(); ry = c.c_int(); child = word()
        x.XTranslateCoordinates(display, window, desktop, px, py, c.byref(rx), c.byref(ry), c.byref(child))
        xt.XTestFakeMotionEvent(display, -1, rx.value, ry.value, 0)
        xt.XTestFakeButtonEvent(display, 1, 1, 0); x.XFlush(display); time.sleep(.08)
        xt.XTestFakeButtonEvent(display, 1, 0, 0); x.XFlush(display); time.sleep(.2)

    def check(condition, name, **evidence):
        if not condition: raise AssertionError(f"{name}: {evidence}")
        report["checks"].append({"check": name, "passed": True, **evidence})

    def delta(a, b):
        return [b["position"][v] - a["position"][v] for v in "xyz"]

    def length(vector): return math.sqrt(sum(v*v for v in vector))

    with tempfile.TemporaryDirectory(prefix="tcr-camera-input-") as work:
        state = Path(work) / "state.json"
        with (Path(work) / "stdout.log").open("w") as stdout:
            process = subprocess.Popen([str(root / "match_replay/run.sh"), "--camera-probe", str(state),
                                        "-logFile", "/tmp/tcr-camera-input-player.log"], stdout=stdout, stderr=stdout)

            def snapshot():
                deadline = time.monotonic() + 10
                while time.monotonic() < deadline:
                    if process.poll() is not None: raise RuntimeError("Player exited early")
                    try: return json.loads(state.read_text())
                    except (FileNotFoundError, json.JSONDecodeError): time.sleep(.02)
                raise TimeoutError("No player probe output")

            def wait_for(predicate):
                deadline = time.monotonic() + 8
                while time.monotonic() < deadline:
                    value = snapshot()
                    if predicate(value): return value
                    time.sleep(.05)
                raise TimeoutError(f"State not reached: {snapshot()}")

            def move(name, duration=.45):
                before = snapshot(); key(name, True); time.sleep(duration); key(name, False); time.sleep(.15)
                after = snapshot()
                return delta(before, after), before, after

            try:
                snapshot()
                deadline = time.monotonic() + 10
                window = player_window()
                while window is None and time.monotonic() < deadline:
                    time.sleep(.1); window = player_window()
                if window is None: raise RuntimeError("Player window not found")
                focus(window); wait_for(lambda s: s["focused"])
                tap("2"); wait_for(lambda s: s["mode"] == "Free" and s["captured"])
                # Xwayland cursor capture/warping finishes asynchronously. Exercise
                # continued movement after it settles, instead of a single event
                # that may be discarded by the player's capture-frame guard.
                time.sleep(.6)
                before = snapshot()
                for _ in range(12):
                    xt.XTestFakeRelativeMotionEvent(display, 5, -2, 0); x.XFlush(display); time.sleep(.035)
                time.sleep(.25)
                after = snapshot()
                yaw = abs((after["euler"]["y"] - before["euler"]["y"] + 180) % 360 - 180)
                pitch = abs((after["euler"]["x"] - before["euler"]["x"] + 180) % 360 - 180)
                check(yaw > 3 and pitch > 1, "mouse look without held button", yaw=round(yaw,3), pitch=round(pitch,3))
                movements = {name: move(name)[0] for name in ("w", "s", "a", "d")}
                for name, vector in movements.items():
                    check(1.3 < length(vector) < 3.3, f"{name.upper()} movement", metres=round(length(vector),3))
                check(length([a+b for a,b in zip(movements["w"],movements["s"])]) < .45 and
                      length([a+b for a,b in zip(movements["a"],movements["d"])]) < .45, "opposite WASD directions")
                up, _, after = move("space")
                check(up[1] > 1.3 and abs(up[0])+abs(up[2]) < .05 and not after["playing"] and after["replayTime"] == 0,
                      "Space moves upward without starting replay", delta=up)
                down, _, _ = move("Shift_L")
                check(down[1] < -1.3 and abs(down[0])+abs(down[2]) < .05, "left Shift moves downward", delta=down)
                down, _, _ = move("Shift_R")
                check(down[1] < -1.3, "right Shift moves downward", delta=down)
                tap("Caps_Lock"); wait_for(lambda s: s["slow"])
                slow, _, _ = move("w"); ratio = length(slow)/length(movements["w"])
                check(.09 < ratio < .23, "Caps Lock slow movement", slow_to_normal=round(ratio,3))
                tap("Caps_Lock"); wait_for(lambda s: not s["slow"])
                tap("Escape"); wait_for(lambda s: not s["captured"])
                stopped, _, _ = move("w", .3)
                check(length(stopped) < .01, "released mouse suspends movement")
                click(window, 800, 450); wait_for(lambda s: s["captured"])
                tap("3"); wait_for(lambda s: s["mode"] == "Attached" and s["captured"])
                tap("Escape"); wait_for(lambda s: not s["captured"])
                before = snapshot(); click(window, 1488, 179)
                after = wait_for(lambda s: s["target"] == "blue_base")
                check(after["position"]["x"] > 20 and after["targetVisible"] and
                      all(abs(after["euler"][axis]-before["euler"][axis]) < .01 for axis in "xyz"),
                      "GUI selects blue building with independent direction", target=after["target"])
                tap("p"); wait_for(lambda s: s["playing"] and s["replayTime"] > .1)
                tap("p"); wait_for(lambda s: not s["playing"])
                check(True, "P toggles replay")
                tap("F10"); wait_for(lambda s: s["clean"])
                original = snapshot(); tap("F11")
                after = wait_for(lambda s: s["fullscreen"] and s["width"] >= original["width"])
                check(True, "F11 fullscreen and F10 clean view", resolution=[after["width"],after["height"]])
                time.sleep(.6); tap("F11")
                after = wait_for(lambda s: not s["fullscreen"] and s["width"] == original["width"] and s["height"] == original["height"])
                check(True, "F11 restores window size", resolution=[after["width"],after["height"]])
                def alt_enter():
                    key("Alt_L", True); tap("Return"); key("Alt_L", False); time.sleep(.25)
                time.sleep(.6); alt_enter(); wait_for(lambda s: s["fullscreen"])
                time.sleep(.6); alt_enter()
                wait_for(lambda s: not s["fullscreen"] and s["width"] == original["width"] and s["height"] == original["height"])
                check(True, "Alt+Enter toggles once and restores window")
                tap("F10"); wait_for(lambda s: not s["clean"])
                click(window, 800, 450); wait_for(lambda s: s["captured"])
                helper = x.XCreateSimpleWindow(display, desktop, 80, 80, 240, 100, 0, 0, 0x203040)
                x.XStoreName(display, helper, b"TCR camera focus verification")
                x.XMapWindow(display, helper); x.XFlush(display); time.sleep(.3)
                focus(helper)
                wait_for(lambda s: not s["focused"] and not s["captured"])
                focus(window); after = wait_for(lambda s: s["focused"])
                check(not after["captured"], "focus loss releases mouse; refocus does not capture")
                x.XDestroyWindow(display, helper); helper = None; x.XFlush(display)
                tap("1"); wait_for(lambda s: s["mode"] == "Observe" and not s["captured"])
                check(True, "1 restores observation mode")
                category = snapshot()["landmarkLabels"]
                tap("F9"); wait_for(lambda s: not s["allLabels"])
                tap("F9"); after = wait_for(lambda s: s["allLabels"])
                check(after["landmarkLabels"] == category, "F9 hides/restores labels and preserves category switches")
                click(window, 420, 166); wait_for(lambda s: not s["allLabels"])
                click(window, 420, 166); wait_for(lambda s: s["allLabels"])
                check(True, "GUI labels button hides and restores all scene labels")
                report["passed"] = True
                print(json.dumps(report, ensure_ascii=False, indent=2))
            except Exception as exc:
                report["passed"] = False; report["error"] = str(exc)
                raise
            finally:
                for name in list(held): key(name, False)
                if helper: x.XDestroyWindow(display, helper)
                if process.poll() is None: process.terminate()
                try: process.wait(timeout=8)
                except subprocess.TimeoutExpired: process.kill(); process.wait()
                (out / "input-verification.json").write_text(json.dumps(report, ensure_ascii=False, indent=2)+"\n")
                x.XCloseDisplay(display)


if __name__ == "__main__":
    main()
