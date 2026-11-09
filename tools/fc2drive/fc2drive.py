"""Drives Far Cry 2 through DevTools' remote control. README.md says how to use it."""

import argparse
import ctypes
import datetime
import functools
import html
import json
import os
import re
import shutil
import subprocess
import sys
import threading
import time
import winreg
from contextlib import contextmanager
from ctypes import wintypes
from pathlib import Path

PIPE = r"\\.\pipe\DevToolsRemote"
GAME_BIN = Path(os.environ.get("FC2_GAME", r"C:\Games\Far Cry 2")) / "bin"
RUNS = Path(__file__).parent / "runs"

# The saves behind the RunDebug shortcuts in the game's bin folder.
SAVES = {
    "demorange": "32433743376.sav",
    "world1": "135403226491.sav",
    "world2": "105502543219.sav",
    "guntest": "60145820471.sav",
    "bigtruck": "162773352691.sav",
}

# Seconds: a launch's wait for the pipe, a world's for loading and then for staying loaded, a gun's for
# coming to hand and then for its draw animation, NVIDIA's for writing a clip, and the game's for
# quitting when asked.
PIPE_TIMEOUT = 60.0
READY_TIMEOUT = 180.0
READY_SETTLE = 2.0
GUN_TIMEOUT = 6.0
GUN_DRAW = 1.5
CLIP_TIMEOUT = 60.0
QUIT_TIMEOUT = 8.0

# A test's exit codes beyond pass (0) and fail (1), clear of Python's own 2.
SKIPPED = 3
NEEDS_APPROVAL = 4

# What a line of fcse.log says when something did not work: a crash, a Lua error, a plugin that did
# not load, or a part of one that found nothing to hook.
LOG_FAILURE = re.compile(r"CRASH:|lua error:|not found in this build|was not hooked|could not|failed|"
                         r"returned false|no FCSE_Load|rejected|refus|no match for", re.IGNORECASE)

# Pictures are compared this many pixels wide and softened; a pixel changed when a channel moved more
# than PIXEL_CHANGE of its range; and a picture is judged by its most changed square of TILE pixels.
COMPARE_WIDTH = 480
PIXEL_CHANGE = 0.1
TILE = 16

KEEP_DISPLAY_ON = 0x80000003
KEYEVENTF_KEYUP = 2
HWND_TOPMOST = -1
HWND_NOTOPMOST = -2
SWP_NOSIZE = 0x0001
SWP_NOMOVE = 0x0002
SWP_NOACTIVATE = 0x0010

user32 = ctypes.WinDLL("user32", use_last_error=True)
kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
# The window's real pixels, which is what NVIDIA records and what a crop has to name.
user32.SetProcessDPIAware()
user32.GetClientRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT)]
user32.ClientToScreen.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.POINT)]
user32.SetWindowPos.argtypes = [wintypes.HWND, wintypes.HWND, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int, wintypes.UINT]
kernel32.SetThreadExecutionState.argtypes = [wintypes.DWORD]
WNDENUMPROC = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
user32.EnumWindows.argtypes = [WNDENUMPROC, wintypes.LPARAM]
user32.EnumChildWindows.argtypes = [wintypes.HWND, WNDENUMPROC, wintypes.LPARAM]
user32.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]


class GameError(RuntimeError):
    pass


def eventually(check, timeout, interval=0.1):
    """Calls `check` until it returns something true and returns that, or None after `timeout` seconds."""
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        found = check()
        if found:
            return found
        time.sleep(interval)
    return None


def _tap_keys(keys):
    for key in keys:
        user32.keybd_event(key, user32.MapVirtualKeyW(key, 0), 0, 0)
    time.sleep(0.05)
    for key in reversed(keys):
        user32.keybd_event(key, user32.MapVirtualKeyW(key, 0), KEYEVENTF_KEYUP, 0)


def _window_text(window):
    text = ctypes.create_unicode_buffer(512)
    user32.GetWindowTextW(window, text, len(text))
    return text.value


