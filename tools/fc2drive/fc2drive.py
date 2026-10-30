"""Drives Far Cry 2 through DevTools' remote control. README.md says how to use it."""

import argparse
import ctypes
import datetime
import html
import json
import os
import subprocess
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
# coming to hand and then for its draw animation, and NVIDIA's for writing a clip.
PIPE_TIMEOUT = 60.0
READY_TIMEOUT = 180.0
READY_SETTLE = 2.0
GUN_TIMEOUT = 6.0
GUN_DRAW = 1.5
CLIP_TIMEOUT = 60.0

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


class GameError(RuntimeError):
    pass


def _poll(check, timeout, interval=0.1):
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


class Game:
    """One connection to a running game. Each method answers once the game has done the thing."""

    def __init__(self, process=None):
        self.process = process
        self._pipe = None

    def connect(self, timeout=2.0):
        def attempt():
            if self.process is not None and self.process.poll() is not None:
                raise GameError(f"the game exited with code {self.process.returncode}")
            try:
                return open(PIPE, "r+b", buffering=0)
            except OSError:
                return None

        self._pipe = _poll(attempt, timeout, interval=0.25)
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
            raise GameError(f"the game closed the connection during: {line}") from error
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

        state = _poll(settled, READY_TIMEOUT, interval=0.25)
        if state is None:
            raise GameError(f"the world was not ready after {READY_TIMEOUT:.0f} s")
        return state

    def lua(self, code):
        return self.call(f"lua {code}")["lines"]

    def eval(self, expression):
        return self.call(f"eval {expression}")["value"]

    def console(self, line):
        self.call(f"console {line}")

    def option(self, name, value):
        """Sets a row of the Mod Configuration Menu for this launch: `option("Infinite ammo", 1)`."""
        self.call(f"option {name} {value}")

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
        if not _poll(lambda: self.state()["gun"] == archetype, GUN_TIMEOUT):
            raise GameError(f"{archetype} was given but never came to hand")
        time.sleep(GUN_DRAW)

    def spawn_pickup(self, archetype):
        self.call(f"spawnpickup {archetype}")

    def pick_up_gun(self):
        """Presses use, and returns the gun that comes to hand, or None if the one held stays."""
        held = self.state()["gun"]
        self.action("use", 1.0)
        return _poll(lambda: (gun := self.state()["gun"]) and gun != held and gun, GUN_TIMEOUT)

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
        """Asks the game to quit, then ends the process if it does not."""
        if self._pipe is not None:
            try:
                self.console("quit")
            except GameError:
                pass
            self._pipe.close()
            self._pipe = None
        _end_game(self.process)


def _end_game(process):
    if process is not None:
        try:
            process.wait(8.0)
            return
        except subprocess.TimeoutExpired:
            pass
    subprocess.run(["taskkill", "/f", "/im", "FCSE.exe"], capture_output=True)
    subprocess.run(["taskkill", "/f", "/im", "FarCry2.exe"], capture_output=True)


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

    clip = _poll(lambda: next((clip for clip in set(folder.rglob("*.mp4")) - before if _settled(clip)), None),
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


class Run:
    """One run's folder: its shots and clips, and an index.html to look through them."""

    def __init__(self, name):
        stamp = datetime.datetime.now().strftime("%Y-%m-%d_%H-%M-%S")
        self.folder = RUNS / f"{stamp}-{name}"
        self.folder.mkdir(parents=True)
        self.name = name
        self._entries = []

    def _path(self, label, extension):
        return self.folder / f"{len(self._entries):03d}-{_safe(label)}.{extension}"

    def _add(self, kind, label, path=None):
        self._entries.append((kind, label, path.name if path else None))
        self._write_index()
        print(f"{kind:6}{path or label}")

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
                parts.append(f"<p>{label}</p>")
        (self.folder / "index.html").write_text("\n".join(parts), encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description="One command to a running game, or start and stop it.")
    parser.add_argument("command", help="start, stop, wait, or a remote command: state, lua, eval, console, option, action, shot, goto, guns, give, spawnpickup, rounds, gunhealth, weather, record, stoprecord, play")
    parser.add_argument("arguments", nargs=argparse.REMAINDER)
    options = parser.parse_args()

    if options.command == "start":
        game = start(options.arguments[0] if options.arguments else None, options.arguments[1:])
        print(json.dumps(game.state()))
    elif options.command == "stop":
        Game().close()
    elif options.command == "wait":
        print(json.dumps(Game().wait_ready()))
    else:
        print(json.dumps(Game().call(" ".join([options.command, *options.arguments]))))


if __name__ == "__main__":
    main()
