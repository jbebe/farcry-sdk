# fc2drive

Drives Far Cry 2 from outside, to test mods without playing through the test by hand: start from a
save, teleport, fire actions, take pictures, record the run with NVIDIA, replay a chunk of play.

It talks to [DevTools' remote control](../../mods/DevTools/README.md#remote-control), so DevTools has to
be in the install. It needs Python 3 and nothing else, plus ffmpeg with NVENC on `PATH` for clips. The
install it drives is `C:\Games\Far Cry 2`, or `FC2_GAME` if that is set.

## Scenarios

```
python scenarios\guns.py                  every Armory gun: one shot, a longer burst, in one clip
python scenarios\guns.py --only AK-47,M79 --no-clip
python scenarios\locations.py             a picture at each spot in scenarios\spots\world1.json
python scenarios\locations.py --hour 22 --clip
python scenarios\inventory.py             the notebook's pages, then a pickup; needs Inventory Overhaul
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

## One command at a time

For looking around and deciding what to do next, with the game left running between commands:

```
python fc2drive.py start world1
python fc2drive.py state
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
