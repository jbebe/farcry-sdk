---
sidebar_position: 3
---

# Attacking Animals

**Status:** feasibility checked 2026-10-19 from data and code; the buffalo charge built in
`mods/wildlife-overhaul` the same day; [the hyena](#the-hyena) researched as a new archetype the same
day; nothing seen in game. The behaviour cannot be copied from Far
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
| hyena | none: Far Cry 3's hyena archetype ships no mesh, rig, ragdoll, textures or sounds; the rabid dog and thylacine are the nearest | medium | a mesh made for the goat rig; [details](#the-hyena) |
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
  Sight threatens it too: any pawn, the player included, within 25 m
  ([sight](../engine-internals/animals.md#the-animal-agent)), so the charge also fires on a player who
  walks up to the herd *(inferred from the code; not seen)*.
- **The two species values are independent** ([Animals](../engine-internals/animals.md#two-species-values)):
  one picks animations, the other sight and flee distances.

**S: data only, nothing new to make**

- **Cape buffalo charge.** The existing provocation, with chase in place of flee: it runs at the player
  on its own run cycle and sends Crush on contact. A head-butt clip is an extra.

**A: a new mesh on a Far Cry 2 rig, no new animation**

- **Fish shoals.** A copy of the bird flock with a fish mesh, placed under the surface. Skin the side
  fins to the wing bones and the flap cycle becomes fin strokes. No AI; Far Cry 3's fish are harmless
  too. Risk: a zone taller than the water is deep may let fish out of it.
- **Hyena.** A new mesh on the goat rig, on the goat's clips. The zebra rig does not take goat clips.
  The herd panic and per-animal sight make the pack: hyenas placed together with a hunting brain. See
  [the hyena](#the-hyena).

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

## The hyena

Researched 2026-10-19 as a new archetype, `Animals.DLC.Hyena`, beside the animals already there.
Nothing was built or seen in game. Everything up to the mesh and the voice is data, and both of those
have to be made: Far Cry 3 has neither. The one choice with a real cost is the animation species.

### The archetype

A copy of `Animals.Quadrupeds.Goat` with these changes:

| Piece | Value | Basis |
|---|---|---|
| Fragment | `mods/worlds/world1/generated/entitylibrary.fcb/Animals/DLC/Hyena.xml`, and the same under `world2` | JackAll appends a fragment that matches no archetype to the library group named by the first `hidName` segment, `Animals` *(read in `FcbFragments.AppendTarget`)*; the engine keys archetypes by `hidName` alone ([archetype table](../engine-internals/entity-instancing.md#the-archetype-table-is-one-name-keyed-map)) |
| Names | `Name` `DLC.Hyena`, `hidName` `Animals.DLC.Hyena` | the retail pattern *(seen in data)* |
| `disEntityId` | one above 5,735, the same in both worlds | archetype ids are one numbering across both libraries, 1 to 5,735, no duplicates; the goat is 6 *(seen in data)* |
| Mesh | `CGraphicComponent/object/objModel` and its `text_` twin: a new `graphics\characters\animals\hyena\hyena.xbg` | `CFileDescriptorComponent.hidDescriptor` mirrors a build-time `goat.xml` that does not ship; keep it consistent, but the model comes from `objModel` *(inferred, as for [vehicles](../engine-internals/vehicle-physics.md))* |
| Rig | `CAnimationComponent.fileSkeleton`: the goat's own `hips_ref.skeleton` | [rig and animation](#rig-and-animation) |
| Ragdoll | the goat's `goat_ragdoll.hkx`; `fMass` from 50 to about 60 | it is built on the same bones *(inferred)* |
| Animation species | `CAnimal.selAnimalSpecies` 4 (Goat), or 7 with its own MOVE branches | [rig and animation](#rig-and-animation) |
| Behaviour species | `CAnimalAgent.selAnimalSpecies` 2 (Zebra) | 2 gives 25 m sight and the savanna smart-terrain points; the goat's 5 gives 3 m sight ([two species values](../engine-internals/animals.md#two-species-values)) |
| Brain | a hunting brain, [below](#behaviour) | |
| Sounds | `sndAlertSound`, `sndDeathSound` on new banks | [sound](#sound) |
| Beautifier, GOSM | unchanged: `selBeautifier` 1, `animalQuadruped.gosm.xml` | the beautifier reads no bones and no per-animal data *(read in code)*; the GOSM names no species or animal *(seen in data)* |

**Depload.** Nothing is required for the rig, the ragdoll or the clips. World 1's depload lists no
animal skeleton or ragdoll at all, and the goat clips are already listed *(seen in data)*. Retail lists
`goat.xbg` with its materials and textures as a parent of its own, so a `hyena.xbg` entry is
prefetch, not a gate *(inferred from the [texture rule](../file-formats/depload.md))*.

**Precedent.** No mod adds an animal, but Realism Plus adds a whole skinned archetype:
`player.MainCharacter.PawnPlayer.Anonymous_Mercenary` loads a new kit mesh at a new path, shipped
nameless in the patch with no depload entry (`legacy/realism-plus`, `player-playable-characters` and
`graphics-avatar-models`). Its patrol drivers and Scubrah's Patch's nine faction patrols
are further new pawn archetypes. All are on the human rig.

### Rig and animation

**The zebra rig does not take goat clips.** Both `hips_ref` rigs have the same 30 bone names, but the
goat lists the right ear, foreleg and hind leg before the left, and the zebra the left first *(read in
both `.skeleton` files)*. Clips address bones by id, so a goat clip on the zebra rig drives each limb
with the other side's rotations. The zebra's clips hold its hips 1.33 to 1.43 m up, too tall for a
hyena *(read in its clips)*.

**On the goat rig, only the hip height is fixed.** `Hips` is the only bone the goat rig lets
translate, and every goat clip translates only that bone, at 0.67 to 0.78 m; every other bone gets
rotations only *(read in the `.skeleton` and all 16 clips)*. So the mesh must stand with its hips about
0.75 m up, which suits a spotted hyena (0.7 to 0.9 m at the shoulder). Lengthening the legs in a copied
skeleton would lift the feet off the ground, since the hip height comes from the clip *(inferred)*.

**On species 4 it gets the goat's 16 clips:** idle, eat forward and back, head twitch, three look-ats,
walk, fast walk, two walk turns, two fast walk turns, run and two run turns. There is no attack clip,
and death is the idle clip into ragdoll.

Two costs come with them:

- **Speed is the clip's.** Near the player the beautifier moves an animal by its clip's own
  displacement times the animal's 0.9 to 1.1 size factor, and keeps only the direction of the agent's
  velocity ([movement](../engine-internals/animals.md#movement-comes-from-the-clip)). The goat's run
  clip covers 4.55 m/s, so the hyena runs at about 4.55 m/s whatever `fSpeedsRun` says. The player's
  agent speeds give a sprint of 5 m/s *(seen in data; how fast the player really runs was not
  measured)*, so it catches a walking player but probably not a sprinting one.
- **It bleats.** Sounds are fired by the clips. The goat's idle, eat and walk clips play the goat bleat
  banks `00456b1f` and `00456b20` *(seen in depload)*.

**Species 7 removes both, at the price of a MOVE edit.** Copy the goat clips to hyena paths, re-point
or drop their sound events, retime the run cycle shorter so it covers more ground (BlenderFC2's Retime
Animation, confirmed in game on a weapon bank), and add a branch for 7 beside every `AnimalSpecies ==
Goat` criterion in `movemgr.bin`. JackAll writes `movemgr.bin` (`move fragments`, `move assemble`), but
nothing it wrote has been loaded by the game yet. The movement resources build their clip lists from
the MOVE graph, so the copied clips should need no depload entry *(inferred)*. Values above 6 are safe
in code ([two species values](../engine-internals/animals.md#two-species-values)).

### Behaviour

**Within 25 m a hyena already hunts unprovoked.** Sight threatens an animal when any pawn, the player
included, comes within 25 m, with no view cone and no line of sight
([the animal agent](../engine-internals/animals.md#the-animal-agent)). The buffalo brain's alert plan,
which charges when the player is within 40 m, therefore attacks a player who simply walks up. A soldier
can be the threat too, but the charge aims at `PlayerPosition`.

**Beyond 25 m the brain has to look for itself.** The sight radius is hard-coded, and a brain cannot
alarm its own animal ([no self-alarm](../engine-internals/animals.md#a-brain-cannot-alarm-its-own-animal)).
Two routes:

- **Data:** an independent `CScannerIsInDistance` in the idle sub-brain, on `<PlayerPosition>` with
  `InDistance` 60 and `CheckIsInside` 1, starting a hunt plan. Retail's `scriptedanimalbrain` runs this
  scanner class on an animal agent, on a merge position.
- **FCSE:** patch the squared radii (625 and 1,225) in `UpdateVision` per species.

**The hunt plan,** from tasks retail animal brains already run:

1. `CTaskPathFind` to `PlayerPosition`, then `CTaskAnimalPathFollow` at `Speed` 1 (a walk: the stalk),
   re-pathing every second or two while the player is farther than 20 m;
2. inside 20 m, `Speed` 3 (a run), re-pathing every 0.75 s as the buffalo does;
3. inside 2 m, `CTaskBroadcastStims` with Cut (5), radius 2.5. Level 18 is about 100 health on the
   player's curve, a third of the buffalo's gore *(inferred from the curve the buffalo used)*;
4. back off 10 to 15 m with the retail flee, then hunt again.

That `Speed` 1 is a walk and 3 a run is inferred from retail's `SlowPathFollow` and `FastPathFollow`.

**The pack is placement plus the herd panic.** While threatened, an animal sends its
`Stim_AnimalPanic` every 0.25 s to every animal within that stim's own `fRadius`, 20 m on every retail
quadruped, and a receiver turns threatened if the threat is within 150 m of it
([herd panic](../engine-internals/animals.md#the-herd-panic)). It is species-blind:

- a threatened hyena sends nearby zebras fleeing;
- a herd panicking at gunfire turns hyenas within 20 m of it threatened, so they come for the player;
- raising the hyena's `fRadius` to 40 or 60 m spreads the alarm wider. It is archetype data.

The idle scanner's hunt does not make a hyena threatened, so it does not relay. Hyenas placed within
about 10 m of each other still start together, because each one sees the player at almost the same
distance. Far Cry 3's pattern, one attacking while the rest circle, needs an FCSE plugin.

### Sound

- **What a bank is.** Retail alert and death banks are one IMA-ADPCM mono 22,050 Hz audio record, a
  sample and a sound event, in a file named after the event id: the zebra's alert is `0x004B2872`, its
  death `0x004B2873`. A random pick of several is a random container, as in the goat's `00456b20`
  *(seen in data)*. `jackall-cli spk new` scaffolds one; Ogg is accepted as well
  ([`.spk`](../file-formats/spk.md)).
- **How it plays.** The brain's `CTaskPlaySound` with `AlertSound` or `DeathSound` reaches
  `CAnimalAgent::PlaySoundFromTask` (`0x0855CE30`), which stops the entity's sound and plays
  `sndAlertSound` or `sndDeathSound`, with its type, on the entity *(read in code)*. A bank named after
  an id an `.fcb` field holds loads by path with no depload entry (heard for weapons); the same for
  these two fields is *(inferred)*.
- **No donor in Far Cry 3.** Its hyena has no clips and no voice
  ([Far Cry 3's hyena](../engine-internals/far-cry-3-animals.md#the-hyena-ships-no-assets)). Its rabid
  dog and thylacine banks are the nearest, and they use the codec JackAll cannot read. A recorded
  hyena whoop and growl in a new Far Cry 2 bank is cheaper, and closer to the animal.

### Placement

A generated layer, as in [`mods/npc-example`](../modding/adding-an-npc.md): one XML per hyena under
`levels/<level>/generated/worldsectors/worldsector<n>.data.fcb/`, holding `tplCreatureType`
`Animals.DLC.Hyena`, `hidName`, a new `disEntityId` from the mod's own range, `hidPos` and
`hidPos_precise`, `hidAngles`, and the baked lighting copied from a retail animal nearby.

- **The sector** is `n = ⌊y / 64⌋ × 80 + ⌊x / 64⌋` *(checked on a retail zebra and the example NPC)*.
  The level is the one holding that sector's file. Campaign levels lack most sector files, so use
  sectors that have one.
- **Ground z** is the `.sdat` height grid at row `y mod 64`, column `x mod 64`, interpolated: it gives
  34.900 m under a retail zebra placed at 34.8998 *(computed from data)*. `JackAll.Tools`' `SdatSector`
  reads the grid.
- **The navmesh** is `nv_<n>.nvm` in the same level; `WorldNavMesh.ReadSector` gives its triangles, and
  a point must fall inside one. Points next to retail zebras, gazelles or `SavannahAnimals` points are
  on it already *(inferred: animals path there)*.
- **Streaming:** placed entities load within 4 sectors of the player, about 256 m
  ([world loading](../engine-internals/world-loading.md)).

The generator is a script over those two readers: new tooling, but nothing left to reverse.

### Obstacles

| Obstacle | Solved by | Cheapest one-launch check |
|---|---|---|
| A new animal archetype resolves and spawns | data: the fragment | the hyena archetype with the goat mesh as a stand-in, spawned from the console as in experiment 2 |
| The hyena mesh | new art on the goat rig: the goat's bone names, hips at 0.75 m; no new bones | the archetype with the mesh, spawned: does it walk and run without stretching |
| Goat bleats | data: species 7, copied clips and a `movemgr.bin` edit, or accept them | stand by the stand-in while it grazes |
| 4.55 m/s run | the same species 7, with a retimed run clip | race the stand-in |
| Hunting beyond 25 m | data: the distance scanner; or FCSE | the stand-in on the hunting brain, approached to 50 m |
| The bite hurts | data: the stim, as the buffalo's gore | the buffalo check covers it |
| Pack | data: placement and `fRadius` | four stand-ins beside a zebra herd, then one shot |
| Alert and death sounds | data: a new bank and the two fields | shoot one |
| A hyena voice | a recording; Far Cry 3's dog banks would need a codec decoder (new tooling) | none needed |
| Ground and navmesh | new tooling: the placement generator | one generated hyena: does it stand on the ground and path |

The spawn, bleat, speed and hunting checks share one launch: a goat-meshed `Animals.DLC.Hyena` on
species 4 and agent species 2, with the hunting brain, spawned 50 m from the player.

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
under an animation package. Unlike a weapon's package, a movement resource builds that child list
itself from the MOVE state when it loads
([clip membership](../engine-internals/animals.md#animation)), so a clip a MOVE state names should not
need a depload entry *(inferred from the code; not tested)*. A new behaviour species value must be
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
