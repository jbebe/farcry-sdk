# Sky Overhaul

Replaces Far Cry 2's sky, clouds and the light they cast with an atmosphere that follows the sun
through the whole day, the weather and the night.

- **The sky**, worked out from the sun's position in place of the engine's painted dome, with the
  land's distant fog in the sky's own horizon colour.
- **Volumetric clouds**, a high cirrus sheet and aircraft trails. They drift with the game's clock,
  so loading a save shows the sky you left, and every midnight rolls a new set of one to three
  trails.
- **Storms** that thicken the clouds and grey the light without drowning the land in fog.
- **The sun**: light shafts that stop at the clouds, a glare the clouds cover, and an afterimage.
- **Cloud shadows** on the ground.
- **The night**: a smaller moon lit on the clouds, darker nights with moonlight that throws shadows,
  and the world's colour draining where it is dark.
- **A neutral colour grade** in place of the game's yellow cast.

Each part switches back to the engine's own in the **Mod Configuration** menu. Every effect's values
are in `bin\sky-overhaul.ini`, and with DevTools installed its overlay has a window of sliders for
them.

## Requirements

- **FCSE**, on Far Cry 2 1.03 (Steam or GOG).
- **Bloom on** in Options → Video.
- **Depth pass quality high** or above, for the cloud shadows only.

## Installing

This archive is a JackAll layer: `mods\` at its root is game data, `plugins\` is the FCSE plugin, and
both installers read that same shape.

- **Vortex** — with the Far Cry 2 extension installed, drop the zip in and enable it.
- **JackAll** — add the zip in the app, or from the command line:

```
jackall-cli mod build   --game "C:\Games\Far Cry 2" --layer sky-overhaul.zip
jackall-cli mod restore --game "C:\Games\Far Cry 2"
```

`mod restore` is the uninstall, and removes the plugin too.

## Compatibility

- **Lighting, fog and weather mods** conflict: the night lighting ships as the world's whole preset
  library, and the moon and storm changes as its whole environment block.
- **Other rendering plugins** that hook the same Direct3D calls are refused by FCSE. DevTools and
  UFCP work alongside it.

## Known issues

- The night's colour drain also applies indoors.