def _dialogs(pid):
    """The text of each message box process `pid` has up: its title, then what it says."""
    found = []

    def visit(window, _):
        owner = wintypes.DWORD()
        user32.GetWindowThreadProcessId(window, ctypes.byref(owner))
        if owner.value != pid or not user32.IsWindowVisible(window):
            return True
        kind = ctypes.create_unicode_buffer(32)
        user32.GetClassNameW(window, kind, len(kind))
        if kind.value == "#32770":
            parts = [_window_text(window)]
            user32.EnumChildWindows(window, WNDENUMPROC(lambda child, _: parts.append(_window_text(child)) or True), 0)
            found.append(" - ".join(part for part in parts if part))
        return True

    user32.EnumWindows(WNDENUMPROC(visit), 0)
    return found


def log_failures():
    """(module, text) for each line of this launch's fcse.log that says something did not work."""
    log = GAME_BIN / "fcse.log"
    lines = log.read_text(encoding="utf-8", errors="replace").splitlines() if log.exists() else []
    tagged = (re.match(r"\[[^\]]*\]\[([^\]]+)\]\s*(.*)", line) for line in lines if LOG_FAILURE.search(line))
    return [found.groups() for found in tagged if found]


class Game:
    """One connection to a running game. Each method answers once the game has done the thing."""

    def __init__(self, process=None):
        self.process = process
        self.fault = None
        self._pipe = None
        self._closing = False
        if process is not None:
            threading.Thread(target=self._watch, daemon=True).start()

    def _watch(self):
        """Ends the game when a message box comes up, which would hang the run, and notes a crash."""
        while self.process.poll() is None and not self._closing:
            if boxes := _dialogs(self.process.pid):
                self.fault = "a message box came up: " + " | ".join(boxes)
                self.process.kill()
                return
            time.sleep(0.5)
        if not self._closing:
            self.fault = f"the game exited with code {self.process.returncode} during the run"

    def connect(self, timeout=2.0):
        def attempt():
            if self.process is not None and self.process.poll() is not None:
                raise GameError(self.fault or f"the game exited with code {self.process.returncode}")
            try:
                return open(PIPE, "r+b", buffering=0)
            except OSError:
                return None

        self._pipe = eventually(attempt, timeout, interval=0.25)
        if self._pipe is None:
            raise GameError("no game is serving the remote control: launch it with -devtoolsremote")

    def call(self, line):
        if self._pipe is None:
            self.connect()
        try:
            self._pipe.write((line.replace("\n", " ") + "\n").encode("utf-8"))
            reply = b""
            while not reply.endswith(b"\n"):
                chunk = self._pipe.read(65536)
                if not chunk:
                    raise OSError("the pipe closed")
                reply += chunk
        except OSError as error:
            self._pipe = None
            raise GameError(f"the game closed the connection during: {line}"
                            + (f" ({self.fault})" if self.fault else "")) from error
        answer = json.loads(reply)
        if not answer["ok"]:
            raise GameError(f"{line}: {answer['error']} {answer.get('lines', '')}")
        return answer

    def state(self):
        return self.call("state")

    def wait_ready(self):
        """Waits until the player is in a world with nothing loading, and it has stayed that way."""
        since = None

        def settled():
            nonlocal since
            state = self.state()
            now = time.monotonic()
            since = (since or now) if state["ready"] else None
            return state if since is not None and now - since >= READY_SETTLE else None

        state = eventually(settled, READY_TIMEOUT, interval=0.25)
        if state is None:
            raise GameError(f"the world was not ready after {READY_TIMEOUT:.0f} s")
        return state

    def lua(self, code):
        return self.call(f"lua {code}")["lines"]

    def eval(self, expression):
        return self.call(f"eval {expression}")["value"]

    def console(self, line):
        self.call(f"console {line}")

    def setting(self, plugin, name, value):
        """Sets a mod's setting for this launch, spelled as in fcse.ini: `setting("AimingOverhaul", "Lean", False)`."""
        if isinstance(value, bool):
            value = "true" if value else "false"
        self.call(f"setting [{plugin}] {name} = {value}")

    def option(self, name, value):
        """Sets one of DevTools' own settings for this launch: `option("Infinite ammo", 1)`."""
        self.setting("DevTools", name, value)

    def action(self, signal, *values):
        self.call(" ".join(["action", signal, *map(str, values)]))

    def fire(self, seconds):
        self.action("startshooting", 1.0)
        time.sleep(seconds)
        self.action("stopshooting", 0.0)

    def walk(self, forward, strafe, seconds):
        self.action("move", forward, strafe)
        time.sleep(seconds)
        self.action("move", 0.0, 0.0)

    def look(self, yaw, pitch):
        """Points the view: yaw as goto takes it, pitch in degrees, up positive. Answers with the state."""
        return self.call(f"look {yaw} {pitch}")

    def shot(self, path):
        path = Path(path).resolve()
        path.parent.mkdir(parents=True, exist_ok=True)
        self.call(f"shot {path}")
        return path

    def guns(self):
        return self.call("guns")["guns"]

    def give(self, archetype):
        """Puts a gun in hand and waits until it is drawn."""
        self.call(f"give {archetype}")
        if not eventually(lambda: self.state()["gun"] == archetype, GUN_TIMEOUT):
            raise GameError(f"{archetype} was given but never came to hand")
        time.sleep(GUN_DRAW)

    def spawn_pickup(self, archetype):
        self.call(f"spawnpickup {archetype}")

    def pick_up_gun(self):
        """Presses use, and returns the gun that comes to hand, or None if the one held stays."""
        held = self.state()["gun"]
        self.action("use", 1.0)
        return eventually(lambda: (gun := self.state()["gun"]) and gun != held and gun, GUN_TIMEOUT)

    def spawn(self, archetype, x, y, z, yaw=0.0):
        """Puts an entity into the world at x, y, z, kept for clear(), and returns its id."""
        return self.call(f"spawn {archetype} {x} {y} {z} {yaw}")["id"]

    def clear(self):
        """Removes everything spawn() and the Range tab put into the world."""
        self.call("clear")

    def entity(self, which="player", component=None):
        """An entity by id, or the player: name, pos, yaw, health and max_health, and with `component`
        a class name such as "CPawn", `has`."""
        return self.call(f"entity {which}" + (f" {component}" if component else ""))

    def sight(self):
        """Each soldier whose sight of the player updated in the last second: pos, distance, known,
        visibility and plainly_seen. The first ask starts the copying, so it can come back empty."""
        return self.call("sight")["soldiers"]

    def pause(self, stopped=True):
        """Stops the game clock, so the world holds still while frames keep coming, or starts it again."""
        self.call(f"pause {'on' if stopped else 'off'}")

    def goto(self, x, y, z, yaw=None):
        return self.call(f"goto {x} {y} {z}" + ("" if yaw is None else f" {yaw}"))

    def fix_light(self, hour):
        """Sets the hour and clears the weather, so shots of one place compare between runs."""
        self.lua(f"CDynamicEnvironmentManager_GetInstance():SetScriptedTimeOfDay({hour}, 0)")
        self.call("weather clear")

    def start_recording(self):
        """Starts keeping every action, from the player or a script, for stop_recording to write."""
        self.call("record")

    def stop_recording(self, path):
        path = Path(path).resolve()
        path.parent.mkdir(parents=True, exist_ok=True)
        self.call(f"stoprecord {path}")
        return path

    def play(self, path):
        """Plays a recording and waits for its last action. Start it from where it was recorded."""
        self.call(f"play {Path(path).resolve()}")
        while self.state()["playing"]:
            time.sleep(0.1)

    def close(self):
        """Asks the game to quit, then ends the process if it does not. True when it quit by itself."""
        self._closing = True
        if self._pipe is not None:
            try:
                self.console("quit")
            except GameError:
                pass
            self._pipe.close()
            self._pipe = None
        return _end_game(self.process)


