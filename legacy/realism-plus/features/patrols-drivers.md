---
title: Patrol driver archetypes with close-range loadouts
kind: shared
status: located
systems: [patrols, ai, weapons]
match:
  - "generated/entitylibrarypatchoverride.fcb/enemy_archetypes/{blue,red}_faction/patrol_driver.xml"
exclude: []
requires: [weapons-new-enemy-packs]
verified: diff
---

# Patrol driver archetypes with close-range loadouts

Two new soldier archetypes for the driver's seat of patrol vehicles, one per faction, armed with a
shotgun and a sidearm instead of a rifle. `patrols-blue-crews` and `patrols-red-faction-crews` seat
them.

## How

- `enemy_archetypes.Blue_Faction.Patrol_Driver` and `Red_Faction.Patrol_Driver`, new whole units in
  `generated/entitylibrarypatchoverride.fcb/enemy_archetypes/{blue,red}_faction/patrol_driver.xml`.
  Each is a copy of its faction's `Assault_Caucasian` with the black rifleman's look (tag `nubian`
  and a `PartOverwrite` list of its own), inventory pack `patroldriver`, `fPreCombatMultiplier`
  `0.6` (as `ai-stealth-precombat` sets on the riflemen) and, on the blue one, no
  `FootstepSpeedSwitch` entries.
- The pack they name, `Pack[patroldriver]` in `engine/gamemodes/gamemodesconfig.xml`, is on
  `weapons-new-enemy-packs`: per progression level a shotgun (Ithaca, SPAS-12, USAS-12, the DLC
  silenced shotgun), a pistol or SMG sidearm, the merc flare gun and three M67 grenades.

This is Boggalog's "How to create new driver/gunner enemy types" recipe
([patrols](../../../docs/docs/modding/guide/patrols.md#guide---how-to-create-new-drivergunner-enemy-types)),
except that it adds a new pack instead of reusing the Carl Gustaf one.

## Uncertain

- The guide says new packs cannot be created; the mod adds one anyway, naming it by hash
  (`0A4B892E`) in both archetypes. Whether the engine resolves a pack name it did not ship is not
  traced; if it does not, drivers fall back to whatever the engine does with a missing pack.
