# Sky Overhaul

Replaces Far Cry 2's sky, clouds and the light they cast with an atmosphere that follows the sun
through the whole day, the weather and the night.

- **The sky**, worked out from the sun's position in place of the engine's painted dome, with the
  land's distant fog in the sky's own horizon colour.
- **Volumetric clouds**, a high cirrus sheet and aircraft trails. They drift with the game's clock,
  so loading a save shows the sky you left, and every midnight rolls a new set of one to three
  trails.
- **Weather** that changes by itself and saves with the game's clock: clear days, haze, morning
  fog, overcast, drizzle and rain, wind and dust, and showers and storms that drift over the map
  behind a dark warning sky, most over the jungle and few over the desert.
- **Rain you can see**, thousands of streaks slanted by the wind and none under a roof, and a wet
  world after it: mud, shining surfaces, puddles on level ground that mirror what stands round them
  and rings on lakes and rivers, all drying over a few hours.
- **Lightning and thunder**: clouds lit from inside, branching bolts, a flash on the land by how
  near the strike is, and thunder as late as sound would bring it.
- **A rainbow** opposite a low sun once the rain where you stand stops.
- **The sun**: light shafts that stop at the clouds, and a glare the clouds cover.
- **Cloud shadows** on the ground.
- **The night**: a smaller moon lit on the clouds, darker nights with moonlight that throws shadows,
  clouds that keep the moonlight off the ground, and the world's colour draining where it is dark.
- **A neutral colour grade** in place of the game's yellow cast, a faint film grain, and ambient
  occlusion in corners and creases.
- **The world in that light**: grass and tree leaves lit by the sun, and sun shadows that reach
  further and follow a low sun toward the horizon.
- **Heat haze and mirages**: on sunny days distant land shimmers and smudges in the hot air near the
  ground, more the farther it is, and bare level ground far off mirrors the sky.
- **Land to the horizon**: where the game stops drawing the world short of the skyline, the gap is
  filled with far, hazy land instead of sky.
- **Flatten**: distant land drawn flatter, so the world reads bigger than it is.

Each part switches back to the engine's own in the **Mod Configuration** menu. Every effect's values
are in `bin\sky-overhaul.ini`, and with DevTools installed its overlay has a window of sliders for
them. **Upgrading from 1.1.0**, delete that file once so the new values take effect.

## Requirements

- **FCSE 1.5.0 or later**, on Far Cry 2 1.03 (Steam or GOG).
- **Bloom on** in Options → Video.
- **Depth pass quality high** or above, for the cloud shadows and the heat haze only.

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

- **Lighting, fog and weather mods** conflict: the night and clear-day lighting ship as the world's
  whole preset library, and the moon and storm changes as its whole environment block.
- **Other rendering plugins** can hook the same calls in the game's renderer; FCSE runs both.
  DevTools and UFCP work alongside it.
- **Third Person 1.0.2 or later** alongside this version: an older one cannot read the depth this
  version stores.

## Known issues

- The night's colour drain also applies indoors.
- With Flatten on, things beyond about 250 m are drawn lower or higher than they are, so long shots
  land slightly off from where they appear.
