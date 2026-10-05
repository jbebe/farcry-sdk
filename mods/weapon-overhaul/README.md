# Weapon Overhaul

An FCSE plugin, with a data layer, for how aiming and shooting look: sway and a blurred gun down the
iron sights; a scope that shadows, is seen from its eyepiece, and has the world around it at the
eye's own field of view; and tracers. It changes only what is drawn: no damage, spread or recoil, and
no weapon's data, so it goes alongside mods that change those.

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
- **Scope shadow.** As the look turns, a soft dark crescent comes in from the eyepiece's rim on the
  side turned toward: hardly any on a slow move, a slight one on a fast flick, gone when the look is
  still or the scope comes down. It needs the eyepiece, whose opening it shades.
- **Eyepiece.** The engine draws a scope as a tube seen down its length, with the reticle and the
  zoomed view far at its end. Here the tube is not drawn. Every scope looks the same size instead:
  a pitch-black, out-of-focus body whose outer edge spans 80% of the screen's height, with a black
  mount below it down to the screen's foot, and the zoomed view filling its opening. The sniper
  scopes are a plain ring, thin on the Dart Rifle and the M1903 and thicker on the Dragunov and the
  AS50; the AR-16's and the MGL-140's, which are not round, also show their eyepiece's own shape.
  The reticle is the scope's own, drawn from the game's texture in its own colours, and fills the
  opening as it filled the lens. The magnification is unchanged. The view inside is seen through
  glass: lines bow slightly toward the rim, colours part there the more the scope magnifies, the
  rim darkens, and the coating tints it faintly. The scope is laid over the finished frame, after
  the tone mapping, and trails the look a little as it turns. Each round fired kicks it back toward
  the eye for a moment, as the stock comes back onto the shoulder. A scope the plugin does not know
  is drawn as the game draws it, and named in `fcse.log`.
- **Surroundings.** Around the eyepiece the world is seen at the field of view the eye has without
  a scope, as with both eyes open, while the eyepiece keeps the scope's magnified view. With the
  eyepiece on too, the view does not zoom as the scope comes up: the gun comes to the eye, and the
  magnification is there at once inside the eyepiece, so the world around it never changes. The
  surroundings are evenly out of focus, as the other eye sees past a scope, and the game's own
  radial blur toward the screen's edges is left out. They are a second, cheaper view drawn by the
  engine's water reflection renderer, at half the screen's size with less detail; the magnified
  view inside stays the engine's own.
- **Tracers.** Only the M249, the PKM, the Dragunov and the AS50 fire them, at the rate the game
  gives each, the player's own shots included, which the game never draws. The streak is a bright
  orange glow with a hotter core, which stays as bright far off as near.

The eye is moved through the camera's positional offset, the one the recoil kicks, and a scope's
zoom held back until its sight picture is up, in `src/engine/aim.cpp`. The blur draws the gun's own
depth pass a second time into a depth texture of its own, in `src/engine/weapon_draws.cpp`, and the
blur and the scope shadow draw at the end of the pass the gun's colour is drawn in, before the
bloom, in `src/blur.cpp` and `src/scope_shadow.cpp`.
The eyepiece drops the engine's scope and draws its own over the finished frame, in
`src/eyepiece.cpp`, from what was measured off each scope's mesh, in `src/scopes.cpp`. The second
view is drawn in `src/engine/second_view.cpp` and laid down as the gun's colour pass begins, in
`src/surroundings.cpp`. Which shots leave a tracer is decided in `src/tracers.cpp`, and the
tracer's texture, `layer\mods\graphics\gfx\weapons\bullettracer_d.xbt`, is drawn by
`textures\bullettracer_d.ps1`. The engine side is in
[first-person aiming](../../docs/docs/engine-internals/first-person-aiming.md),
[bullet tracers](../../docs/docs/engine-internals/bullet-tracers.md) and
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
| Tracers | Yes / No | Yes |

With Tracers off, the game decides which shots leave one again, and they keep the orange texture.

## Building

```
.\build.ps1                                     # x86 release -> layer\plugins\weapon-overhaul\
.\build.ps1 -Install "C:\Games\Far Cry 2\bin"   # and build the layer into the game with jackall-cli
.\textures\bullettracer_d.ps1                   # redraw the tracer texture into the layer
```

`fxc`, from the Windows SDK, compiles the shaders at build time. `-Install` builds the game's patch
from this layer alone, dropping any other layer built into it before.
