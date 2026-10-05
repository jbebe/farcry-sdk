---
title: Ambient birds fly closer
kind: component
bundle: graphics
status: located
systems: [graphics, environment]
match:
  - "worlds/world*/generated/world*_deploadnewparticles.rml#PartSys[*]/PartEmit[*]@PosOffset"
exclude: []
requires: []
verified: diff
---

# Ambient birds fly closer

The flocks of bird sprites that circle overhead in the jungle, savannah and swamp ambience are
emitted much nearer the player and much lower, so they read as birds nearby rather than specks high
up.

## How

The ambient particle systems that play around the player in each vegetation zone carry bird
emitters drawing the animated sprite `graphics\GFX\Environment\GFX_Birds_01.PNG`. The mod moves
their emitter offset (`PosOffset`) in both campaign worlds, `world1_deploadnewparticles.rml`
(`PartSys[289]`-`[293]`) and `world2_deploadnewparticles.rml` (`PartSys[270]`-`[274]`):

| Particle system | Emitter | `PosOffset` |
|---|---|---|
| `environment.ambiant.env_dense_jungle` | `ENV_Jungle.birds_closer` | `10,50,70` -> `08,13,18` |
| `environment.ambiant.env_light_jungle` | `ENV_Jungle.birds_closer` | `10,50,70` -> `08,13,18` |
| `environment.ambiant.env_medium_jungle` | `ENV_Jungle.birds_closer` | `10,50,70` -> `08,13,18` |
| `environment.ambiant.env_savannah` | `ENV_Jungle.birds_closer` | `10,50,70` -> `08,13,18` |
| `environment.ambiant.env_hod` | `ENV_Jungle.birds` (15 m sprites) | `50,0,70` -> `15,0,25` |

The third number is the height (the engine is Z-up), so the small birds come down from 70 m to
18 m and in from 50 m to 13 m; the large `env_hod` flock from 70 m to 25 m. `env_hod`'s own
`birds_closer` was already at `10,50,15` and is unchanged. The leading zero in `08,13,18` is the
mod's spelling.

Maps made with the editor copy their particle library from `tmpla`, and the mod's rewritten
`tmpla_deploadnewparticles.rml` keeps the base game's bird offsets, so this is campaign only.

## Uncertain

- That `PosOffset` is relative to the player (the ambient systems follow the camera) is inferred
  from the systems being ambience; it is not traced.
- Large flat bird sprites at 13-25 m may look worse up close than they did far away. Not seen in
  game.