def _end_game(process):
    if process is not None:
        try:
            process.wait(QUIT_TIMEOUT)
            return True
        except subprocess.TimeoutExpired:
            pass
    subprocess.run(["taskkill", "/f", "/im", "FCSE.exe"], capture_output=True)
    subprocess.run(["taskkill", "/f", "/im", "FarCry2.exe"], capture_output=True)
    return False


def _game_is_running():
    listing = subprocess.run(["tasklist", "/fi", "imagename eq FCSE.exe", "/fo", "csv", "/nh"], capture_output=True, text=True)
    return "FCSE.exe" in listing.stdout


def start(save=None, args=(), env=None):
    """Launches the game with the remote control on, and waits for its world."""
    if _game_is_running():
        raise GameError("a game is already running")
    # The screensaver stops the game starting and the frame being presented.
    subprocess.run(["taskkill", "/f", "/im", "scrnsave.scr"], capture_output=True)
    kernel32.SetThreadExecutionState(KEEP_DISPLAY_ON)

    command = [str(GAME_BIN / "FCSE.exe"), "-devtoolsremote"]
    if save:
        command += ["-load", SAVES.get(save, save)]
    game = Game(subprocess.Popen(command + list(args), cwd=GAME_BIN, env={**os.environ, **(env or {})}))
    game.connect(PIPE_TIMEOUT)
    if save:
        game.wait_ready()
    return game


