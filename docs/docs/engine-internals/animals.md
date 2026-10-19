---
sidebar_position: 26
---

# Animals

:::info[Verified via reverse engineering]
Code traced in `FarCry2_server`; addresses are the server's unless marked GOG, which are GOG
`Dunia.dll` functions matched against the server and Steam builds. Data read from world 1's
`entitylibrary_full.fcb`, `world1.managers`, the animal brains, `movemgr.bin`, and every world 1 and 2
`worldsector*.data.fcb` decoded with `jackall-cli fcb decode`. Claims marked *(inferred)* were not read in code or data. Nothing
here was seen in game.
:::

Far Cry 2's animals are grazers that notice you, panic and run. No animal can attack: there is no
attack, charge or melee code for them, not even unused. This page describes what there is, as the
ground for [porting attacking animals from Far Cry 3](../design/attacking-animals.md).

## The roster

World 1's entity library has eight quadrupeds, the same eight as scripted variants, and four flyers:

| Group | Archetypes |
|---|---|
| `Animals.Quadrupeds.*` | `CapeBuffalo`, `Chicken`, `ChickenA`, `ChickenB`, `Gazelle`, `Goat`, `Wildebeest`, `Zebra` |
| `Animals.Scripted.*` | the same eight, on `::ScriptedAnimalBrain/AnimalBrain` and `ScriptedAnimal.gosm.xml` |
| `Animals.Winged.*` | `Bat`, `Bird01`, `Bird02`, `Birds` |

Art exists for bats, birds, cape buffalo, chicken, gazelle, goat, wildebeest and zebra. Zebra,
wildebeest, cape buffalo and goat each have a 30-bone `hips_ref` rig with 16 clips, though zebra and
buffalo order the same bone names differently; the gazelle's 29 bones have other names. No animal has
an attack clip.

### A quadruped's components

| Component | What it holds |
|---|---|
| `CAnimal` | GOSM `animalQuadruped.gosm.xml`, `selAnimalSpecies` |
| `CAnimationComponent` | the animation side |
| `CCharacterPhysComponent` | the ragdoll `.hkx`, mass (500 for the buffalo), `SwimDimensions` |
| `CAnimalBeautifierSelector` | |
| `CFCXCountersComponentAnimal` | damage, on `tables.StimEffectTables.NPCDefault` with hit locations off; no health value of its own |
| `CCreatureSoundAndFXComponent` | sound and effects |
| `CFCXAIComponent` | the `CAnimalAgent`: brain `::AnimalBrain/CBrainAnimal` in `animalbrain.ai.rml`, a `CAnimalPersonality`, walk/jog/run speeds (buffalo 0.675 / 5.5 / 5.9), `fThreatAwarenessDistance`, and `Stim_AnimalPanic` (type Panic, radius 20) |

### Two species values

An animal carries two species values with different numberings, and they can be set independently.

**`CAnimal.selAnimalSpecies`** (`+0xD4`) picks animations: Gazelle 0, Zebra 1, Wildebeest 2,
CapeBuffalo 3, Goat 4, Chicken 5, Crocodile 6. Its only readers are
`CAnimalBeautifierGazelle::UpdateAnimalMoveState` (`0x08E93650`) and `GenericQuadruped::Activate`
(`0x08E96640`). Both call `CAnimationComponent::SetEnum(0x1B, value)`, and `SetEnum` (`0x0946CC80`)
stores the value unchecked into the MOVE channel table. Nothing in code is indexed by it, so values of
7 and up are safe in code: they match no MOVE branch until MOVE data adds states that test for them.

`movemgr.bin` has 46 criteria on the `AnimalSpecies` channel (index 27): EnumEqual on Gazelle 8 times,
Zebra 7, Chicken 7, CapeBuffalo, Goat and Wildebeest 6 each, plus 6 NotEqual. Nothing tests 6, so
Crocodile has no animations. A new animal that reuses a value plays that species' clips by path, which
works only on a rig whose bones match.

**`CAnimalAgent.selAnimalSpecies`** (`+0x1E8`) drives behaviour: Unset 0, Gazelle 1, Zebra 2,
Cape Buffalo 3, Wildebeest 4, Goat 5, Chicken 6.

