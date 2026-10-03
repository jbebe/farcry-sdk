# Weapon Overhaul

An FCSE plugin for how aiming down the sights looks. Unfinished: the sway, the gun blur, the scope
shadow and the eyepiece are in, a picture-in-picture scope is not.

## What it does

- **Sway.** Down the iron sights, the eye drifts a couple of millimetres off the gun and rises and
  falls with a slow breath. The rear sight is nearer the eye than the front one, so it moves further
  and the two drift out of line. Shots still go where the engine sends them: down the view, which is
  where the barrel points. Sights that have drifted out of line are wrong by the same angle as on a
  real gun. While a scope's own sight picture is up there is no sway, because that picture is a
  miniature right at the eye.
- **Gun blur.** Down the iron sights the eye focuses on the front sight, and the rest of the gun
  goes out of focus by how far it is from it, as through a 4 mm pupil: the rear sight most, and the
  blur spills a little past the gun's edge. It fades in as the eye settles into the sights. A gun
  held far from the eye, like a pistol, has its sights too close together in dioptres for a blur
  you could see, and gets none, as a real eye nearly does.
- **Scope lag.** Through a scope, the scope follows the look on a spring: a sudden jerk leaves it
  behind on screen, and it swings back a little past before it settles. The eye is moved ahead of it
  through the same camera offset as the sway, which the scope's own sight picture, a miniature at
  the eye, turns into a large shift on screen.
- **Scope shadow.** The scope trails the camera as the look turns, so the eye runs ahead of it and
  a dark crescent comes in from the lens's rim on the side turned toward: hardly any on a slow
  move, a slight one on a fast flick, gone when the look is still or the scope comes down. The
  lens, its centre and its radius, is found on screen as the hole the scope's housing leaves in the
  gun's depth, so it fits every scope and follows it as it lags.
- **Eyepiece.** The engine draws a scope as a tube seen down its length, with the reticle and the
  zoomed view far at its end. Only the tube's first centimetre is kept, so the zoomed view fills the
  eyepiece, and the reticle grows with the opening so it sits in it as it sat in the lens. The
  magnification is unchanged. Each time a scope comes up, its depth is read back once to find the
  eyepiece and both openings, so it fits every scope the engine draws this way. The reticle sits at
  the scope's far end, so as the look turns it swings off centre with the shadow, centred on the
  shadow's clear circle, and shows only through the opening. Shots still go to the screen's
  centre.

The eye is moved through the camera's positional offset, the one the recoil kicks, in
`src/engine/aim.cpp`. The blur draws the gun's own depth pass a second time into a depth texture of
its own, in `src/engine/weapon_draws.cpp`, and the blur and the scope shadow draw at the end of the
pass the gun's colour is drawn in, before the bloom, in `src/blur.cpp` and `src/scope_shadow.cpp`.
The eyepiece cuts the scope's draws with a clip plane and scales the reticle's in clip space, in
`src/eyepiece.cpp`.
The engine side is in
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
| Scope shadow | Yes / No | Yes |
| Scope eyepiece | Yes / No | Yes |

## Building

```
.\build.ps1                                     # x86 release -> out\build\x86-release\WeaponOverhaul.dll
.\build.ps1 -Install "C:\Games\Far Cry 2\bin"   # and copy it into bin\plugins\weapon-overhaul\
```

`fxc`, from the Windows SDK, compiles the shaders at build time.