@contextmanager
def launch(save=None, args=(), env=None):
    game = start(save, args, env)
    try:
        yield game
    finally:
        game.close()


def _normal(name):
    return re.sub(r"[^a-z0-9]", "", name.lower())


@functools.cache
def _plugins():
    """The plugins in the install, by the names of their DLLs."""
    return sorted({path.stem for path in (GAME_BIN / "plugins").rglob("*.dll")}, key=str.lower)


def _nvidia_settings():
    """NVIDIA's record hotkey and the folder its clips land in, as this machine has them set."""
    settings = Path(os.environ["LOCALAPPDATA"]) / "NVIDIA Corporation" / "NVIDIA Overlay" / "ShareSettings.json"
    keys = json.loads(settings.read_text(encoding="utf-8"))["settings"]["shortcuts"]["RecordToggle"]
    with winreg.OpenKey(winreg.HKEY_CURRENT_USER, r"Software\NVIDIA Corporation\Global\ShadowPlay\NVSPCAPS") as key:
        folder = winreg.QueryValueEx(key, "DefaultPathW")[0].decode("utf-16-le").rstrip("\0")
    return keys, Path(folder)


@contextmanager
def recording(game, target):
    """Records the inside of the block with NVIDIA's recorder, saving only the game's window to `target`."""
    keys, folder = _nvidia_settings()
    before = set(folder.rglob("*.mp4"))
    window = game.state()["window"]
    area = _client_area(window)
    user32.SetWindowPos(window, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE)
    _tap_keys(keys)
    time.sleep(1.0)
    try:
        yield
    finally:
        _tap_keys(keys)
        user32.SetWindowPos(window, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE)

    clip = eventually(lambda: next((clip for clip in set(folder.rglob("*.mp4")) - before if _settled(clip)), None),
                      CLIP_TIMEOUT, interval=0.5)
    if clip is None:
        raise GameError(f"NVIDIA left no new clip under {folder}: is its overlay on, and did the hotkey reach it?")
    Path(target).parent.mkdir(parents=True, exist_ok=True)
    _crop(clip, area, target)
    clip.unlink()


def _client_area(window):
    rect = wintypes.RECT()
    user32.GetClientRect(window, ctypes.byref(rect))
    corner = wintypes.POINT(0, 0)
    user32.ClientToScreen(window, ctypes.byref(corner))
    return corner.x, corner.y, rect.right, rect.bottom


def _crop(clip, area, target):
    x, y, width, height = area
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", str(clip), "-vf", f"crop={width}:{height}:{x}:{y}",
                    "-c:v", "h264_nvenc", "-preset", "p5", "-cq", "23", "-c:a", "copy", str(target)], check=True)


def _settled(clip):
    size = clip.stat().st_size
    time.sleep(1.0)
    return size > 0 and clip.stat().st_size == size


def _safe(label):
    return "".join(c if c.isalnum() or c in "-_." else "_" for c in label)


