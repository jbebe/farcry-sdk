# fc2drive

Drives Far Cry 2 from outside, to test mods without playing through the test by hand: start from a
save, teleport, fire actions, take pictures, record the run with NVIDIA, replay a chunk of play.

It talks to [DevTools' remote control](../../mods/DevTools/README.md#remote-control), so DevTools has to
be in the install. It needs Python 3, plus Pillow and numpy for picture checks
(`pip install pillow numpy`) and ffmpeg with NVENC on `PATH` for clips. The install it drives is
`C:\Games\Far Cry 2`, or `FC2_GAME` if that is set.

## Scenarios

```
python scenarios\guns.py                  every Armory gun: one shot, a longer burst, in one clip
python scenarios\guns.py --only AK-47,M79 --no-clip
python scenarios\locations.py             a picture at each spot in scenarios\spots\world1.json
python scenarios\locations.py --hour 22 --clip
python scenarios\inventory.py             the notebook's pages, then a pickup; needs Inventory Overhaul
python scenarios\perception.py            Difficulty Overhaul's perception on the test range, case by case
python scenarios\perception.py --only day-upright,night-upright
python scenarios\grass.py                 the test range's grass field: pictures, then whether soldiers count you in grass
```

Each launches the game, does its work, closes the game, and leaves a folder under `runs\` (not
committed) with its pictures, its clip and an `index.html` to look through them. The guns scenario also
notes when each gun starts in the clip and how many rounds each press fired.

A spots file is a save, and each spot's name, x, y, z and yaw. `python fc2drive.py state` reads off
where the player stands. The game finds no ground for a teleport, so a z too low lands inside the
terrain; 2 m above the ground drops safely.

## Writing one

```python
from fc2drive import Run, launch

run = Run("my test")
with launch("world1") as game:
    game.option("Invincibility", 1)
    game.fix_light(hour=12)
    game.goto(1876.16, 3523.59, 37.0, yaw=187)
    game.give("weapons.Primary.AK47")
    with run.record(game, "burst"):
        game.fire(3.0)
    run.shot(game, "afterwards")
```

`launch` takes a name from `SAVES` or a `.sav` file. `game.action(signal, values...)` fires anything in
the action map, and `game.lua(code)` and `game.eval(expression)` reach the rest of the game.

The game's own state, each a remote command in [DevTools' table](../../mods/DevTools/README.md#remote-control):

| Call | What it gives or does |
|---|---|
| `game.state()` | position, yaw, look pitch, `crouched`, `in_vehicle`, the `camera` (position, yaw, pitch, roll, fov) and the gun in hand |
| `game.look(yaw, pitch)` | points the view, in degrees, so a picture frames the same every run |
| `game.spawn(archetype, x, y, z, yaw)`, `game.clear()` | an entity put into the world, by id, and everything spawned removed |
| `game.entity(id or "player", component)` | name, position, yaw, health, and whether it has a component class |
| `game.sight()` | what each soldier makes of the player |
| `game.setting(plugin, name, value)`, `game.option(name, value)` | any mod's setting, or DevTools' own, for this launch |
| `game.pause(True)` | the world held still, for a picture |

`eventually(check, seconds)` waits for something that takes time.

## A mod's tests

A mod's automated tests are fc2drive scripts in its own `tests/` folder, one `.py` per test, run by
`/release-check` against the mods deployed to the install. Each sets up its case - on the test range
(`demorange`), or in world1 or world2 from a save - reads the outcome off the game's state, and ends
with a verdict. A plugin never carries logic for its own tests: what a test needs that the remote
control cannot read yet is added to DevTools, for every mod.

```python
import sys
import time
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[3] / "tools" / "fc2drive"))
from fc2drive import Run

run = Run("lean", mods=["aiming-overhaul"])
with run.launch("demorange") as game:
    game.goto(256.0, 110.0, 16.5, yaw=0.0)
    game.action("start_lean_right", 1.0)
    time.sleep(1.0)
    run.expect_between("the view rolls, degrees", game.state()["camera"]["roll"], 12.0, 18.0)
run.finish()
```

- `Run(name, mods=[...])` names the plugins the test is about, and ends it as skipped when one is not in
  the install.
- `run.launch` is `launch` with the game's own health part of the test. It fails the run when a message
  box comes up (it would hang the run, so the game is ended), when the game crashes, when it does not quit
  by itself when asked, and when `fcse.log` reports a crash, a Lua error, or a failure from FCSE,
  DevTools or one of the test's mods. Another mod's failure is noted, not failed: a feature can rightly
  be missing in a world, as Inventory Overhaul's notebook is on the test range. It also switches
  DevTools' FPS counter off, since it changes every frame.
- `run.expect(label, held, detail)` and `run.expect_between(label, value, low, high)` record one check.
- `run.expect_picture(game, label, mask, tolerance)` takes a shot and compares it with the approved one
  of the same label. Both are shrunk to 480 pixels wide and softened, and the check fails when more than
  `tolerance` (5%) of any 16-pixel tile changed: two runs of the notebook test differed by under 1% of a
  tile, a missing FPS counter by 12%, another page by 37%. A failure adds a picture of what changed.
  Fix the light, clear the weather, `look` and `pause` before one; `mask` leaves rectangles out.
- `run.finish()` exits 0 when every check held, 1 when one failed or none was made, 4 when only pictures
  wait for approval. A skipped test exits 3.

### Approving pictures

A picture with no reference yet, or whose reference was taken with other plugins installed or at another
resolution, waits for approval, and so does one that failed. Show the user the run's `index.html`; once
they say the pictures are right:

```
python fc2drive.py approve runs\2026-11-09_14-02-12-notebook
```

That makes them the references, in `references/<test>/` beside the test, with a `setup.json` of the
plugins and resolution they were taken with. References are true of this machine's install only, so
they stay out of git.

## One command at a time

For looking around and deciding what to do next, with the game left running between commands:

```
python fc2drive.py start world1
python fc2drive.py state
python fc2drive.py entity player CPawn
python fc2drive.py goto 2604 2108 23 0
python fc2drive.py action use 1
python fc2drive.py shot C:\temp\here.png
python fc2drive.py stop
```

A script started this way does not keep the screensaver off, and the game hangs at the splash and stops
presenting once it starts. `launch` holds it off for as long as the block runs.

## Recording with NVIDIA

`run.record` presses NVIDIA's record hotkey and moves the clip into the run's folder. The hotkey and the
clips' folder are read from NVIDIA's own settings, so a rebind is picked up. The NVIDIA App's overlay has
to be on.

NVIDIA records a windowed game as the whole desktop, whatever else is on it. So the game's window stays
on top for the clip, and the clip is cropped to it afterwards with ffmpeg. The original is deleted. The
game keeps running and taking scripted input with another window focused, so the rest of the screen stays
usable. Sound follows NVIDIA's own audio settings, so anything else playing is in the clip too.

## Replaying a chunk of play

```python
game.start_recording()
# ...play by hand, or script it...
game.stop_recording("take.rec")

game.play("take.rec")
```

Start a replay from where the recording started: loading the same save, or a `goto` with the same facing.
A recording holds actions, not the player's position, so look direction is relative to wherever the view
already points. See [the plugin's notes](../../mods/DevTools/README.md#remote-control).

The game's own demo recorder is a different thing; see
[demo recording](../../docs/docs/engine-internals/demo-recording.md).
