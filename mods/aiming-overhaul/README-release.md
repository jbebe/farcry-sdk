# Aiming Overhaul

How aiming and shooting look in Far Cry 2: sway and a blurred gun down the iron sights, scopes seen
from the eyepiece with the world around them, and tracers. It changes only how things look: no
damage, spread or recoil. Its weapon data sets only which guns fire tracers, how fast they fly, and
the scopes' iron-sight effect, so it merges with mods that change anything else about a weapon.

- **Sway.** Down the iron sights the eye drifts a couple of millimetres off the gun and rises and
  falls with a slow breath, so the rear and front sights drift out of line as on a real gun. Shots
  still go where the game sends them.
- **Gun blur.** Down the iron sights the eye focuses on the front sight and the rest of the gun goes
  out of focus by how far it is from it, the rear sight most. A pistol's sights are too close
  together to blur.
- **Scope eyepiece.** Instead of a long tube, every scope is a black, out-of-focus eyepiece spanning
  80% of the screen, with the zoomed view filling its opening. The view inside is seen through
  glass, and the scope trails the look a little and kicks back with each shot.
- **Reticles**, new in every scope: a hunting duplex on the Dart Rifle and the M1903, a PSO-1 with
  its rangefinder on the Dragunov, a red tactical mil-scale on the AS50, and a lit holographic
  chevron on the AR-16 and the MGL-140.
- **Scope surroundings.** Around the eyepiece the world stays at your normal field of view, slightly
  out of focus, as with both eyes open. The magnification is there at once inside the eyepiece, so
  the view no longer zooms as the scope comes up.
- **Scope shadow.** A soft dark crescent comes in from the eyepiece's rim on a fast turn.
- **Tracers** from the M249, the PKM, the Dragunov, the AS50 and the mounted machine guns, your own
  shots included: bright orange streaks at 350 m/s, visible however far off. One in six glances off
  where it lands, up into the air.

## Settings

In the **Mod Configuration** menu, and under `[AimingOverhaul]` in `bin\fcse.ini`:

| Setting | Values | Default |
|---|---|---|
| Sway | Yes / No | Yes |
| Gun blur | Yes / No | Yes |
| Scope shadow | Yes / No | Yes |
| Scope eyepiece | Yes / No | Yes |
| Scope surroundings | Yes / No | Yes |
| Tracers | Yes / No | Yes |

The scope shadow needs the eyepiece, whose opening it shades. With Tracers off, your own shots leave
none, none ricochets, and they are drawn as the game draws them; which guns fire them and how fast
they fly stay as the mod's weapon data sets them.

## Requirements

- **FCSE 1.3.0 or later**, on Far Cry 2 1.03 (Steam or GOG).
- **JackAll 1.0.0 or the Vortex extension 0.2.0, or later**, to install it.

## Installing

This archive is a JackAll layer: `mods\` at its root is the weapon data, `plugins\` is the FCSE
plugin, and both installers read that same shape.

- **Vortex** — with the Far Cry 2 extension installed, drop the zip in and enable it.
- **JackAll** — add the zip in the app, or from the command line:

```
jackall-cli mod build   --game "C:\Games\Far Cry 2" --layer aiming-overhaul.zip
jackall-cli mod restore --game "C:\Games\Far Cry 2"
```

`mod restore` is the uninstall, and removes the plugin too.

## Compatibility

- **Mods that change weapons' stats or data** merge with it field by field.
- **Mods that change the same fields** — a weapon's tracer settings or its iron-sight effect — clash
  on those fields, and the installer reports it.
