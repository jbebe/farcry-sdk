# Third Person

The player's own legs under the first-person view, walking as the player moves, and the player seen
from outside: over the shoulder on foot, behind the car at the wheel. An FCSE plugin with a small data
layer, the Third-person view control and the quad's driving clip; it works on whichever character
the player plays.

| Part | Status |
|---|---|
| Legs and boots seen looking down, standing straight | seen in game (test range, scripted shots) |
| A stride that follows the way the player moves: walking, strafing, backing up | seen in game |
| A crouch with the knees out, so the lowered eye sees past them | seen in game |
| Seen from a camera away from the eye, such as DevTools' free camera: the whole body, head and back included, that does not vanish at 2 m; its arms keep the first-person pose | seen in game |
| Feet tucked up in the air | built, not seen |
| A soft round shadow on the ground under the feet, where the engine casts none in first person | seen in game (test range, scripted shots) |
| A **Third-person view** control, V by default, rebindable under Misc in Controls, on foot and at the wheel; the view chosen is kept for the session | built, not seen |
| Third person on foot: over the right shoulder, the full body with its own animations, weapons and shadow, the game's own crosshair with shots landing on it, and no iron sights | seen in game (test range, scripted shots) |
| Third person at the wheel: a chase camera 6 m behind the car, swinging after its heading and height, orbited by the mouse and easing back behind two seconds after it stops, kept above the ground; the driver's whole body with the game's driving animations, the quad's steering centred | seen in game as Vehicle Overhaul's; moved here, not seen |
| In third person, the map, phone and notebook hidden from the belt, but the one in hand | built, not seen |
| In third person, the camera pulled in toward the player, or toward the car at the wheel, where the ground, a wall or a rock comes between, quickly, and let back out softly once it is gone | built, not seen |
| In third person on foot, the aim key held brings the camera closer over the right shoulder and draws the crosshair at its tightest | built, not seen |
| In third person on foot, the gun carried down on the move and standing, and held up while aiming, firing and a second after a shot | built, not seen |

## How it works

The player has two models: the full body, and the headless avatar the first-person view is drawn with.
The engine discards the full body when first person comes up, and draws the avatar in the gun's own
near depth range, in front of the world. The first-person animation holds the avatar's legs bent back
out of the camera's sight.

- `engine/graphic.cpp` keeps the full body, and in first person on foot draws it in every viewport and
  in the world's own depth, so walls and the ground hide its legs. The engine's model switch and its
  near depth range undo that from many callers, the free camera every frame, so it is put back after
  each.
- `body.cpp` trims each model in its own skin matrices, which the skinning job builds for one model at
  a time: the full body keeps only its hips and legs, and the avatar everything else. A trimmed bone's
  matrix takes its vertices to a single point.
- `legs.cpp` rebuilds the leg bones after each posing from the full body's rest pose, read from its
  mesh's inverse bind matrices. The hips hang below the spine where the animation holds it, which
  crouching lowers and looking down swings back; the feet go halfway with them. Each foot follows a
  stride worked out from how far the player moves, and two-bone IK bends the knee to reach it.
- `shadow.cpp` darkens a soft circle around where the feet stand, at the frame's sky pass, from the
  engine's own linear depth: `shaders/shadow.fx` finds each pixel's place in the world and darkens
  it by its distance from the feet, so the circle lies on slopes and steps. The depth texture is
  found by watching a draw that reads it (`engine/scene_depth.cpp`).
- `outside.cpp` puts up the engine's `Cameras.Camera.Third` when the control's signal asks for
  it, and takes the player out of first person, which brings the full body, its animations and its
  shadow; its update is taken over (`engine/third_camera.cpp`) to place it. Leaving first person
  shows everything the player carries, so the gadgets not in hand are hidden again, once. A cutscene
  or a load that takes the camera puts first person back until the next press, or until the player
  gets in or out of a car.
  - On foot it stands over the right shoulder. The HUD is told to draw the game's own crosshair, each
    shot is aimed from the muzzle at what a bullet would hit under the screen's centre, and the iron
    sights are let go of. The game's own aim signals move the camera in, and the spread the crosshair
    is drawn from goes to nought. First person leaves the player in the stance that holds the gun up,
    so the full body would run aiming; it is lowered unless the player aims or fires.
  - At the wheel, `chase.cpp` places it behind the car, orbited by the mouse look, which is taken from
    the input pass so the driver's own view stays still. Who drives is followed through the game
    giving each car its driver (`engine/driver.cpp`). A passenger seat or a turret, and getting in
    or out, are seen in first person.
  - Either camera hangs on `boom.cpp`'s boom from the player, or from above the car: a ray out to
    where the camera wants to stand, through the layers a car's wheels hit, so neither the player
    nor the car stops it. What it hits shortens the boom within about a tenth of a second, and it
    lengthens again over about three quarters of one.

The engine side is in [first-person aiming](../../docs/docs/engine-internals/first-person-aiming.md#the-first-person-body).

## Settings

`Legs in first person` and `Shadow in first person`, both on by default, under `[Third Person]` in
`bin\fcse.ini` and on the Mods page. Off, the player is drawn as in retail. The shadow needs the
engine's linear depth, which `DepthPassQuality` high or above draws. The Third-person view control
is in Options > Controls, under Misc.

## Building and installing

```
.\build.ps1
```

builds `ThirdPerson.dll` (32-bit) and stages it in `layer\plugins\third-person\`. The game gets it with
the layer, from `jackall-cli mod build` together with every other layer installed. Needs FCSE 1.3.0
or later, since it hooks the same posing functions as Aiming Overhaul, and JackAll 1.4.0 or later,
which merges the control sections with other mods'.

```
layer\mods\
  config\                    the Third-person view control and its bindings on foot and driving
  languages\                 the control's label, in every language
  graphics\move\             the quad's driving clip with its steering centred
layer\plugins\third-person\   the built plugin, staged here by build.ps1
```

## Open

- In a vehicle in first person, the avatar is drawn as in retail.
- Not yet checked in game: cutscenes and other full-body first-person clips, whose legs the plugin
  overrides; ladders; swimming; slopes, where the feet stand on the body's level ground.
- Third person: the camera is pulled in along one ray, so it can still clip a corner or a thin post
  beside that line, and other cars and people do not stop it. Getting in or out of a car shows first
  person between the two views. Cutscenes and a gadget picked up in third person are not checked.
  Aiming does not tighten the spread; only the view and the crosshair change. Moving crouched plays
  the standing legs, because the game has no crouch-walk animation. There is no jump animation: the
  game's is for multiplayer only.