- `UpdateVision` (`0x086FF63E`) and `ComputeRunAwayDestination` (`0x083D4116`) test only for 5 and 6,
  the small animals: sight 3 or 5 m, flee 7 or 4 m. Every other value, 7 and up included, gets the large
  animals' 25/35 m sight and 60/20 m flee.
- `CanFindSTP` calls `IsCompatible` (`0x08558540`), which takes a smart-terrain point only if its
  affiliation is 5 and its `SupportedAnimalSpecies` list holds the animal's value.
  `CTaskSmartTerrainFinder` copies the value into its query.

### What is archetype data

Sounds (`sndAlertSound`, `sndDeathSound` and their types), speeds and turning, `fPathfindRadius`,
`Stim_AnimalPanic`, the brain, `fileStateMachine`, mass, ragdoll and the beautifier all come from the
archetype. The beautifier selector takes 0 Gazelle or 1 Generic; a higher value creates no beautifier.
All retail quadrupeds, the gazelle included, use 1 (`GenericQuadruped`).

Hard-coded: the goat and chicken case above, and the 150 m herd panic.

## Where animals come from

Every live animal is placed one by one in `worldsector*.data.fcb`. No animal appears in any `landmark*`
file or in `managers`, `omnis` or `mapsdata`. Instances, and the number of sectors they sit in:

| Archetype | World 1 | World 2 |
|---|---|---|
| `Quadrupeds.Gazelle` | 70 in 32 | 33 in 10 |
| `Quadrupeds.Zebra` | 40 in 21 | 15 in 8 |
| `Quadrupeds.Wildebeest` | 25 in 11 | 28 in 8 |
| `Quadrupeds.CapeBuffalo` | 6 in 2 | 8 in 4 |
| `Quadrupeds.Chicken`, `ChickenA`, `ChickenB` | 8, 4, 4 | 9, 6, 6 |
| `Quadrupeds.Goat` | 3 in 2 | 9 in 7 |
| `Scripted.*` | 12, on the taxi-ride layer | 0 |
| `Winged.Birds` | 46 | 78 |

- **An instance holds** `tplCreatureType`, `hidName` (the archetype name plus a number), `disEntityId`,
  `hidPos` and `hidPos_precise`, sometimes `hidAngles`, and baked lighting in `CGraphicComponent`. Some
  also override `CCharacterPhysComponent.fMaxSlope` or carry an empty `CEventComponent`. Some birds
  override `CBoidsComponent` fields.
- **Nothing groups or randomizes them.** There is no herd id, count or random field, and no
  `BindingHierarchy`. A herd is instances placed near each other.
- **Layers:** 273 animals in world 1 and 495 in world 2 sit in `main`. The `benchmark` layer, off by
  default, holds 13 that never appear in the campaign. `_disableformission` layers remove animals during
  missions, such as 13 wildebeest in `w2b4_a17_enemies`. A few sit in `a2sm05_ai` and the taxi ride.
- **Scripts move, never spawn.** `AnimalFollowPath.lua` drives animals that are already placed; no
  retail script spawns an animal.

### Smart-terrain points

`CSmartTerrainManager` names the defaults in `archDefaultSavannahAnimalSTP` and
`archDefaultUrbanAnimalSTP`: `STP_archetypes.Animal.SavannahAnimals` and `UrbanAnimals`. They are
multi-user `CSmartTerrain` points, run from `smartterrainanimal.ai.rml`, and spawn nothing.

| Point | Placed (w1 / w2) | Species (agent values) | Actions |
|---|---|---|---|
| `SavannahAnimals` | 83 / 263 | 1, 2, 3, 4 | `Quadruped_Eat`, `_Generic_Movement`, `_HeadTwitch`, `Pawn_ST_GazelLieDown` |
| `UrbanAnimals` | 18 / 56 | 5, 6 | the same, with `Pawn_ST_ChickenSit` |

`fBroadcastRange` is 50 and `fExecutionRange` 20; placed points often override the broadcast range, up
to 160. `GenericMultiUser*_TODELETE` points are soldier duty and rest points, not animals.

### Saves and respawning

Every quadruped archetype has `CPersistComponent.selLevel` 1 (Limited), like soldiers. A level-1 world
entity is not saved, and dead ones come back once their sector's `main` unloads
(`ForgetDeletedInSector`, see [AI](./ai.md)). So killed animals return after a sector reload, and
animals are not in saves *(inferred from the shared level; the savegame component census has no
`CAnimal`)*.

