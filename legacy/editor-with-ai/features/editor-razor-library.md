---
title: Janne252's "dlc2" library of custom archetypes
kind: shared
status: partial
systems: [world, vehicles, weapons, ai, missions]
match:
  - "_hash/bc494ad9.fcb"
  - "_hash/6fddb0b7.rml"
exclude: []
requires: []
verified: diff
---

# Janne252's "dlc2" library of custom archetypes

A shared piece: a separate entity library of 118 custom archetype declarations (117 names, one
declared twice), packaged the way a DLC packages its library, plus that package's particle library. It is the origin of every `Custom.RaZoR_Object.*`
and `NoSync.RaZoR_Object.*` name in the palette. Nothing in the editor changes from this page alone.
The palette pages that list its archetypes require it, and so does
[`scripts-community-samples`](scripts-community-samples.md).

## How

Two whole new files. Their names were recovered by hashing candidate paths (CRC32 of the lowercased
backslash path):

| File | Path | What it is |
|---|---|---|
| `_hash/bc494ad9.fcb` (718,559 bytes) | `dlc2\dlc2_entitylibrary.fcb` | the entity library |
| `_hash/6fddb0b7.rml` (5,615,421 bytes) | `dlc2\dlc2_deploadnewparticles.rml` | byte for byte the base game's `worlds\world1\generated\world1_deploadnewparticles.rml` |

The library's archetypes, by group (`hidName`):

- **Particle effects**, `Custom.RaZoR_Object.ParticlesEffect1`..`20`: a `CNewParticlesComponent`
  with `bAutoStart` on, naming a campaign particle system. 15 of the 20 are missing from the
  template world's vanilla particle library: `environment.animals.flies`, `butterfly` and
  `lucioles`, the six `environment.waterfall_gen.*` falls and crashes, the three
  `environment.river.*_remous`, `environment.background.campfire_high_smoke` and
  `dark_smoke_little`, and `fires.background_fires.car_hull_fire`. The other five (sand storm, dark
  smoke, black object smoke, sticky fire smoke) are vanilla template systems.
- **Plants**, `Custom.RaZoR_Object.Natural.*` (27): cactus, tamarix, aloes, stripe leaves, lianas,
  acacia and ficus trees, bachia, araceae, jungle roots and dead jungle, a ground detail and
  savannah grass.
- **Vehicle variants**: `vehicle.Land.Datsun.Color1`..`4`, `vehicle.Land.Buggy.Color1`..`4`,
  `vehicle.Sea.SwampBoat.Color1`..`4` and their `.Unarmed` twins, `RaZoR_Vehicles.Quad1.Color1`..`4`.
  Each sets `CVehicle/selVehicleColor` to a fixed paint. `vehicle.Land.Datsun.SpeedX5` and
  `SpeedX10` raise `fEnginePower` from 280 to 466 and 955 and `fGearBoxTopSpeed` from 90 to 150 and
  350. `Datsun.Epic` drops the engine to 50 and multiplies the torque roll, pitch and yaw factors
  (to 6, 6 and 16). `Datsun.Pro` has less power (180), lower chassis inertia and different
  steering.