def _changes(shot, reference, mask):
    """The share of the most changed tile against `reference`, and a picture of where: the reference
    dimmed, changed pixels red. Both are compared COMPARE_WIDTH wide and softened."""
    import numpy
    from PIL import Image, ImageFilter

    def load(path):
        image = Image.open(path).convert("RGB")
        small = image.resize((COMPARE_WIDTH, round(COMPARE_WIDTH * image.height / image.width)), Image.BOX)
        return numpy.asarray(small.filter(ImageFilter.GaussianBlur(1)), dtype=numpy.float32) / 255

    now, then = load(shot), load(reference)
    changed = numpy.abs(now - then).max(axis=2) > PIXEL_CHANGE
    height, width = changed.shape
    for left, top, right, bottom in mask:
        changed[round(top * height):round(bottom * height), round(left * width):round(right * width)] = False
    picture = (then * 102).astype(numpy.uint8)
    picture[changed] = (255, 0, 0)
    rows, columns = height - height % TILE, width - width % TILE
    tiles = changed[:rows, :columns].reshape(rows // TILE, TILE, columns // TILE, TILE).mean(axis=(1, 3))
    return float(tiles.max()), Image.fromarray(picture)


class Run:
    """One run's folder: its shots and clips, and an index.html to look through them. A test also
    keeps its checks here, and its pictures' approved references in references/<name>/ beside it.
    `mods` are the plugins a test is about: with one missing from the install the test ends as skipped,
    exit code 3, and their failures in fcse.log fail it as FCSE's and DevTools' do."""

    def __init__(self, name, mods=()):
        missing = [mod for mod in mods if _normal(mod) not in {_normal(plugin) for plugin in _plugins()}]
        if missing:
            print(f"skipped: {', '.join(missing)} not in {GAME_BIN / 'plugins'}")
            raise SystemExit(SKIPPED)
        self._answerable = {"fcse", "devtools", *map(_normal, mods)}
        stamp = datetime.datetime.now().strftime("%Y-%m-%d_%H-%M-%S")
        self.folder = RUNS / f"{stamp}-{name}"
        self.folder.mkdir(parents=True)
        self.name = name
        self._entries = []
        self._checks = []
        self._approvals = []

    def _path(self, label, extension):
        return self.folder / f"{len(self._entries):03d}-{_safe(label)}.{extension}"

    def _add(self, kind, label, path=None):
        self._entries.append((kind, label, path.name if path else None))
        self._write_index()
        print(f"{kind:8}{path or label}")

    def shot(self, game, label):
        path = self._path(label, "png")
        game.shot(path)
        self._add("image", label, path)
        return path

    @contextmanager
    def record(self, game, label):
        path = self._path(label, "mp4")
        with recording(game, path):
            yield
        self._add("video", label, path)

    def note(self, text):
        self._add("note", text)

    @contextmanager
    def launch(self, save=None, args=(), env=None):
        """launch(), with the game's own health part of the test: no message box or crash during the
        run, nothing failed in fcse.log for FCSE, DevTools or the test's mods, and a quit by itself when
        asked. Other mods' failures are noted. The FPS counter is off. A GameError ends the block as a
        failed check instead of a traceback."""
        game = start(save, args, env)
        try:
            game.option("FPS counter", False)
            yield game
        except GameError as error:
            self.expect("the test ran to its end", False, str(error))
        finally:
            quit_by_itself = game.close()
            self.expect("no message box and no crash", game.fault is None, game.fault or "")
            if game.fault is None:
                self.expect("the game quit when asked", quit_by_itself,
                            f"exit code {game.process.returncode}" if quit_by_itself else "it had to be ended")
            ours, others = [], []
            for module, text in log_failures():
                (ours if _normal(module) in self._answerable else others).append(f"[{module}] {text}")
            self.expect("fcse.log reports nothing failed", not ours, " | ".join(ours[:5]))
            if others:
                self.note("other mods' failures in fcse.log: " + " | ".join(others[:5]))

    def expect(self, label, held, detail=""):
        """One check of a test, read off the game's state; finish() fails the run if any did not hold."""
        self._checks.append(bool(held))
        self._add("pass" if held else "FAIL", f"{label}: {detail}" if detail else label)
        return held

    def expect_between(self, label, value, low, high):
        return self.expect(label, value is not None and low <= value <= high,
                           f"{'none' if value is None else f'{value:.3f}'}, wanted {low} to {high}")

    def expect_picture(self, game, label, mask=(), tolerance=0.05):
        """Takes a shot and compares it with the approved reference of the same label: more than
        `tolerance` of any tile changed fails it. `mask` is rectangles to leave out, each (left, top,
        right, bottom) as shares of the frame. With no reference yet, or one taken with other plugins
        installed or at another resolution, the shot waits for approval: `fc2drive.py approve <run>`."""
        shot = self.shot(game, label)
        references = Path(sys.modules["__main__"].__file__).resolve().parent / "references" / _safe(self.name)
        from PIL import Image

        reference = references / f"{_safe(label)}.png"
        setup = {"plugins": _plugins(), "resolution": list(Image.open(shot).size)}
        wanted = references / "setup.json"
        approved = json.loads(wanted.read_text(encoding="utf-8")) if wanted.exists() else None
        approval = {"shot": shot.name, "reference": str(reference), "setup": setup}

        if not reference.exists() or approved != setup:
            why = "no reference yet" if not reference.exists() else f"the references were taken with {approved}"
            self._approvals.append(approval)
            self._add("approve", f"{label}: {why}")
            return None
        changed, where = _changes(shot, reference, mask)
        changes = shot.with_name(f"{shot.stem}-changes.png")
        where.save(changes)
        self._add("image", f"{label}: what changed", changes)
        if changed > tolerance:
            self._approvals.append(approval)
        return self.expect(label, changed <= tolerance, f"{changed:.1%} of a tile changed, {tolerance:.1%} allowed")

    def finish(self):
        """Ends a test with its verdict: exit 1 when a check failed or none was made, 4 when only
        pictures wait for approval, 0 when everything held."""
        failed = self._checks.count(False)
        self.note(f"{len(self._checks) - failed} of {len(self._checks)} checks held"
                  + (f", {len(self._approvals)} pictures to approve" if self._approvals else ""))
        if self._approvals:
            (self.folder / "approvals.json").write_text(json.dumps(self._approvals, indent=1), encoding="utf-8")
        print(self.folder / "index.html")
        if failed or not (self._checks or self._approvals):
            raise SystemExit(1)
        raise SystemExit(NEEDS_APPROVAL if self._approvals else 0)

    def _write_index(self):
        parts = [f"<!doctype html><meta charset=utf-8><title>{html.escape(self.name)}</title>",
                 "<style>body{background:#222;color:#ddd;font:14px sans-serif}"
                 "figure{display:inline-block;margin:6px;vertical-align:top}"
                 "img,video{width:640px;display:block}p{margin:6px}</style>",
                 f"<h1>{html.escape(self.name)}</h1>"]
        for kind, label, path in self._entries:
            label = html.escape(label)
            if kind == "image":
                parts.append(f"<figure><a href='{path}'><img src='{path}' loading=lazy></a><figcaption>{label}</figcaption></figure>")
            elif kind == "video":
                parts.append(f"<figure><video src='{path}' controls></video><figcaption>{label}</figcaption></figure>")
            else:
                parts.append(f"<p>{'' if kind == 'note' else kind + ': '}{label}</p>")
        (self.folder / "index.html").write_text("\n".join(parts), encoding="utf-8")


def approve(folder):
    """Makes a run's waiting and changed pictures the references their tests compare with."""
    approvals = json.loads((Path(folder) / "approvals.json").read_text(encoding="utf-8"))
    for approval in approvals:
        reference = Path(approval["reference"])
        reference.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(Path(folder) / approval["shot"], reference)
        (reference.parent / "setup.json").write_text(json.dumps(approval["setup"], indent=1), encoding="utf-8")
        print(f"approved {reference}")


def main():
    parser = argparse.ArgumentParser(description="One command to a running game, start or stop it, or approve a run's pictures.")
    parser.add_argument("command", help="start, stop, wait, approve <run folder>, or a remote command: state, look, "
                                        "lua, eval, console, setting, action, shot, goto, guns, give, rounds, gunhealth, "
                                        "spawnpickup, spawn, clear, entity, sight, pause, weather, record, stoprecord, play")
    parser.add_argument("arguments", nargs=argparse.REMAINDER)
    options = parser.parse_args()

    if options.command == "start":
        game = start(options.arguments[0] if options.arguments else None, options.arguments[1:])
        print(json.dumps(game.state()))
    elif options.command == "stop":
        Game().close()
    elif options.command == "wait":
        print(json.dumps(Game().wait_ready()))
    elif options.command == "approve":
        approve(options.arguments[0])
    else:
        print(json.dumps(Game().call(" ".join([options.command, *options.arguments]))))


if __name__ == "__main__":
    main()