### Nothing picks at random

No data or code chooses an animal from a list:

- `bRadomize` re-rolls kit outfit parts only.
- `CPickupContainer.ContainedPickups` (`nProbability`, `nInstanceLimit`) is on 36 multiplayer weapon
  crates.
- `CEntitySpawner` (srv `0x08B82B20`, 15 placed) spawns one `archArchetypeToSpawn` once at its own
  position, at persist level 3 with `bAlreadySpawned`. All 15 spawn weapon pickups.
- `CReinforcementPoint` (277 placed) carries archetype lists (`MercNames`, `VehicleNames`), but for
  mercs and vehicles. `CFCXGameplayManager::GetReinforcementArchetype` (`0x08798840`) is a fixed remap
  table.
- An RTTI and symbol search for Ecosystem, Wildlife, Herd, Spawner, Population, AnimalSpawn, SpawnGroup
  and RandomArchetype finds nothing else but `CGhostManager` ghost groups and `CBoidsComponent`.

The only randomness on animals: `CAnimal::Finalize` (`0x08B2F290`) sets the graphic component's
`+0x1BC` to a random 0.9 to 1.1, probably a size jitter *(inferred)*, and fleeing picks among random
points.

## How a placed animal is built

`CEntitySystem::SpawnEntityFromNode` (srv `0x09453010`, GOG `0x104DC190`, `__thiscall`, `RET 8`) reads
the instance's `tplCreatureType` as a string and calls the entity library manager's vtable slot 0,
`GetArchetypeDescription(const std::string&)` (srv `0x0944C0B0`, GOG `0x1053AEB0`, `__thiscall`,
`RET 4`). The manager singleton is at GOG `[0x11595228]`, its vtable at `0x10DEACD4`. The name is hashed
case-insensitively to a `CStringID` and looked up in a hash map that returns the archetype node. The
spawn takes that node's `Entity` child, merges the instance over it, and creates the entity; the
merge asks the instance first, so any field the instance carries wins.

The same slot 0 also serves runtime spawns (`SpawnGenericEntityFromArchetype`, srv `0x094534C0`) and
lookups such as weapon properties.

`CPersistenceDB::RespawnEntities` (srv `0x0967A3C0`) re-runs `SpawnEntityFromNode` on saved
descriptions, then `ChangeGenericEntityID`. A saved description includes `tplCreatureType`.

### Spawning at runtime

The Lua `SpawnEntityFromArchetype` (GOG `0x10582A50`, bound in `RegisterEngineHelpers` `0x10583A54`):

1. gets `CMissionComponent::DefaultLayer` (`0x105732A0`);
2. calls `CEntitySystem::SpawnEntityFromArchetype` (GOG `0x104DD270`, `__thiscall` with ECX =
   `[0x11594270]`, arguments `EntityHandle* out, const std::string* name, const CPathID* layer`,
   `RET 0xC`);
3. sets the position (`0x104D2A40`) and the angles in radians (`0x104D2A50`).

A plugin can make the same call from C++ with an MSVC `std::string` (text at `+4`, inline while the
capacity at `+0x18` is under `0x10`).

- **Finalizing waits on resources:** `CEntity::OnIsLoaded` is the only caller of `CWorld::ToFinalize`.
- **Animals need a navmesh,** since fleeing and pathfinding snap to it.
- **Nothing ties a runtime entity to a sector's layer,** so sector unload probably does not remove it
  *(inferred)*. Only the Lua `RemoveEntity` was confirmed as a way to remove one.
- **Saving:** `CPersistenceDB::SaveEntity` (srv `0x09680380`) saves only entities with a
  `CPersistComponent` (level at `+0x10`) or a `CGhostComponent`, filed by the sector of their position,
  and respawns them when that sector loads. Whether a runtime animal is saved is therefore up to its
  archetype.

