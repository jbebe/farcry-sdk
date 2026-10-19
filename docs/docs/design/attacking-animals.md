---
sidebar_position: 3
---

# Attacking Animals

**Status:** feasibility checked 2026-10-19 from data and code; the buffalo charge built in
`mods/wildlife-overhaul` the same day; nothing seen in game. The behaviour cannot be copied from Far
Cry 3 and has to be rebuilt on Far Cry 2's own agent. New animals can be
[added alongside the old ones](#adding-instead-of-replacing) with a data layer and a small plugin.
The first steps are [four experiments](#experiments-in-order).

## The goal

Far Cry 3's wildlife hunts the player. Far Cry 2's only grazes and runs. The plan is to bring Far Cry
3's attacking animals into Far Cry 2, each as an African animal:

| Far Cry 3 source | Far Cry 2 animal |
|---|---|
| cassowary | ostrich (new) |
| Komodo dragon, on land | crocodile (new) |
| crocodile, in water | crocodile (new) |
| tiger | lion (new) |
| buffalo | cape buffalo (the existing model, which has no attack) |
| hyena | hyena (new) |
| snake | snake (new) |
| fish packs | fish packs (new) |

## What the engines give us

The details are in [Animals](../engine-internals/animals.md) and
[Animals in Far Cry 3](../engine-internals/far-cry-3-animals.md). The parts that decide the verdicts:

- **Far Cry 3's behaviour is data over code Far Cry 2 lacks.** Its brains are behaviour trees wired in
  data, but the attack decision, stalking, mauling, packs and fish movement are C++ task classes and
  agents that `Dunia.dll` does not have. Copying the brains or archetypes does nothing.
- **Far Cry 3's tuning carries over as numbers:** health, damage, attack ranges and detection radii per
  animal.
- **Far Cry 2's animal agent only detects, panics and flees.** Sight is a 25 m radius with no view cone.
  There is no attack code, not even unused.
- **A data-only chase and hit look possible.** Soldier chase tasks are not tied to soldiers and can aim
  at the player's position, and `CTaskBroadcastStims` can send a Crush or Cut stim, which damages the
  player, to everything within a radius. Both mechanisms were read in code; neither was run on an
  animal.
- **Far Cry 2 has no pack AI, no water movement for animals and no paired quick-time events.** It does
  have `CBoidsComponent`, the birds' flocking, and an unused Crocodile species value.

## What stays out

- **Mauling.** Far Cry 3's pinned bite sequence (crocodile, tiger, komodo, snake) needs a paired
  player animation system Far Cry 2 does not have. Plain hits take its place.
- **Hunting packs**, unless an FCSE plugin coordinates them. Without one, each hyena hunts alone; the
  only shared behaviour is one animal's panic spreading to others nearby.
- **A crocodile that hunts underwater.** Far Cry 2's animals move only on the navmesh. Far Cry 3's own
  crocodile does not navigate water either: it waits and lunges. A waterline ambusher is realistic.

## The assets

Far Cry 3 has a donor for every animal. Its quadruped rigs use the same bone names as Far Cry 2's
30-bone zebra and buffalo rig, so they retarget by name. JackAll reads none of Far Cry 3's formats
today:

| Format | Far Cry 3 to Far Cry 2 |
|---|---|
| textures, Ogg sound files | easy: a texture is a header plus a plain DDS |
| materials | easy: copy a Far Cry 2 animal's `.xbm` and swap its textures |
| mesh, skeleton | moderate: the same formats, newer versions |
| ragdoll | moderate to hard: Havok 2010 against Far Cry 2's 5.5 |
| animation banks | **hard**: version `0x61` has no decoder |

The second gap is BlenderFC2. It cannot add bones or a new rig, and it writes animation only into an
existing bank, without root motion, tags or events. So any animal that does not fit an existing Far Cry
2 skeleton is blocked. What works today is a new mesh on a copied Far Cry 2 rig, with clips keyed by
hand into copied banks.

Three tools would unblock every animal:

1. a `.mab` `0x61` decoder, to reuse Far Cry 3's animations;
2. a Far Cry 3 mesh and skeleton reader, to convert its models;
3. a rig exporter in BlenderFC2: bones, inverse-bind matrices, palettes.

## Per animal

| Far Cry 2 animal | Far Cry 3 donor | Difficulty | Blockers |
|---|---|---|---|
| cape buffalo | water buffalo, which has attack clips | lowest | an attack clip: keyed by hand now, or retargeted once the decoder exists |
| lion | tiger or leopard | medium | a new mesh on the Far Cry 2 rig works today; animations as above |
| hyena | Far Cry 3's hyena archetype (mesh not found), rabid dog, thylacine | medium | as for the lion |
| crocodile | the crocodile: walk, jog, swim and kill clips | medium-high | no animations, MOVE rules or assets behind the Crocodile species value; waterline ambush only |
| fish packs | river fish (7 bones) or the reef school (125 bones) | medium | `CBoidsComponent` should work under water as data; needs a fish rig |
| snake | the snake (32 bones) | high | a new rig, so the rig exporter |
| ostrich | the cassowary (two-legged, 51 bones) | high | a new rig, and two-legged animation rules Far Cry 2 does not have |

Beyond the data-only charge, behaviour lives in one FCSE plugin: chase instead of flee, hits timed to
an animation event, knockback, a camera hit and per-species sight.

## Tier list from what Far Cry 2 has

Ranked by how much each animal can borrow from Far Cry 2's own rigs, clips and systems. Every attack
rests on the same untested pair, a brain chasing the player and a damage stim, which the buffalo charge
tests for all of them.

What the ranking leans on, read in Far Cry 2's data:

- **The chicken is a small ostrich.** Its rig is 10 bones: `Pelvis`, `Neck`, `Head`, `R Wing`,
  `L Wing`, and `Thigh`, `Calf`, `Foot` on each side. Its clips are walk, run, run-jump, eat,
  look-around, sit-down and get-up, and it has a ragdoll and its own MOVE branch.
- **The bird flock is all data.** `Animals.Winged.Birds`' `CBoidsComponent` holds one model, skeleton
  and clip, a zone (`fZoneRadius` 30, `fZoneHeight` 4), speeds, and separation, alignment and cohesion
  weights. The bird rig is `Pelvis` plus two-segment wings.
- **The buffalo is already provoked the way Far Cry 3's is.** It turns threatened when shot at or hit,
  which is exactly when Far Cry 3's defensive buffalo attacks. Mass 500 and a 0.82 m capsule are set.
- **The two species values are independent** ([Animals](../engine-internals/animals.md#two-species-values)):
  one picks animations, the other sight and flee distances.

**S: data only, nothing new to make**

- **Cape buffalo charge.** The existing provocation, with chase in place of flee: it runs at the player
  on its own run cycle and sends Crush on contact. A head-butt clip is an extra.

**A: a new mesh on a Far Cry 2 rig, no new animation**

- **Fish shoals.** A copy of the bird flock with a fish mesh, placed under the surface. Skin the side
  fins to the wing bones and the flap cycle becomes fin strokes. No AI; Far Cry 3's fish are harmless
  too. Risk: a zone taller than the water is deep may let fish out of it.
- **Hyena.** A new mesh on the goat or zebra rig, on the goat's run cycle. The herd panic makes the pack:
  four hyenas placed together with the chase brain, and when one is alerted, all within 150 m are.

**B: works, but borrowed animation shows**

- **Lion.** A new mesh on the buffalo or zebra rig. A horse's gallop under a big cat will be noticed. A
  pounce can be keyed by hand into a copied bank.
- **Ostrich.** A chicken clone with an ostrich mesh and the chicken rig's bones lengthened. It inherits
  all seven chicken clips, and run-jump doubles as a kicking lunge. The chicken clips may carry
  chicken-sized hip motion, so feet could slide or sink *(inferred)*. Animating as a chicken (5) while
  behaving as a zebra (2) gives it 25 m sight instead of a chicken's 3 to 5 m.
- **Snake as a still hazard.** A coiled snake that never moves needs no rig: a hiss and a bite when the
  player steps close, from a trigger and `SendPierceStimWithOrigin`. Far Cry 3 ships stationary snakes
  too.

**C: all its animation keyed by hand**

- **Crocodile.** A new mesh on the buffalo rig, re-posed to sprawl, which is allowed while bone names
  and order stay. No Far Cry 2 clip fits, so idle, crawl and lunge are keyed by hand into copied banks.
  Far Cry 3's crocodile barely moves and does not pathfind, so three clips are enough for an ambusher at
  the waterline.

**D: out of reach with today's tools**

- a snake that moves (a new rig)
- Far Cry 3's own clips (the `.mab` `0x61` decoder)
- mauling (paired player animations)
- coordinated packs (a plugin)
- a swimming crocodile (swimming for animals)

The build order that follows: buffalo, hyena, fish, ostrich, then lion and crocodile.

## Adding instead of replacing

The aim is a pack like a DLC: install it and the world draws its animals from a bigger pool, with
nothing replaced. [Where animals come from](../engine-internals/animals.md#where-animals-come-from)
sets the terms: about 160 quadrupeds are placed one by one in world 1 and 114 in world 2, no spawner
or random pool exists, and animals are not saved, so a pack works on old saves.

### New archetypes, as a data layer

New archetypes such as `Animals.DLC.Hyena`, `.Lion`, `.Ostrich` and `.AngryBuffalo` go into both
worlds' entity libraries as fragments, the path [new weapons](../modding/adding-a-weapon.md) take.
Each brings its assets and depload entries, a brain per behaviour (predator, defensive, skittish),
and its own MOVE branches under a new animation species value (7 and up). Animal clips sit in depload
under the `CMovementResource` entries named after MOVE states (`quadruped_eat` and the rest), not
under an animation package *(inferred from the weapon rule)*. A new behaviour species value must be
added to the smart-terrain points' `SupportedAnimalSpecies`, or the animal gets no idle actions;
reusing an existing value inherits them.

### A pool over the placed animals, as a plugin

The placed animal's archetype name is resolved in one call,
[`GetArchetypeDescription`](../engine-internals/animals.md#how-a-placed-animal-is-built). An FCSE
plugin detours it while `SpawnEntityFromNode` runs and sometimes passes another name: a zebra
becomes a hyena 30% of the time, by rules in an ini.

- The entity id, position and event links come from the instance, so nothing downstream changes.
  Placed animals override only position, lighting and slope, so the new archetype's model, species and
  brain apply.
- Because animals are not saved, the pick can be either:
  - **fixed**, from a hash of `disEntityId`: the same place always holds the same animal;
  - **rolled on every sector load**: the herd by the river is zebras today and hyenas next time.
- Picking per sector or per herd instead of per animal keeps herds whole, so a herd becomes a pack
  instead of one hyena among zebras.

### More animals, not only different ones

A pool changes what the existing placements become, not how many there are. To fill empty land:

- **A plugin spawner** calls the engine's own
  [runtime spawn](../engine-internals/animals.md#spawning-at-runtime) from C++, places herds out of
  sight around the player, and removes far ones itself, since sector unload probably does not. Every
  spawn must land on the navmesh.
- **Data only:** retail's DLC weapon crates show a host. Five sectors per world place `DominoEntity_9`
  to `_12` in `main` with a `CDominoComponent` (`Domino\User\DLC\DlcNWeapons.graph.lua`,
  `hidStartOnLoad` True); `dlc1weaponsspawn.lua` spawns crates at fixed coordinates, and the DLC 2 to
  4 graphs are empty. A mod can place its own hosts and spawn from a list with Domino's random boxes
  (`random.lua`, `randominteger.lua`). Whether a runtime-spawned animal's AI and physics start, and
  whether it duplicates across loads, is open.
- **A generated placement layer:** permanent extra herds, one XML per entity as in
  [`mods/npc-example`](../modding/adding-an-npc.md), written by a script. The work is ground height and
  navmesh coverage.

## Experiments in order

1. **Spawn the existing buffalo from the console** with `SpawnEntityFromArchetype`
   ([experiment 2](../engine-internals/data-to-live-object.md#2-spawn-an-animal-a-soldier-and-a-typo-at-runtime)).
   It runs the code path a spawner plugin would call.
2. **Clone the buffalo** as `Animals.DLC.TestBuffalo`, with only its speed changed, as a fragment, and
   spawn it from the console. It proves adding archetypes.
3. **Swap every zebra for `TestBuffalo`** with the plugin. It proves the pool's choke point.
4. **The buffalo charge.** Copy `animalbrain.ai.rml` for the buffalo, add a chase toward
   `PlayerPosition` and a `CTaskBroadcastStims` pulse of Crush within about 2.5 m. It answers whether
   chase tasks run on an animal agent and whether the stim hurts the player.

Then the assets, in tier order.
