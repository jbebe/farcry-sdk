# Third Person

Far Cry 2 seen from outside when you want it: over your shoulder on foot, behind the car at the
wheel. And in first person, your own legs under you and a shadow at your feet.

## What it does

- **A Third-person view control**, on V by default and rebindable in Options > Controls, under Misc.
  It switches between first and third person, on foot and at the wheel. The view you choose is kept
  until you switch back.
- **On foot, over the right shoulder.** You see your whole body, with the game's own animations,
  the weapons you carry and your shadow.
  - The game's own crosshair is shown, and your shots land on it.
  - Hold aim (right mouse) and the camera comes closer over the shoulder and the crosshair tightens.
    There are no iron sights in third person.
  - You carry your gun low as you walk and run, and raise it to aim, to fire, and for a second after
    a shot.
  - The map, phone and notebook are not hung on your belt; the one in your hands shows.
- **At the wheel, a chase camera.** It sits behind the car and swings after its heading and height
  with a lag, so you see the body work on its suspension. The mouse orbits it, and two seconds after
  the mouse stops it eases back behind the car. You see yourself driving, with the game's own driving
  animations. A passenger seat or a turret is seen in first person.
- **The camera keeps out of walls.** Where the ground, a wall or a rock comes between you and the
  camera, it pulls in toward you quickly, and eases back out once the way is clear.
- **Your legs in first person.** Look down and you see your legs and boots, striding as you walk,
  strafe and back up, with the knees out when you crouch.
- **A soft shadow under your feet** in first person, where the game casts none.

## Settings

In **Options > Mod Configuration Menu**, and under `[Third Person]` in `bin\fcse.ini`:

| Setting | Values | Default |
|---|---|---|
| Legs in first person | On / Off | On |
| Shadow in first person | On / Off | On |

The shadow needs the game's depth pass, which `DepthPassQuality` high or above draws.

## Requirements

- **FCSE 1.4.0 or later**, on Far Cry 2 1.03 (Steam or GOG).
- **JackAll 1.4.0 or the Vortex extension 0.4.0, or later**, to install it.

## Installing

This archive is a JackAll layer: `mods\` at its root is game data, `plugins\` is the FCSE plugin, and
both installers read that same shape.

- **Vortex**: with the Far Cry 2 extension installed, drop the zip in and enable it.
- **JackAll**: add the zip in the app, or from the command line:

```
jackall-cli mod build   --game "C:\Games\Far Cry 2" --layer third-person.zip
jackall-cli mod restore --game "C:\Games\Far Cry 2"
```

`mod restore` is the uninstall, and removes the plugin too.

## Compatibility

- **Vehicle Overhaul 1.0.1 and earlier** have a third-person view of their own on V. Use Vehicle
  Overhaul 1.1.0 or later, which leaves the view to this mod.
- **Mods that change the controls** combine with it when they ship only the sections they change,
  the way this one does. One that ships `config\defaultusercontrols.xml` or
  `config\inputactionmapcommon.xml` whole still gets the Third-person view control, but loses its own
  changes to the Misc controls and the gameplay and driving action maps, and the installer says so.

## Known limits

- Moving crouched in third person plays the standing legs: the game has no crouch-walk animation.
- There is no jump in third person: the game's jump animation is for multiplayer only.
- Getting in or out of a car shows first person while you climb in or out.
- The camera keeps out of the ground, walls and rocks, but not out of other cars or people, and can
  clip a corner beside its line of sight.