None of this was run; [experiment 2](./data-to-live-object.md#2-spawn-an-animal-a-soldier-and-a-typo-at-runtime)
on the data-to-live-object page, which spawns a cape buffalo from the console, is the first check.

## The animal agent

`CAnimalAgent` derives from `CGameAgent`, which derives from `CAgent`. It is not a `CPawnAgent`, so it
has no pawn or weapon layer.

- **Sight** is `CAnimalSensorySystem::UpdateVision` (`0x086FF620`): a distance check against every
  `CPawnAgent`, with no view cone and no line-of-sight test. The radius is 25 m, or 35 m once
  threatened; goat and chicken use 3 and 5 m. The target must be within 10 m of height.
- **Hearing and hits.** `CAnimalAgent::OnEvent` (`0x0855B140`) makes the animal threatened on a stim of
  type Bang, Snap, Pierce, Cut or Burn.
- **The herd** is one rule: a Panic stim from another `CAnimalAgent` within 150 m also makes it
  threatened. There is no pack or leader code.
- **Fleeing.** `CBrainAnimal::ComputeRunAwayDestination` (`0x083D3EA0`) tries 16 points 60 m away from
  the threat, then 20 m, snaps each to the navmesh and writes `AnimalRunAwayDestinationFact`.

The animal brains use Idle and Alert sub-brains, the smart-terrain finder and executor,
`CTaskPathFind`, `CTaskAnimalPathFollow`, `CTaskPlaySound` and `CTaskSendActionSignal`.

## What a brain could reuse

Soldier tasks are not tied to soldiers. `CTaskChase::RequestNewPath` uses only generic agent virtuals
and a `CSmartPosition`. The `PlayerPosition` RefType (6) appears 322 times in `mercbrain`. So an
animal brain built from `CScannerIsInDistance`, `CTaskPathFind` or `CTaskChase` aimed at
`PlayerPosition`, then a fast `CTaskAnimalPathFollow`, should chase the player *(inferred: the
mechanism was read, an animal running it was not)*.

## How anything damages the player

Soldiers melee by switching to the machete (`FireStrategy/Machete` in `mercbrain`). The swing is
weapon code, `CWeaponFireMeleeStrategy`, which sends bullet-hit stims and needs the pawn and weapon
layers that animals lack.

`CEntityEventStims::DamageHealth` (`0x09446F50`) treats stims of type 3 Pierce, 4 Crush, 5 Cut and 7
Burn as damage. The player's handler is `CFCXCountersComponentPlayer::HandleDamageStim`
(`0x087FDF20`), which also has `NotifyAttackerHUD`.

Two routes need no code *(the mechanism was read; the result was not seen)*:

- **A brain task.** `CTaskBroadcastStims` with type 4 or 5, a level, a radius of about 2.5 m and
  `broadcastToAllAI=1`. Its `DoRun` (`0x082F8970`) queues the stim to every `CPawnAgent` entity in the
  radius, and `CFcxAI::PostUpdate` delivers it to `CEntity::OnEvent`. The class is in retail
  `Dunia.dll`; the shipped brains use it only for "vroom" sound stims.
- **A script.** The Lua global `SendPierceStimWithOrigin(playerId, animalId, level)` (`0x086DC880`)
  with `GetLocalPlayerId`, from a Domino graph.

Turning flee into chase in one place (a hook on `ComputeRunAwayDestination` writing the threat's
position instead), hitting on an animation event, knockback, a camera hit and per-species sight all
need an FCSE plugin.

## Water

Swimming is pawn logic: `CPawn::SPawnImpl::UpdateSwimming` and `CPawn::SetCanSwim`. The physics swim
state `CHkPhysCharacterStateSwimming` sits on the character controller, which animals also use
*(inferred)*. Animals path only on the navmesh. Whether the navmesh covers water was not checked.

## Flocks

`CBoidsComponent` is a real flocking system, used by `Animals.Winged.Birds`: zone radius and height,
wanderers, subgroups of model, skeleton and clip, and separation, alignment and cohesion weights.

## Animation

GOSM `CGOStateAnim` states name MOVE states: `Quadruped_Generic_Movement`, `_LookAt`, `_Eat`,
`_HeadTwitch` and `_Die`. In `movemgr.bin` these branch on the `AnimalSpecies` channel: 46 criteria,
six species and about 130 animal clips. The brain drives the GOSM with action signals such as
`start_lookat`.

An attack therefore needs four pieces of data: a clip, a MOVE state with a branch per species (see
[MOVE](../file-formats/move.md)), a GOSM state with a sink signal, and a `CTaskSendActionSignal` in the
brain. A new species also needs MOVE branches. Whether `CAnimal` species values above 6 are safe is
untested.

Animal sounds play from animation events that point at `.spk` banks. Those event bytes are not
decoded, so a new clip would copy them from an existing one.
