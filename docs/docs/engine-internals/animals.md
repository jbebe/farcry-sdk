---
sidebar_position: 26
---

# Animals

:::info[Verified via reverse engineering]
Code traced in `FarCry2_server`; addresses below are the server's, and retail `Dunia.dll` addresses
were not located. Data read from world 1's `entitylibrary_full.fcb`, `world1.managers`, the three
animal brains and `movemgr.bin`. Claims marked *(inferred)* were not read in code or data. Nothing
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

### Two species enums

- **`CAnimal`**, the value MOVE branches on: Gazelle, Zebra, Wildebeest, CapeBuffalo, Goat, Chicken,
  **Crocodile**. Crocodile has no MOVE branch and no assets.
- **`CAnimalAgent`**: Unset, Gazelle, Zebra, Cape, Wildebeest, Goat, Chicken.

### Where animals come from

There is no herd or ecosystem spawner: `world1.managers` holds none. `CSmartTerrainManager` names
`DefaultSTP` as `STP_archetypes.Animal.SavannahAnimals` and `UrbanAnimals`. These are multi-user
`CSmartTerrain` points (broadcast radius 50 m) with Eat, HeadTwitch and GazelLieDown actions, run from
`smartterrainanimal.ai.rml`. `GenericMultiUser*_TODELETE` points are soldier duty and rest points, not
animals.

Each animal is probably placed one by one in sector data *(inferred: sectors were not read)*.

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
