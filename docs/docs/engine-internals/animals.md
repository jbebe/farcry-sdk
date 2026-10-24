---
sidebar_position: 26
---

# Animals

:::info[Verified via reverse engineering]
Code traced in `FarCry2_server`; addresses are the server's unless marked GOG, which are GOG
`Dunia.dll` functions matched against the server and Steam builds. Data read from world 1's
`entitylibrary_full.fcb`, `world1.managers`, the animal brains, `movemgr.bin`, and every world 1 and 2
`worldsector*.data.fcb` decoded with `jackall-cli fcb decode`, plus world 1's `_depload.xml` and the
animal `.skeleton`, `.mab` and `.sdat` files read directly. Claims marked *(inferred)* were not read in code or data. Nothing
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
wildebeest, cape buffalo and goat each have a 30-bone `hips_ref` rig with 16 clips, the same bone
names in four different orders ([the rigs](#the-rigs)); the gazelle's 29 bones have other names. No
animal has an attack, rear, kick or jump clip on these rigs.

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

Nothing copies one value into the other. `CAnimal +0xD4` is written only by its constructors (0) and
reflection, from `CAnimal`'s own field, and `CAnimalAgent +0x1E8` likewise from the agent's; the
agent's copy constructor (`0x0855EEC0`) copies only its own fields.

### What is archetype data

Sounds (`sndAlertSound`, `sndDeathSound` and their types), speeds and turning, `fPathfindRadius`,
`Stim_AnimalPanic`, the brain, `fileStateMachine`, mass, ragdoll and the beautifier all come from the
archetype. The beautifier selector takes 0 Gazelle or 1 Generic; a higher value creates no beautifier.
All retail quadrupeds, the gazelle included, use 1 (`GenericQuadruped`).

Hard-coded: the goat and chicken case above, and the 150 m reach of the herd panic. The panic's
delivery radius is archetype data ([the herd panic](#the-herd-panic)).

Archetype `disEntityId`s are one numbering across both worlds' libraries: 1 to 5,735, with no
duplicates (1,419 archetypes in world 1, 1,469 in world 2). The quadrupeds are 1 to 8.

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

- **An instance's z is the ground.** A retail zebra at (1,381.62, 3,193.99) sits at z 34.8998; its
  sector's `.sdat` height grid, read at row `y mod 64` and column `x mod 64` and interpolated, gives
  34.900. Its file is `worldsector3941`, and `3941 = ⌊y / 64⌋ × 80 + ⌊x / 64⌋`.
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

Each action's `mmsAction` names a MOVE state, which branches on the animation species. Two of them
send other species elsewhere, so every action plays on every rig:

- **`Pawn_ST_GazelLieDown`** lies down only a gazelle; any other species is redirected to
  `Quadruped_Eat`.
- **`Pawn_ST_ChickenSit`** sits only a chicken; any other species is redirected to `Quadruped_Eat`.
- **`Quadruped_Eat`** has an eat branch for all six species, the chicken's being its pecking clip.
- **`Quadruped_HeadTwitch`** has one per species; the chicken plays its look-around.

MOVE states are keyed by the CRC-32 of the lowercased name (`quadruped_eat` is `0x8953EA81`).

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
`+0x1BC` to a random 0.9 to 1.1, a uniform size factor ([the size factor](#the-size-factor)), and
fleeing picks among random points.

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
  threatened; goat and chicken use 3 and 5 m. The target must be within 10 m of height. The player is
  a `CPawnAgent` (`PawnPlayer`'s AI type, seen in data), so seeing the player within 25 m is enough to
  threaten an animal. The nearest pawn becomes the threat; its stored position is replaced only when
  the new one is more than 10 m away. `SetThreatened` (`0x086FF1C0`) also sets a 2 s timer, and once no
  pawn is in range the animal calms when it runs out.
- **Hearing and hits.** `CAnimalAgent::OnEvent` (`0x0855B140`) makes the animal threatened on a stim of
  type Bang, Snap, Pierce, Cut or Burn. A stim whose sender is the animal itself is ignored, whatever
  its type.
- **The herd** is the panic relay [below](#the-herd-panic). There is no pack or leader code.
- **Fleeing.** `CBrainAnimal::ComputeRunAwayDestination` (`0x083D3EA0`) tries 16 points 60 m away from
  the threat, then 20 m, snaps each to the navmesh and writes `AnimalRunAwayDestinationFact`.
- **Alert and death sounds.** A brain's `CTaskPlaySound` with `AlertSound` or `DeathSound` reaches
  `CAnimalAgent::PlaySoundFromTask` (`0x0855CE30`), which stops the entity's sound and plays
  `sndAlertSound` or `sndDeathSound`, with its sound type, on the entity.

The animal brains use Idle and Alert sub-brains, the smart-terrain finder and executor,
`CTaskPathFind`, `CTaskAnimalPathFollow`, `CTaskPlaySound` and `CTaskSendActionSignal`. Retail's
`scriptedanimalbrain` also runs `CScannerIsInDistance` on an animal, against a position fact.

### The herd panic

`CAnimalAgent::CoreRun` (`0x0855D770`) sends the panic. While the animal is threatened, and a field at
`+0x214` is zero (not identified), it sends its own `Stim_AnimalPanic` (`+0x330`) every 0.25 s to every
`CAnimalAgent` within that stim's `fRadius`, stamped with its position. `fRadius` is archetype data, 20
on every retail quadruped.

`OnEvent` acts on a Panic stim (type 9) only when the receiver is not threatened yet and the sender is
a `CAnimalAgent`. It takes the threat position, and if that lies within 150 m of the receiver, the
receiver turns threatened with it and starts relaying in turn. Nothing checks species: a zebra's panic
alarms a buffalo, and the other way round.

### A brain cannot alarm its own animal

`CTaskBroadcastStims::DoRun` (`0x082F8970`) has two modes. With `broadcastToAllAI` set it queues the
stim to every `CPawnAgent` in the radius, so no animal receives it. Without it the stim goes to the
agent's own entity through `CFcxAI::DelayedSendEvent`, and `OnEvent` ignores a stim from itself. So no
task can make the brain's own animal threatened, and the sight radius cannot be widened from a brain.
A brain can still poll the player's distance with `CScannerIsInDistance` on `<PlayerPosition>` and act
on it.

## What a brain could reuse

Soldier tasks are not tied to soldiers. `CTaskChase::RequestNewPath` uses only generic agent virtuals
and a `CSmartPosition`. The `PlayerPosition` RefType (6) appears 322 times in `mercbrain`. So an
animal brain built from `CScannerIsInDistance`, `CTaskPathFind` or `CTaskChase` aimed at
`PlayerPosition`, then a fast `CTaskAnimalPathFollow`, should chase the player *(inferred: the
mechanism was read, an animal running it was not)*.

**`CTaskCheckVisibleByPlayer` runs on an animal.** It is a `CPawnDecision`, but its `DoRun`
(`0x084277A0`) uses only the generic agent `CTask::SetAgent` stores at `+0xC`; the pawn pointer
`CPawnDecision::SetAgent` (`0x082F34F0`) leaves null for a non-pawn sits at `+0x34` and is not read. It
looks up the agent entity's `COcclusionQueryComponent`, takes the largest `GetCoverageOnViewport`
over its viewports, and fires the event `Yes` (CRC-32 `0x4DAA7349`) when that is above 0, else `No`
(`0xF22E0CBD`); an entity without the component always gets `Yes`. Every retail quadruped, scripted
quadruped and flyer archetype carries `COcclusionQueryComponent` already (19 of world 1's 115). The
task has no parameters. That coverage means pixels the player's camera actually draws, so that an
animal behind a wall or a rock reads as unseen, is *(inferred from the names)*; whether grass hides
it was not read. No retail brain uses the task.

**An animal no smart-terrain point accepts stands still.** The idle sub-brain's
`CTaskSmartTerrainFinder` has a `NoSTPFound` exit that retail's `animalbrain` connects to nothing, so
an agent species no point lists, 7 and up for instance, idles in place and never grazes *(read in the
brain; the standing was not seen)*.

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
  with `GetLocalPlayerId`, from a Domino graph. It delivers one bullet hit straight to the target.
- **A stim emitter.** `CStimsEmitterComponent` sends its stims to everything within their radius on a
  fixed period, with no brain or script; retail campfires burn with it. It suits an animal that does
  not move. See [stims and damage](./stims-and-damage.md#stim-emitters).

Turning flee into chase in one place (a hook on `ComputeRunAwayDestination` writing the threat's
position instead), hitting on an animation event, knockback, a camera hit and per-species sight all
need an FCSE plugin.

## Water

Swimming is pawn logic: `CPawn::SPawnImpl::UpdateSwimming` and `CPawn::SetCanSwim`. The physics swim
state `CHkPhysCharacterStateSwimming` sits on the character controller, which animals also use
*(inferred)*. Animals path only on the navmesh. Whether the navmesh covers water was not checked.

## Flocks

`CBoidsComponent` is a real flocking system, used by `Animals.Winged.Birds`: zone radius and height,
wanderers, subgroups of model, skeleton and clip, and separation, alignment and cohesion weights. It
has no AI, physics or collision, takes no events, and climbs out of sight at night. See
[Flocks](./flocks.md).

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
decoded, so a new clip would copy them from an existing one. Depload lists each clip's banks as its
children: the goat's idle, eat and walk clips fire the goat bleats `00456b1f` and `00456b20`, its run
clips `00456acb`, which the buffalo's and zebra's gallops share, and its walk clips `00456ab8`.
`CAnimationResource::ClientProcessRawData` (`0x09B9E8D0`) creates each event object from the clip's
event chain and loads it with the clip as its parent.

`TriggerGameEvents` (`0x09B9EAB0`) hands each event whose time falls in the frame to
`CAnimationComponent::DispatchEvent` (`0x09470B60`). A sound event is queued and played there; any
other event goes to the animal's own entity through `CEntity::OnEvent`. So a clip event cannot reach
the player, and a stim from it would be ignored, since the agent drops stims it sent itself.

### Clip membership

A `CMovementResource` is named after a MOVE state, and depload lists the state's clips under it. Its
`ClientProcessRawData` (`0x09BAD7F0`) does nothing but call `GenerateChildren` (`0x09BAD220`), which
walks the state in the loaded MOVE graph and adds every clip and pose it can play as a child. So the
child list follows the MOVE graph, unlike a weapon's animation package. That a clip only an edited
`movemgr.bin` names then plays with no depload entry is *(inferred)*.

### The rigs

Zebra, wildebeest, cape buffalo and goat each have a 30-bone `hips_ref.skeleton` with the same names,
in four different orders. Clips address bones by id ([`.mab`](../file-formats/mab.md)), so a clip
plays only on the rig it was made for:

| Rig | Ids 5–9 (head) | Ids 10–19 (forelegs) | Ids 20–29 |
|---|---|---|---|
| zebra | `Jaw`, ears, eyes | left, then right | left hind leg, right hind leg, `Tail`, `Tail1` |
| cape buffalo | left ear, left eye, right eye, right ear, `Jaw` | left, then right | left hind leg, `Tail`, `Tail1`, right hind leg |
| wildebeest | right ear, left ear, `Jaw`, right eye, left eye | right, then left | `Tail`, `Tail1`, left hind leg, right hind leg |
| goat | `Jaw`, right ear, left ear, left eye, right eye | right, then left | right hind leg, left hind leg, `Tail`, `Tail1` |

Ids 0 to 4 are `Hips`, `Spine`, `Neck`, `Neck1` and `Head` on all four. Mapping a clip to another rig by
bone name is not enough either: the rigs' bone lengths differ, so the zebra's gallop on the
wildebeest's lengths lifts the hind hooves 0.12 m above the fore hooves *(computed by posing the clip
on the wildebeest's offsets)*.

`Hips` is the only bone with `m_fAnimatedTranslation` on the goat and zebra rigs, and the only bone
translated in every clip read: all 16 of the goat's, and the walk and run cycles of the other three.
The chicken's 11-bone `pelvis_ref` rig is the same with `Pelvis`: `Pelvis`, `Neck`, `Head`, `R Wing`,
`L Wing`, and `Thigh`, `Calf`, `Foot` per side, and all seven of its clips translate `Pelvis` alone.
Every other bone gets rotations only, so bone lengths come from the `.skeleton` and hip height from the
clip:

| Animal | Hip height in the clips read | Run clip covers | `fSpeedsRun` |
|---|---|---|---|
| goat | 0.67–0.78 m | 4.55 m/s | 4.55 |
| zebra | 1.33–1.43 m | 12.28 m/s (gallop) | 12.28 |
| cape buffalo | 1.30–1.48 m | 5.90 m/s | 5.9 |
| wildebeest | 1.08–1.29 m | 8.35 m/s | 7.9 |
| chicken | 0.285 m standing; 0.165 sitting, 0.544 at the top of its run-jump | 3.75 m/s | 3.75 |

Walk speeds match the same way: the goat's walk clip covers 0.86 m/s, its `fSpeedsWalk` is 0.86; the
chicken's 0.30 m/s, its `fSpeedsWalk` 0.3.

**A clip's translation replaces the rig's offset; it is not added to it.** `EvaluateSingleAnimNode`
(`0x09BBA350`) writes a translated bone's value into its result slot with a "has translation" flag,
and the pose builder `GenerateTransformForJointFromParams` (`0x09BB30B0`) takes that value when the
flag is set and the `.skeleton`'s `m_LocalOffset` when it is not. Every animal rig rests its root at
offset 0, so the hip height cannot be lifted from the `.skeleton`. That builder is the two-bone IK
path's, and the per-frame `CAnimModel::GenerateModelTransforms` (`0x09B49480`) does the same.
Posed that way, the chicken's walk keeps each planted foot at a steady 0.027 m.

### Movement comes from the clip

`CAnimalBeautifierGenericQuadruped` reads no bones and no per-animal data. Each update,
`UpdateAnimalMoveState` (`0x08E95E10`) writes three MOVE channels: the agent's speed to float channel
2, the turn rate in degrees to float channel 28, and the look-at angle to angle channel 11. `Activate`
writes the species to channel 27.

Movement then depends on the animation LOD. When it is 1 or 2 (`UpdatedEveryFrame`, `0x08E96110`),
`UpdateFromAnimation` (`0x08E97D10`) takes the clip's displacement, multiplies it by the graphic
component's `+0x1BC` (the 0.9 to 1.1 size jitter `CAnimal::Finalize` rolls) and gives the animal the
agent's direction at that magnitude. Otherwise `Update` (`0x08E983B0`) moves it by the agent's own
velocity. So near the player an animal runs at its clip's speed, not its archetype's, and `+0x1BC`
scales root motion as it scales the model ([below](#the-size-factor)).

**The displacement is the clip's trajectory, not its `Hips` track.** `UpdateFromAnimation` gets it
through `ComputeDisplacement` (`0x08E96310`) and `CAnimationComponent::GetDisplacement`, and
`EvaluateSingleAnimNode` (`0x09BBA350`) fills it from section 0 of the clip, the root translation.
Every animal clip keeps that trajectory flat at z 0 and holds `Hips` in place forward: the buffalo's
run moves its trajectory 3.74 m in 0.633 s, the 5.90 m/s, while `Hips` stays at y −0.49. A clip
that should carry the animal forward, a leap for instance, has to move its trajectory; moving `Hips`
forward would leave the entity and its capsule behind *(inferred)*. Every animal bank holds one clip
and no participants (all 48 buffalo, zebra and wildebeest banks read).

**With zero agent velocity the animal stands, whatever the clip.** Speed comes from the clip and
direction from the agent, so a clip with a still trajectory, or a brain that stops its path follow,
halts it. A GOSM state can switch this off: `CGOStateEventAnimal::Activate` (`0x08B0D540`) sets
`CAnimal +0x5C` to 1 for `EventType` 0 (`UseRotation`) and to 0 for `EventType` 1, and while it is set
the animal moves by the clip's own displacement and yaw, with no agent velocity. The idle state's
`IgnoreRotation` event clears it again.

The gazelle beautifier (`selBeautifier` 0) also writes bool channel 33, `PerformingJump`, from a flag
`UpdateEffectiveFromDesire` (`0x08E93CF0`) raises when the wanted heading is more than 45° off the
animal's facing and clears once it has nearly stopped: a hop on a sharp turn. No other code writes
that channel, so on the generic beautifier the MOVE branches that test it never play.

### The size factor

`CGraphicComponent +0x1BC` is a uniform model scale. `OnEntityMove` (`0x094ABB30`) builds the render
matrix at `+0x170` as the entity matrix times `diag(s, s, s, 1)` whenever it is not 1, and both
`GetObjectWorldMatrix` (the mesh) and `GetNodeWorldMatrix` (every skeleton node) go through it. So it
scales the mesh, the animated pose including the clip's hip height, and, through the beautifier, root
motion: one scale for the whole animal, with feet that stay planted.

- **Nothing in data sets it.** `CGraphicComponent` has no scale member; the constructors set 1 and
  only `CAnimal::Finalize` writes the jitter (GOG `0x10683280`, the store at `0x10683353`,
  `fstp [edi+0x1BC]` with EDI the graphic component). A plugin can write it after `Finalize` runs.
- **The culling sphere does not grow.** `UpdateTransform` (`0x094AF6A0`) moves the world bounding
  sphere's centre through the scaled matrix but passes its radius unscaled: the mesh's own, or
  `fLODSphereRadius` in the branch that overrides it.
- **Physics does not grow.** The character capsule and `fPathfindRadius` are archetype data and
  read no scale *(inferred)*.
- **Ragdolls draw through it.** In ragdoll mode `UpdateRagdollBonesToMeshNodes` (`0x09516540`) writes
  the ragdoll's pose into the skeleton's node matrices, which are model-space, so a scaled animal's
  corpse is drawn at the scale times the physical ragdoll, about the entity's origin *(that the pose
  is entity-relative is inferred)*.