- **Static props and buildings**: `CarWreck1`/`2`, `CarBurned1`, `TruckWreck1`/`2`, `MagicBus`,
  `AlmightyTroopTransportTruck` (the intro truck's mesh), `CeaseFireSign`, `TownFence1`..`4`,
  `CollisionBlock2x2Meters` (declared twice) and `CollisionBlock10x10Meters`, the weapon shop and
  storage interiors and exteriors, the urban safehouse parts, and `WeaponshopCombined` /
  `UrbanSafehouseCombined`, which carry two and four `CGraphicComponent` + `CStaticPhysComponent`
  pairs so a whole building is placed as one object.
- **Physics objects without network sync**: `NoSync.RaZoR_Object.Bottle1`..`4` and
  `Custom.RaZoR_Object.Barrel_NoPhysSync`.
- **Sound points**: `SoundPoint1` (sound point `00456C0D`) and `FliesSound`.
- **Gameplay and script archetypes**:
  - `Custom.MissionScripts.JanneMapScript`, `TestScript`, `SampleScript`: a `CDominoComponent` with
    `hidStartOnLoad` naming `DLCDomino\Community\RaZoR\JanneMap.lua`, `FrankBildersTest.lua` and
    `DominoSample.lua` (see [`scripts-community-samples`](scripts-community-samples.md)).
  - `Custom.SpamPatrol`: a `CGhostEntity` (a ghost patrol) whose `Ghost/archVehicle` is
    `vehicle.Land.Rover.M2_Mounted`, with three `enemy_archetypes.Blue_Faction.Assault_Caucasian`
    passengers, `fSpeed` 8 and no path (`entPathToFollow` -1).
  - `CustChars.RaZoR.NetworkFrankBildersTest`: an AI pawn on Frank Bilders' mesh with the buddy
    brain `::BuddyWorkspace/CBrainBuddyBase`, inventory pack `buddy_shotgun`, and
    `CPawnNetworkComponent`/`CNetworkComponent`.
  - `Custom.RaZoR_Object.SpawnableMissionProximitytrigger`: a proximity trigger with a
    `CMissionComponent` bound to `missions\storymissions\a1sm03\a1sm03_churchattack`.
  - `Custom.RaZoR_Object.BurnEmitter`: a `CStimsEmitterComponent` (stim type 12, level 25, radius
    6 m).
  - `Custom.RaZoR_Object.FakeCapturePointA` (a `CCapturePoint` on a beer bottle mesh),
    `custom.RaZoR_Object.FakeDestroyableMPAmmoPile` (with a `CRandomShooterComponent`),
    `Custom.RaZoR_Object.TestSplinePrimitive` and `Custom.Spline_1` (road splines as entities).
  - Weapons `weapons.Primary.AK47Mod` (with `WeaponProperties.Primary.AK47Mod` and
    `pickups.Weapons.AK47Mod`) and `weapons.Primary.AR15Mod`.

56 palette entries name archetypes from it, in [`editor-palette-vehicles`](editor-palette-vehicles.md),
[`editor-palette-utilities`](editor-palette-utilities.md),
[`editor-palette-natural`](editor-palette-natural.md),
[`editor-palette-effects`](editor-palette-effects.md) and
[`editor-palette-extras`](editor-palette-extras.md). The rest are reachable only from a map file or
a script.

## Depends on

Something has to load the library. The game loads a DLC's library because the DLC names it: `dlc1`
ships a loose `Data_Win32\downloadcontent\dlc1\toc.rml` whose game modes list
`<EntityLib path="downloadcontent/dlc1/generated/entitylibrary.fcb">` and a matching
`<ParticleLib>`. The two paths here have that shape for a package called `dlc2`. This archive ships
only `patch.dat`/`patch.fat`, with no `dlc2` folder or `toc.rml`. The mod's own palette strings
expect one: `Janne252_DLC2_folder` reads "DLC2 required Ingame!".

## Uncertain

- With no `dlc2` registration installed, nothing found here loads either file, and the palette
  entries naming these archetypes spawn nothing. Janne252's mod, which came with its own installer,
  presumably adds the registration. That has not been seen. That the files load through a DLC
  `toc.rml` is inferred from the path shapes, not traced.
- `dlc1`'s `toc.rml` lists its library per game mode (`FCXSingle` and the four multiplayer modes)
  and names none for `FCXEditor`. A `dlc2` table would have to list `FCXEditor` for the editor to
  load this library.
- How the 20 RaZoR particle archetypes resolve their systems depends on which particle library is
  loaded. Here both the dlc2 particle library (world 1's) and the template world's replaced one
  ([`editor-particles`](editor-particles.md), world 2's) carry them.
- What `Custom.SpamPatrol` does with no path, and whether the networked Frank Bilders pawn works
  outside the editor's play-test, is not checked.
