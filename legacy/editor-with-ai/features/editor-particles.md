---
title: World 2's particle library in the editor's template world
kind: component
bundle: editor-content
status: located
systems: [graphics, world]
match:
  - "worlds/tmpla/generated/tmpla_deploadnewparticles.rml#**"
exclude: []
requires: [editor-depload]
verified: diff
---

# World 2's particle library in the editor's template world

The editor's template world (`tmpla`), which every editor map is built on, gets the campaign's act 2
particle library in place of its own. Effects that only the campaign had now play in editor maps:
flies and butterflies, fireflies, cockroaches, waterfalls and river foam, campfire smoke, burning car
hulks, the pipeline and bridge explosions, and the small effects of NPC idle animations (cigarette
smoke, a zippo, welding sparks).

## How

`worlds/tmpla/generated/tmpla_deploadnewparticles.rml` is replaced whole by a byte-for-byte copy of
the base game's `worlds\world2\generated\world2_deploadnewparticles.rml` (5,979,813 bytes against
vanilla's 5,571,059).

By particle system name, vanilla `tmpla` has 347 systems and world 2 has 382. The 330 they share are
identical in both, so the real difference is 52 systems gained and 17 lost. The analysis pairs
`PartSys` elements by position, and the insertions and removals shift that pairing, which is why
one file swap shows as 10,186 changes (`PartSys`, `PartEmit` and keyframe additions, removals and
value changes between unrelated systems). They only make sense applied together.

- **Gained (52)**: `environment.animals.flies`, `butterfly`, `lucioles`, `roaches` and
  `roaches_interior`, `environment.waterfall_gen.*` (six), `environment.river.*` (four),
  `environment.background.campfire_high_smoke`, `dark_smoke_little` and `smoking_debris`,
  `fires.background_fires.car_hull_fire`, `fire_propagation.fire_propagation.fire_glow_far`,
  `explosions.buildings.bridge`, `bridge_side` and the `pipeline_*` effects,
  `explosions.explosives.bomb_shell` and `mountain_dynamite`, the `misc_animation.stp.*` idle
  effects (`cigaret_puff`, `cigaret_smok`, `zippo`, `welding`, `lite_camp_fire`, `water_drip`,
  `piss` and others), the scripted-scene `misc_animation.scripted_event.*` effects, and the barge's
  `vehicles.boats.barge_*`.
- **Lost (17)**: the multiplayer markers `game_play.game_play.multi_smok_obj_a`, `_b`, `_green` and
  `_red`, and the breakup effects of some multiplayer-only props: wooden doors, shutters and windows
  (`destructibility.wood.*`, nine), `destructibility.objects.plant_pot` and `nitrous_valve_cut`,
  `destructibility.smash.large_window_borders` and `explosions.explosives.train_fuel_explo`.

## Depends on

- [`editor-depload`](editor-depload.md) puts world 2's dependency index in place for the same world.
  It lists each particle system's textures, so the two belong together.
- The RaZoR particle archetypes ([`editor-razor-library`](editor-razor-library.md),
  [`editor-palette-effects`](editor-palette-effects.md)) and the NPC idle effects of AI placed with
  [`ai-people-palette`](ai-people-palette.md) need systems from this library.

## Uncertain

- Multiplayer maps that use the lost `multi_smok_obj_*` markers, or the lost breakup effects, would
  play those effects without particles. Which multiplayer objects reference them has not been
  checked.
- The mod also edits world 1's and world 2's own particle libraries (five changes each). Those are
  not on this page.
