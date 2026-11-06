# Player Body

The player's own legs under the first-person view, walking as the player moves. An FCSE plugin, with
no data layer: it changes nothing on disk, and works on whichever character the player plays.

| Part | Status |
|---|---|
| Legs and boots seen looking down, standing straight | seen in game (test range, scripted shots) |
| A stride that follows the way the player moves: walking, strafing, backing up | seen in game |
| A crouch with the knees out, so the lowered eye sees past them | seen in game |
| Seen from a camera away from the eye, such as DevTools' free camera: the whole body, head and back included, that does not vanish at 2 m; its arms keep the first-person pose | seen in game |
| Feet tucked up in the air | built, not seen |
| A soft, shapeless shadow where the engine casts none in first person, at the angle of the world's shadows: fainter toward the head's end, shorter crouched, fading out with the night and a storm | seen in game (test range, scripted shots; night and storm not seen) |

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
- `shadow.cpp` stands a column of seven spheres from the feet to the eye, and at the frame's sky pass
  multiplies the world by their shadow, worked out per pixel from the engine's own linear depth in
  `shaders/shadow.fx`. Each sphere's shadow blurs and pales with its distance from the body. It is
  cast along the direction the engine lays every other shadow along, which holds a low sun higher
  than it stands, read with the night and the storm from the engine's cloud-layer submission
  (`engine/sun.cpp`). The depth texture is found by watching a draw that reads it
  (`engine/scene_depth.cpp`).

The engine side is in [first-person aiming](../../docs/docs/engine-internals/first-person-aiming.md#the-first-person-body).

## Settings

`Legs in first person` and `Shadow in first person`, both on by default, under `[Player Body]` in
`bin\fcse.ini` and on the Mods page. Off, the player is drawn as in retail. The shadow needs the
engine's linear depth, which `DepthPassQuality` high or above draws.

## Building and installing

```
.\build.ps1 -Install "C:\Games\Far Cry 2\bin"
```

builds `PlayerBody.dll` (32-bit) and copies it to `bin\plugins\player-body\`. Needs FCSE 1.3.0 or later:
it hooks the same posing functions as Aiming Overhaul.

## Open

- In a vehicle the full body is hidden and the avatar drawn as in retail.
- Not yet checked in game: cutscenes and other full-body first-person clips, whose legs the plugin
  overrides; ladders; swimming; slopes, where the feet stand on the body's level ground.
- The shadow darkens ground the engine already shades, such as under a roof or a tree, a second time.
