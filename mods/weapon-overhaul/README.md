# Weapon Overhaul

An FCSE plugin for how aiming down the sights looks. Unfinished: the sway and the gun blur are in,
the scope effects are not.

## What it does

- **Sway.** Down the iron sights, the eye drifts a couple of millimetres off the gun and rises and
  falls with a slow breath. The rear sight is nearer the eye than the front one, so it moves further
  and the two drift out of line. Shots still go where the engine sends them: down the view, which is
  where the barrel points. Sights that have drifted out of line are wrong by the same angle as on a
  real gun. While a scope's own sight picture is up there is no sway, because that picture is a
  miniature right at the eye.
- **Gun blur.** Down the iron sights the eye focuses on the front sight, and the rest of the gun
  goes out of focus by how far it is from it, as through a 4 mm pupil: the rear sight most, and the
  blur spills a little past the gun's edge. It fades in as the eye settles into the sights.

The eye is moved through the camera's positional offset, the one the recoil kicks, in
`src/engine/aim.cpp`. The blur draws the gun's own depth pass a second time into a depth texture of
its own, in `src/engine/weapon_draws.cpp`, and blurs at the end of the pass the gun's colour is drawn
in, before the bloom, in `src/blur.cpp`. The engine side is in
[first-person aiming](../../docs/docs/engine-internals/first-person-aiming.md).

## Requirements

FCSE 1.3.0 or later: the blur hooks Direct3D functions other plugins hook too.

## Settings

On the plugin's page in the Mod Configuration Menu, stored under `[WeaponOverhaul]` in
`bin\fcse.ini`:

| Setting | Values | Default |
|---|---|---|
| Sway | Yes / No | Yes |
| Gun blur | Yes / No | Yes |

## Building

```
.\build.ps1                                     # x86 release -> out\build\x86-release\WeaponOverhaul.dll
.\build.ps1 -Install "C:\Games\Far Cry 2\bin"   # and copy it into bin\plugins\weapon-overhaul\
```

`fxc`, from the Windows SDK, compiles the shaders at build time.
