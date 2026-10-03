# Weapon Overhaul

An FCSE plugin for how aiming down the sights looks: sway and a blurred gun down the iron sights; a
scope that shadows, is seen from its eyepiece, and has the world around it at the eye's own field
of view.

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
- **Scope shadow.** As the look turns, a soft dark crescent comes in from the lens's rim on the
  side turned toward: hardly any on a slow move, a slight one on a fast flick, gone when the look is
  still or the scope comes down. The scope itself moves only as the game moves it. The lens, its
  centre and its radius, is found on screen as the hole the scope's housing leaves in the gun's
  depth, so it fits every scope.
- **Eyepiece.** The engine draws a scope as a tube seen down its length, with the reticle and the
  zoomed view far at its end. Only the tube's first centimetre is kept, so the zoomed view fills the
  eyepiece, and the reticle grows with the opening so it sits in it as it sat in the lens. The
  magnification is unchanged. Once a scope has come up and settled, its depth is read back until
  two readings agree, to find the eyepiece and both openings, and what is found is kept for that
  scope; so it fits every scope the engine draws this way. The reticle stays on the scope's frame,
  pitch black and a little soft, and shows only through the opening: it is drawn into a mask of its
  own and laid over the finished frame, after the tone mapping, so nothing lightens it.
- **Surroundings.** Around the eyepiece the world is seen at the field of view the eye has without
  a scope, as with both eyes open, while the eyepiece keeps the scope's magnified view. The
  surroundings are a second, cheaper view drawn by the engine's water reflection renderer, at half
  the screen's size with less detail; the magnified view inside stays the engine's own.

The eye is moved through the camera's positional offset, the one the recoil kicks, in
`src/engine/aim.cpp`. The blur draws the gun's own depth pass a second time into a depth texture of
its own, in `src/engine/weapon_draws.cpp`, and the blur and the scope shadow draw at the end of the
pass the gun's colour is drawn in, before the bloom, in `src/blur.cpp` and `src/scope_shadow.cpp`.
The lens is found as the gun's depth pass ends, in `src/scope_lens.cpp`. The eyepiece cuts the
scope's draws with a clip plane and scales the reticle's in clip space, in `src/eyepiece.cpp`. The second view is drawn
in `src/engine/second_view.cpp` and laid down as the gun's colour pass begins, in
`src/surroundings.cpp`. The engine side is in
[first-person aiming](../../docs/docs/engine-internals/first-person-aiming.md) and
[presenting a frame](../../docs/docs/engine-internals/presentation-and-input.md).

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
| Scope surroundings | Yes / No | Yes |

## Building

```
.\build.ps1                                     # x86 release -> out\build\x86-release\WeaponOverhaul.dll
.\build.ps1 -Install "C:\Games\Far Cry 2\bin"   # and copy it into bin\plugins\weapon-overhaul\
```

`fxc`, from the Windows SDK, compiles the shaders at build time.
