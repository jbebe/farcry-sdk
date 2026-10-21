---
sidebar_position: 3
---

# Attacking Animals

**Status:** feasibility checked 2026-10-19 from data and code; the buffalo charge built in
`mods/wildlife-overhaul` the same day; [the hyena](#the-hyena), [fish shoals](#fish-shoals),
[the ostrich](#the-ostrich), [a still snake](#the-snake) and [the lion](#the-lion) researched as new
archetypes the same day;
nothing seen in game. The behaviour cannot be copied from Far
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
| lion | tiger or leopard: sounds, textures and tuning; no mesh or clips | medium | a mesh on the wildebeest rig, after a one-line skinning fix in BlenderFC2; hand-keyed pounce and bite; [details](#the-lion) |
| hyena | none: Far Cry 3's hyena archetype ships no mesh, rig, ragdoll, textures or sounds; the rabid dog and thylacine are the nearest | medium | a mesh made for the goat rig; [details](#the-hyena) |
| crocodile | the crocodile: walk, jog, swim and kill clips | medium-high | no animations, MOVE rules or assets behind the Crocodile species value; waterline ambush only |
| fish packs | `Fish_FP`, Far Cry 3's own fish flock; river fish (7 bones) | medium | the bird rig instead of a fish rig, a night-climb plugin, a skinning fix in BlenderFC2; [details](#fish-shoals) |
| snake | the snake (32 bones) | high to move; medium for a still one | moving: a new rig, so the rig exporter. Still: a rigid mesh, two sounds and data; [details](#the-snake) |
| ostrich | the cassowary (two-legged, 51 bones), or Far Cry 2's chicken scaled up | medium via the chicken | a size plugin, a mesh on the chicken rig; [details](#the-ostrich). The cassowary needs a new rig and two-legged animation rules |

Beyond the data-only charge, behaviour lives in one FCSE plugin: chase instead of flee, hits timed to
an animation event, knockback, a camera hit and per-species sight.

## Tier list from what Far Cry 2 has

Ranked by how much each animal can borrow from Far Cry 2's own rigs, clips and systems. Every attack
rests on the same untested pair, a brain chasing the player and a damage stim, which the buffalo charge
tests for all of them.

What the ranking leans on, read in Far Cry 2's data:

- **The chicken is a small ostrich.** Its rig is 11 bones: `Pelvis`, `Neck`, `Head`, `R Wing`,
  `L Wing`, and `Thigh`, `Calf`, `Foot` on each side. Its clips are walk, run, run-jump, eat,
  look-around, sit-down and get-up, and it has a ragdoll and its own MOVE branch.
- **The bird flock is data, but for its night.** `Animals.Winged.Birds`' `CBoidsComponent` holds
  models, skeletons and clips, a zone (`fZoneRadius` 30, `fZoneHeight` 4), speeds, and separation,
  alignment and cohesion weights. The bird rig is `Pelvis` plus two-segment wings. Its climb out of
  sight at dusk is code ([Flocks](../engine-internals/flocks.md#day-and-night)).
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

- **Fish shoals.** A copy of the bird flock with a fish mesh on the bird rig, placed under the
  surface, which is how Far Cry 3 makes its own. A stiff fish needs no new animation; a tail sweep
  needs one clip keyed by hand. At dusk every flock climbs 120 m, so a small plugin keeps fish in the
  water. No AI; Far Cry 3's fish are harmless too. See [fish shoals](#fish-shoals).
- **Hyena.** A new mesh on the goat rig, on the goat's clips. The zebra rig does not take goat clips.
  The herd panic and per-animal sight make the pack: hyenas placed together with a hunting brain. See
  [the hyena](#the-hyena).
- **Snake as a still hazard.** No rig and no animation at all: a rigid coil built as a breakable prop,
  a stim emitter for the bite, and a trigger linked to the hiss, each copied from retail. Far Cry 3
  ships stationary snakes too. See [the snake](#the-snake).

**B: works, but borrowed animation shows**

- **Lion.** A new mesh on the wildebeest rig, whose canter under a big cat is noticed up close. A
  pounce and a bite are keyed by hand into copied clips. It stalks the player with an unused retail
  task that tells whether the player can see it. Far Cry 3's cat sounds and textures carry over. See
  [the lion](#the-lion).
- **Ostrich.** A chicken clone with an ostrich mesh, scaled up whole by the engine's per-animal size
  factor, which a small plugin sets. Lengthened leg bones do not work: the clips fix the hips at
  chicken height. It inherits all seven chicken clips, and run-jump is already a two-footed forward
  strike. Animating as a chicken (5) while behaving as a zebra (2) gives it 25 m sight instead of a
  chicken's 3 to 5 m. See [the ostrich](#the-ostrich).

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
| `disEntityId` | one above 5,743, the same in both worlds | archetype ids are one numbering across both campaign libraries, 1 to 5,735, no duplicates; `tmpla`'s run on to 5,743 with other archetypes (its 5,736 is a PKM); the goat is 6 *(seen in data)* |
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
  dog and thylacine banks are the nearest, in version 4 banks JackAll cannot read yet
  ([the codec](../engine-internals/far-cry-3-animals.md#animal-sounds)). A recorded
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

## Fish shoals

Researched 2026-10-19 as a new archetype, `Animals.DLC.FishShoal`, a copy of the bird flock. Nothing
was built or seen in game. The engine side is in [Flocks](../engine-internals/flocks.md).

Far Cry 3 makes its small river fish exactly this way: `Fish_FP` is a bare `CBoidsComponent` with
the birds' values and fish meshes, placed by hand, and never spawned
([Far Cry 3's fish flock](../engine-internals/flocks.md#far-cry-3s-fish-flock)). So the approach is
proven in the next game. In Far Cry 2 three things need more than data: the flock's night climb, the
skinning step in BlenderFC2, and a tail clip if the fish should swim rather than glide.

### What the flock does under water

All of this is read in code, none of it seen:

- **Nothing keeps a fish in the water but the zone.** A unit tests no terrain, water surface or
  object. The zone is centred on the placed point, `fZoneHeight` / 2 above and below it, and it is
  soft: units are steered back from the top and bottom fifth and can overshoot by about
  `fForwardSpeed × sin(pitch) / fTurnSpeed`. Far Cry 3 has no water field either.
- **At dusk the shoal rises out of the water.** Outside sunrise to about sunset the whole flock climbs
  at 8 m/s up to 120 m and is then hidden; at dawn it comes back down. A shoal loaded at night climbs
  at once. The rate and ceiling are code constants, not archetype fields. Far Cry 3 added
  `fDisappearAtNightSpeed` and set it to 0 for its fish.
- **The player cannot shoot or scare a fish.** There is no collision shape, no event handling and no
  reading of the player. Far Cry 3's `Fish_FP` is the same.
- **A subgroup swims in step.** One animation component drives every unit of a subgroup, so three or
  four subgroups on differently timed clips break up the rhythm, as retail's four flap clips do.
- **Speed is constant.** Every unit always moves at `fForwardSpeed` along its local +Y, so the fish
  model must face +Y.

### The archetype

A copy of `Animals.Winged.Birds`, whose only component is `CBoidsComponent`:

| Piece | Value | Basis |
|---|---|---|
| Fragment | `mods/worlds/world1/generated/entitylibrary.fcb/Animals/DLC/FishShoal.xml`, and the same under `world2` | as [the hyena](#the-archetype) |
| Names | `Name` `DLC.FishShoal`, `hidName` `Animals.DLC.FishShoal` | the retail pattern |
| `disEntityId` | the next free id above the hyena's, the same in both worlds | as the hyena |
| `hidResourceCount` | the distinct models, skeletons and clips the subgroups name: Birds' 7 is 2 + 1 + 4 | *(inferred)* |
| `SubGroups` | three or four subgroups of 4 to 6 fish, each on its own clip | flocking cost grows with the square of the units; Far Cry 3 uses 4 × 5 |
| `fileDefaultModel`, `fileDefaultSkeleton`, `fileDefaultAnimationName` | the fish's, so an empty subgroup cannot fall back to a bird | |
| `fZoneHeight` | 1 to 2 m, never more than the water depth minus a margin | the zone is soft |
| `fZoneRadius` | 4 to 10 m | |
| `fPitchSoftMax` | 5 to 8 | it bounds wanderers' pitch and the push back from the zone's top |
| `fForwardSpeed` | about 1.5 m/s; Far Cry 3 kept 5 | slower is calmer and overshoots less |
| `fBankingAmount` | about 0.2 | fish barely roll into turns |
| the rest | the birds' values, as Far Cry 3 kept them | |

Placed instances may override `fZoneRadius` and `fZoneHeight` per shoal, as 45 retail bird flocks do.

**Depload:** nothing is required. No depload file lists the birds' skeleton or clips, and a flock
loads each model, skeleton and clip by path. Retail lists each bird `.xbg` with its material and
textures; the same chain for the fish `.xbg` is prefetch, added with `jackall-cli depload add
--fragment` *(inferred from the [texture rule](../file-formats/depload.md))*. Whether a clip at a new
path loads with no entry is [an unknown](#unknowns): the weapon rule that an unlisted clip never plays
was measured on MOVE clips, which load through a package, and a flock's clip does not.

### Rig and animation

The bird rig is 8 bones: `Pelvis`, `Pelvis_nub`, and `Wing01`, `Wing02` and a nub on each side.
`Wing01` sits at the body's centre; `Wing02` pivots 0.34 m out to the side. `bird01` and `bird02`
share one mesh of 59, 35 and 19 vertices over three LODs, skinned to only `Pelvis`, `L_Wing01`,
`L_Wing02` and `R_Wing02`: `R_Wing01` is in the node tree but not the palette *(read in the
`.xbg`)*.

The four flap clips (0.37 to 0.73 s, 16 frames per second) roll each `Wing01` about the body's long
axis by about ±27° (±38° in `flapcycled`) and swing `Wing02` further. `Pelvis` only bobs, 1.5 cm up and
down; nothing in any clip turns side to side *(read in the clips)*. So:

- **Fins on the wing bones beat like wings,** fast and large, and on `bird01` the right fin's root
  stays still. A fish 20 to 40 cm long can only use bones at its centre: `Pelvis` and `L_Wing01`.
- **A tail on `Wing01` twists** about the body axis instead of sweeping.

Three ways to animate it, cheapest first:

1. **A stiff fish.** Weight everything to `Pelvis` and keep a flap clip: it glides with a 1.5 cm bob
   and no tail motion. No new animation. From a few metres away, small fish read as a shoal.
2. **A tail sweep.** Weight the rear half to `L_Wing01` and key a new clip that turns `L_Wing01`
   about its local Z, which is vertical in the bind pose *(computed from the rest rotations: two 90°
   turns about Z)*, by ±15 to 20° at about 2 Hz. The tail swings
   about the fish's centre. One bone on one axis, keyed by hand in BlenderFC2 into a copied bank.
   Three copies at different lengths desynchronise the subgroups.
3. **Far Cry 3's river fish** on its own 7-bone `spine_ref` rig, with its 1 s swim clip. Blocked on
   the Far Cry 3 mesh and skeleton reader and the `.mab` `0x61` decoder.

A copied skeleton does not help: BlenderFC2 cannot change the bind pose, so the wing chain cannot be
turned to point back along the body.

### Making the mesh today

The route is the weapon one ([replacing a weapon](../modding/replacing-a-weapon.md)):

1. `jackall-cli fc2model export <extracted tree>\graphics\characters\animals\birds\bird01.xbg -o
   fish.fc2model --rig <full path to birds\pelvis_ref.skeleton> --clip <a flap clip> --no-actor`
2. In Blender, import each LOD in turn from the pack the last one wrote, replace the geometry, weight
   it, and export.
3. `jackall-cli fc2model extract fish.fc2model -o <layer>`, then `jackall-cli mod build`.

Four things stand in the way, all read in code:

| Problem | Effect | Fix |
|---|---|---|
| **Skin width.** `export_xbg._skin` divides the old weight array by the *new* vertex count to get the influences per vertex; JackAll always reads 8 (`MeshGeometry.SkinStride`) | a skinned part with a new vertex count gets misaligned weights, or fails to extract, and validation does not notice. It comes out right only at 53 to 59, 32 to 35 and 17 to 19 vertices for LOD 0, 1 and 2 | tooling: one line, taking the width from the stride. Until then a fish of 59, 35 and 19 vertices, which is enough for a small fish |
| **Rig by file name.** A loose-tree export resolves a path that is not a real file by file name alone, so `birds\pelvis_ref.skeleton` can pick another animal's `pelvis_ref.skeleton` | the pack carries the wrong rig | pass a full file path, as above |
| **New paths.** Extract writes each entry at its own game path, and no command renames a pack's model, material or textures; the add-on never writes materials | without renaming, the fish overwrites `bird01.xbg` and its material, and the birds become fish | tooling: a rename for a pack's entries. By hand: edit the paths in `manifest.json` and the mesh's material list *(inferred, untested)* |
| **Channels.** On a part whose vertex count changes, a channel Blender does not supply keeps the bird's array | the fish needs two UV layers and a colour attribute | author both |

The fish must also fit the bird's position quantisation, about the bird's ±0.68 m extent; a vertex
past it is refused at extract.

### The night

Without a plugin a shoal rises out of the water at dusk and falls from the sky at dawn. A small FCSE
plugin fixes it: on every flock whose default model is the fish, write its climb rate (GOG `+0x94`)
as 0, so it stays down and visible all night, or its ceiling (`+0x8C`) as 0, so it is hidden from
dusk to dawn. Either is a few lines, after `Finalize` or in a detour of GOG `0x1005DB70`. Patching
the constants in the constructor would change the birds too.

### Placement

One XML per shoal, as for [the hyena](#placement): `tplCreatureType` `Animals.DLC.FishShoal`,
`hidName`, a new `disEntityId`, `hidPos` and `hidPos_precise`, and optionally `fZoneRadius` and
`fZoneHeight`. No navmesh, lighting or ground snap is needed.

- **Water level** is in the sector's `.sdat` header, a float at `0x3C`: 16.0 m in most water sectors
  ([Flocks](../engine-internals/flocks.md#retail-use)).
- **Depth** is that level minus the terrain grid. The whole zone disc, plus overshoot, must clear the
  bed: the entity z is the middle of the water column there, and `fZoneHeight` at most the shallowest
  depth under the disc minus a metre.
- **Where to start.** 42 retail bird flocks already hover over water, the deepest 13.8 m (world 2,
  sector 2369). World 1 sector 4116, the fishing village, has both a bird flock over water and
  retail's only fish model, a dead-fish prop.

A script over the `.sdat` reader places shoals: new tooling, nothing left to reverse.

### Obstacles

| Obstacle | Solved by | Cheapest one-launch check |
|---|---|---|
| A flock archetype placed under water appears there | data: the fragment and a placed instance | a `FishShoal` stand-in on the bird meshes at sector 4116, by day |
| Fish are visible through the surface | unknown: the water shader | the same launch, from the bank and from under water |
| Fish stay in the water | data: zone and placement | the same launch: watch the top of the zone |
| The dusk climb | FCSE: the night plugin | the same launch: `SetScriptedTimeOfDay(19, 0)` from the [console](../engine-internals/developer-console.md), then watch |
| The shoal vanishes when its centre leaves the view | FCSE: refit the entity's box; or small zones | the same launch: turn until the centre is just off screen |
| A clip at a new path, in no depload | data, if it loads | the stand-in on a flap clip copied to a new path |
| The fish mesh | tooling: the skin-width fix and an entry rename; or 59/35/19 vertices and hand edits | the stand-in with the fish mesh |
| A tail that sweeps | a clip keyed by hand on `L_Wing01` | the same |
| Shooting or scaring | FCSE; Far Cry 3 does without | none needed |

The first five share one launch: the stand-in shoal on bird meshes at sector 4116, at noon and then at
19:00.

### Unknowns

- whether fish under the water surface can be seen from above, and from below once the player dives;
- whether the renderer culls a flock by its stale bounding box;
- whether a flock's clip at a new path loads with no depload entry;
- whether a skinned mesh with new topology works in game: none has been seen yet, on any model;
- whether `fDisappearAtNightSpeed` is the night climb in Far Cry 3, which would confirm the plugin's
  fix as the one Far Cry 3 made;
- the exact end of the day window: the sun-cycle curve's 0.53 point was not converted to a clock time.

## The ostrich

Researched 2026-10-19 as a scaled-up chicken, `Animals.DLC.Ostrich`: a copy of
`Animals.Quadrupeds.Chicken` with an ostrich mesh. Nothing was built or seen in game. Lengthening the
chicken rig's bones does not make an ostrich, because the clips fix the hip height. The engine's own
per-animal size factor does, but only a plugin can set it. The engine side is in
[Animals](../engine-internals/animals.md#the-size-factor).

### The chicken's rig and clips

Read in `graphics\characters\animals\chicken\pelvis_ref.skeleton` and the seven clips:

- **11 bones:** `Pelvis` (the root), `Neck`, `Head`, `R Wing`, `L Wing`, and `Thigh`, `Calf`, `Foot`
  on each side. Only `Pelvis` is marked to translate.
- **Only `Pelvis` moves in any clip.** All seven translate it and nothing else; the other ten bones
  get rotations only. The wings are held still in walk, run, eat and look-around, and keyed in get-up,
  sit-down and run-jump.

| Clip | Length | `Pelvis` height | Root motion |
|---|---|---|---|
| walk | 2.07 s | 0.285–0.288 m | 0.30 m/s |
| run | 0.40 s | 0.285 m | 3.75 m/s |
| run-jump | 1.47 s | 0.278–0.544 m | 5.49 m in all, 3.74 m/s |
| eat | 16.7 s | 0.284–0.350 m | none |
| look-around | 8.0 s | 0.283–0.286 m | none |
| sit-down, get-up | 0.83 s each | 0.165–0.298 m | none |
| sit-look-around | 23.3 s | 0.165–0.175 m | none |

Posed from the clips, a planted foot sits at 0.027 m and the head at 0.42 to 0.44 m in the walk
*(computed)*. The archetype's speeds match the clips (`fSpeedsWalk` 0.3, `fSpeedsRun` 3.75), and
MOVE's chicken locomotion blends on the Speed channel: look-around at 0, walk at 0.3, run at 3.75.

### Why longer bones are not enough

- **The bones below the hips take their length from the `.skeleton`.** No clip translates them, so
  a copied rig with longer offsets keeps every clip's rotations.
- **The hips do not.** A clip's `Pelvis` translation replaces the rig's offset; it is not added to it
  ([Animals](../engine-internals/animals.md#the-rigs)). Legs four times as long on 0.285 m hips put
  the planted feet about 0.75 m underground *(computed)*. Raising the hips takes a rescale of every
  clip's `Pelvis` track and trajectory in copied banks, under species 7 with its own MOVE branches.
  JackAll's `MabEncoder` re-encodes a section it is given, but no command scales one: new tooling.
- **The neck can grow.** `Head` is a leaf whose offset is the neck's length, and it is only rotated,
  so doubling that offset in a copied rig lengthens the neck in every clip.
- **The mesh must be bound to the longer rig.** Skinning goes through the `.xbg`'s own inverse-bind
  matrices ([bone palettes](../file-formats/xbm-xbg.md#charactercreature-bone-palettes)), and
  BlenderFC2 cannot change a bind pose. A mesh bound to the longer rig needs the rig exporter. One
  bound to the chicken's proportions, with the neck blended from `Neck` to `Head`, would be pulled
  long at run time by the longer rig *(inferred)*.
- **The rig's own scale field is dead** ([`m_flScaledFactor`](../file-formats/skeleton.md#remainder)).

### Scaling the whole animal

`CGraphicComponent +0x1BC` scales the mesh, the posed skeleton including the clip's hip height, and
root motion together *(read in code)*. No archetype field sets it and no Lua binding is named for a
scale; only `CAnimal::Finalize` writes it, with the 0.9 to 1.1 jitter. So the size takes an FCSE plugin: detour
`CAnimal::Finalize` (GOG `0x10683280`, `__thiscall`, no arguments) and after it returns, write the
scale into the entity's graphic component for the ostrich archetype. A few lines.

| Scale | Neck | Hips | Head, walking | Head, running | Walk | Run |
|---|---|---|---|---|---|---|
| 3.5 | chicken's | 1.00 m | 1.55 m | 1.42 m | 1.06 m/s | 13.1 m/s |
| 4 | chicken's | 1.14 m | 1.77 m | 1.62 m | 1.21 m/s | 15.0 m/s |
| 4 | doubled | 1.14 m | 2.24 m | 2.00 m | 1.21 m/s | 15.0 m/s |
| 4 | 2.5 times | 1.14 m | 2.47 m | 2.21 m | 1.21 m/s | 15.0 m/s |

*(Computed from the walk and run clips.)* A scale of 4 with a doubled neck is about ostrich size. The
scale brings its own costs:

- **Keep the archetype's speeds at the chicken's.** MOVE's thresholds (walk at 0.3, run at 3.75, the
  run-jump gate at 3.6) compare the agent's speed, so `fSpeedsWalk` and `fSpeedsRun` stay 0.3 and
  3.75. Near the player the scaled root motion moves it at 15 m/s anyway. Far away, where the animal
  moves by the agent's velocity, it moves four times slower *(inferred)*. Species 7 with MOVE bands
  scaled by 4 and matching speeds removes that.
- **Culling.** The bounding sphere's radius is not scaled, so set `bOverrideLODSphere` True and
  `fLODSphereRadius` to about 2.5 in the archetype, or it can vanish while still on screen.
- **Physics.** The capsule (points (0, 0.2, 0.4) and (0, 0.1, 0.28), radius 0.27) and
  `fPathfindRadius` 0.5 are archetype data; multiply them by the scale.

### Two species values

Read in code: `CAnimal.selAnimalSpecies` (`+0xD4`) is read only by the two beautifiers, which hand it
to MOVE, and `CAnimalAgent.selAnimalSpecies` (`+0x1E8`) only by sight, flee and the smart-terrain
finder. Nothing copies one into the other
([two species values](../engine-internals/animals.md#two-species-values)). So 5 and 2 give:

- **Sight and flight of a large animal:** 25 m sight, 35 m once threatened, and a 60 or 20 m flee,
  instead of the chicken's 5 m sight and 4 m flee.
- **Savanna points instead of urban ones.** `SavannahAnimals` points take agent values 1 to 4. Each of
  their four actions has a chicken clip: `Quadruped_Eat` the pecking clip, `_Generic_Movement` the
  chicken's locomotion, `_HeadTwitch` its look-around, and `Pawn_ST_GazelLieDown` sends every
  species but the gazelle to `Quadruped_Eat`, so it pecks again *(read in `movemgr.bin`)*. It never
  sits: `Pawn_ST_ChickenSit` is offered only by urban points.
- **The chicken's voice.** Its eat clip fires 6 sound events and look-around 3, walk and run-jump one
  each. Silencing them takes species 7 with copied clips, as for [the hyena](#rig-and-animation).
  `sndAlertSound` and `sndDeathSound` are unset on the chicken; the ostrich needs its own banks.

### The ragdoll

`chicken_ragdoll.hkx` holds six bodies (`Ragdoll_Pelvis`, the neck, both thighs, both wings) on
capsules and hulls, five ragdoll constraints whose pivots are the rig's bone offsets (the right
thigh's is (0.131, 0.0952, 0.033)), an `hkaSkeleton` reference pose, two skeleton mappers with no
translation, and a fixed 2.5 m `Plane01` body. The calves, feet and head have no body. The bodies
weigh 1 kg (pelvis), 0.25 (neck) and 0.1 (each thigh and wing) *(read with `jackall-cli hkx dump`)*.

JackAll can scale it: `hkx verify` rebuilds it byte for byte, `hkx check` finds nothing, and `hkx
decode` / `encode --original` rewrites any member. A uniform scale is every body's translation,
centre of mass and `objectRadius`, every capsule's ends and radius, every hull's vertices, plane
distances, bounds and radius, the constraints' pivot translations, the reference pose's translations
and `allowedPenetrationDepth`, all times the scale, and `inertiaAndMassInv`'s inertia terms divided by
its square at unchanged mass. There is no `scale` command, so this is a script over the decoded XML:
new tooling. `hkx tree` throws on this file (`ArgumentOutOfRange`, parameter `count`), a bug in it.

Whether to scale it at all depends on the size factor. The ragdoll writes its pose into model-space
node matrices, which are drawn through the scaled matrix, so under the plugin's scale the corpse is
drawn at four times the physical ragdoll about the entity's origin *(inferred)*. A scaled `.hkx` would
then be drawn four times too large again. Which combination looks right is the
[check](#obstacles-2) below.

### The kick

**The run-jump is already a strike.** From 0.27 to 0.53 s both feet are tucked forward and raised:
at 0.40 s the left foot is 0.32 m ahead of the pelvis and 0.32 m up, above the standing hip height,
and the hips peak at 0.544 m at 0.47 s *(computed from the clip)*. The clip already fires an event at
0.40 s, whose payload names `PlaySound`. At a scale of 4 the foot reaches 1.3 m ahead at 1.3 m up,
chest height, 6.1 m into a leap that covers 22 m in 1.47 s, at the running speed.

**How the clip plays today** *(read in `movemgr.bin` and code)*:

- in `Quadruped_Generic_Movement`, at Speed 3.6 or more, 15% of the time (`RandomPercentage` 0 to 15);
- on `PerformingJump`, which only the gazelle beautifier raises, on a turn sharper than 45°.

Neither can be called by a brain.

**Where the contact moment can come from**, cheapest first:

1. **Distance alone (data).** The buffalo's pattern: run at the player and send Crush within reach.
   The leap plays at random while it runs and the hit is not tied to it.
2. **A brain timer (data, with a MOVE and a GOSM edit).** A new MOVE state that plays the run-jump, a
   GOSM state with a sink on a new signal, and in the brain `CTaskSendActionSignal`, `CTaskWait`
   0.4 s, then `CTaskBroadcastStims`. The buffalo brain already uses all three tasks. The state's
   `m_flStartTime` and `m_flStopTime` trim the clip to the leap: the stop time is cut from the end
   (read in code, [pounce and bite](#pounce-and-bite)). Every retail animal leaf leaves them at 0;
   about 550 other leaves set them. Start the leap when the player is about 7.5 m ahead at a scale of 4. That the
   brain's wait lands on the frame is *(inferred)*.
3. **The clip's own event (FCSE).** `CAnimationComponent::DispatchEvent` plays sound events and sends
   every other event to the animal's own entity, so a clip event cannot hurt the player in data. A
   plugin that detours it (server `0x09470B60`; the GOG address was not located) can match the 0.40 s
   event on an ostrich and send Crush to the player if within reach: the exact frame.

JackAll writes `movemgr.bin`, but nothing it wrote has been loaded by the game yet.

### Obstacles

| Obstacle | Solved by | Cheapest one-launch check |
|---|---|---|
| Ostrich size | FCSE: write `+0x1BC` after `Finalize` | the plugin at 4 on every retail chicken: do they stand on the ground and walk without sliding |
| Longer legs | not by the rig; new tooling (a clip rescale) and species 7 | none needed while the whole animal is scaled |
| Longer neck | data: a copied rig with `Head`'s offset doubled; no CLI writes a `.skeleton`, but JackAll's `SkeletonFile` does | a chicken clone on the copied rig with the chicken mesh: does the head ride out on a stretched neck |
| The ostrich mesh | art on the chicken rig; the rig exporter (new tooling) if bound to the longer rig | the clone with the mesh |
| Culling | data: the LOD sphere override | the scale launch: turn until a chicken is at the screen's edge |
| Far-off speed | inferred only; species 7 removes it | the scale launch: watch a chicken run from 100 m away |
| 25 m sight, savanna points | data: agent species 2 | walk to 20 m of a chicken clone on species 5 and 2 |
| Clucking | data: species 7 with copied clips, or accept | the same clone, grazing |
| Alert and death sounds | data: new banks and the two fields | shoot it |
| The ragdoll at scale | unknown; scaling the `.hkx` is new tooling | kill a scaled chicken on the retail ragdoll |
| `hkx tree` on ragdolls | a tooling bug | none needed |
| The kick's timing | data (a brain timer and a MOVE and GOSM edit) or FCSE (the event) | the buffalo charge covers the stim; the leap needs the MOVE edit first |

The first six visual checks share one launch: the scale plugin on the retail chickens, by day.

### Unknowns

- whether the per-frame pose builder takes a clip's translation in place of the rig's offset, as the
  IK path does;
- how a scaled animal's ragdoll looks, and whether its entity follows the ragdoll;
- whether a far-off scaled animal visibly crawls;
- whether the brain's wait lands the hit on the strike frame.

## The snake

Researched 2026-10-19 as a still hazard: a coiled snake that never moves, hisses when the player comes
near, bites on contact and can be shot dead. Nothing was built or seen in game. Engine detail is in
[stims and damage](../engine-internals/stims-and-damage.md) and
[proximity triggers](../engine-internals/entity-instancing.md#cproximitytriggercomponent--the-only-trigger-with-geometry).

The cheapest route needs no graph, script or plugin. Each behaviour copies something retail already
places:

- **the bite** is a stim emitter, the component a campfire burns with;
- **the hiss** is a trigger linked to a sound, as on the creaking dock in `w1_b_3`;
- **the death** is a breakable prop changing state, whose links switch the rest off, as a campfire's
  logs and a shot-out street bulb do.

Only the mesh and two sounds are new. Far Cry 3 has stationary snakes too (`_Stationary` variants),
with health 2 and damage 100 to 120.

### Why not a graph

The brief's route works on paper. A `ProximityTrigger` node's `Enter` hands out the entering pawn as
`Collider`, which can feed `SendPierceStim`'s `Target` directly, so `GetLocalPlayerId` is not
needed. `SendPierceStimWithOrigin` delivers one bullet hit of the given level straight to the target
*(read in code)*. But the graph needs a host entity, names each trigger by its literal entity id, and
bites once per entry unless it loops on a timer. The emitter does all of that from data. The graph
stays the fallback if [the first check](#obstacles-3) shows that an emitter does not reach the player.

The mines are no shortcut: the IED mine has no trigger and does no damage by proximity
([mines](../engine-internals/stims-and-damage.md#mines)).

### The pieces

Three placed entities per snake, grouped like the campfire prefab:

| Entity | Made from | What it does |
|---|---|---|
| the snake | a new archetype, `Animals.DLC.Snake`, copied from a two-state breakable prop | draws the coil, is shot and dies, plays the hiss |
| the bite | a new archetype, `StimEmitters.DLC.SnakeBite`, copied from `StimEmitters.Burn.BurnEmitter` | sends Pierce to everything within 1 m once a second |
| the hiss trigger | placed only, copied from `ProximityTrigger_Sound_Dock_Creak_14` | its `Enter` plays the hiss on the snake |

Every retail proximity trigger is an entity of its own, so this keeps to that. One entity carrying
everything would probably work: `CTriggerComponent` already sits beside a mesh and a
`CCompoundPhysComponent` on 16 retail door archetypes, and animals and props all carry the empty
`CSoundComponent` a sound needs *(seen in data)*. But no retail entity mixes an emitter or a proximity
trigger with a breakable prop, so it is untested.

### The snake archetype

The donor is `IGE_Archetypes.AutoGen.GarbageBag02`. It is 0.78 × 0.87 × 0.51 m, about a large coil,
and its three mesh parts are `StatePrim01` (whole) and `StatePrim02` and `StatePrim03` (torn). Any
two-state prop works; world 1 has 683 of them.

| Piece | Value | Basis |
|---|---|---|
| Fragment | `mods/worlds/world1/generated/entitylibrary.fcb/Animals/DLC/Snake.xml`, and the same under `world2` | as [the hyena](#the-archetype) |
| Names, id | `Name` `DLC.Snake`, `hidName` `Animals.DLC.Snake`, the next free `disEntityId` in both worlds | as the hyena |
| Mesh and collision | the descriptor's `.xbg` and `.hkx` paths, and `CCompoundPhysComponent`'s `hidResourceId`, at new paths; the descriptor's boxes refitted to the new mesh | the descriptor bakes object indices, boxes and the state-to-body map *(seen in data)*; the boxes as in [replacing a weapon](../modding/replacing-a-weapon.md#step-6--regenerate-the-baked-bounding-boxes) |
| `Damage` | `bDamageable` True, **`bPlayerOnly` True** | the guard against its own bite, [below](#the-bite) |
| State 0, `StatePrim01` | `fHealth` 5; Pierce, Crush and Burn on; `OnDamage` Next | any hit kills it: `ObjectDefault` turns even level 9 into 47 health *(read in data)* |
| State 1, `StatePrim02` and `03` | the dead snake, in one or two pieces; `OnDamage` Stay | Next keeps the body on the ground. What Destroy does to the entity's links was not read |
| `CPersistComponent` | `selLevel` 1 | like animals: a dead snake is back when its sector reloads. The bag's 2 would save it dead |
| `CAIShootMeObject` | removed | it probably marks the bag as something soldiers shoot *(inferred from the name)* |
| `CCountersComponent`, `CSoundComponent`, `CEventComponent` | kept: `ObjectDefault`, empty, links per instance | the empty sound component is the hiss target *(read in code)* |

The placed snake's links, all on `OnStateChange`, fire when it dies:

- to the bite: `CEntityEvent` `Disable`, as the campfire logs send their emitter;
- to the hiss trigger: `CTriggerEnableEvent` `SetDisabled` with `bEnable False`, as the street bulb
  sends its trigger;
- to itself, optionally: `CSoundEvent` `PlaySound` with a death sound.

### The bite

`StimEmitters.DLC.SnakeBite` is the `BurnEmitter` with:

| Field | Value | Effect |
|---|---|---|
| `bEnable` | True | always armed; the snake's death disables it |
| `fRateOfEmission` | 1 | the seconds between bites: the first lands up to a second after the player steps in |
| first stim | `selType` 3 (Pierce), `nLevel` 18, `fRadius` 1, `bFalloff` False | 100 health a bite, 7 to 10% of an Experienced player's 1,025 to 1,400 |
| second stim | the empty one, as retail | |

Place it at the coil's centre. It bites everything with a physics body within 1 m: the player,
soldiers and buddies, and animals. An animal hit by Pierce turns threatened
([hits](../engine-internals/animals.md#the-animal-agent)), so a zebra that steps on a snake bolts
*(inferred)*.

**It also bites the snake.** An emitter skips no entity, not even its own
([stim emitters](../engine-internals/stims-and-damage.md#stim-emitters)), and the coil sits inside
the radius. Retail meets this at the campfire by turning `bBurnStim` off on the logs. A snake must
take Pierce from bullets, so the guard is `bPlayerOnly`: `DamageParts` ignores any stim whose
instigator is not the player *(read in code)*, and the emitter sets only a sender *(read in code)*,
so its instigator stays unset *(inferred)*. The cost is that soldiers' bullets do not kill snakes. A
second guard is to bite with Cut (5), which the player takes on the same curve but which has no flag
on a breakable prop's state *(inferred from the flag set)*.

### The hiss

The trigger is a copy of the dock's:

- `CTriggerComponent` with `static` True;
- `CProximityTriggerComponent` with `bEnabled` True, `selUsableBy` 2 and `vectorSize` about 4 × 4 × 2,
  centred on the snake;
- one link: `Enter` → `CSoundEvent` `PlaySound`, `sndSound` the hiss's id, `sndtpType` 11,
  `bAssignOnEntity` True, `bInterruptible` True, aimed at the snake.

PlayerOnly means a pawn that is the local player, on foot or not; `selLocomotionFilter` 2 would leave
out a player in a vehicle ([who sets it off](../engine-internals/entity-instancing.md#cproximitytriggercomponent--the-only-trigger-with-geometry)).
It hisses once per entry. Whether `vectorSize` is the box's full or half extent is still open, so 4 m
means a reach of 2 or 4 m from the snake; the first launch measures it.

The hiss and the death sound are new banks, made with `jackall-cli spk new`, named after new ids and
positioned with a rolloff ([`.spk`](../file-formats/spk.md)). A bank named after an id loads by path
with no depload entry; that was heard for weapons and is *(inferred)* here. A looping hiss with no
trigger would be a placed sound point, but a sound point names a record (`sndptSoundPoint`, such as
`004BE46B`) whose definition was not traced.

### The mesh

A still snake is rigid, so it needs no rig and no clip. The route is the weapon one with a prop:

1. `jackall-cli fc2model export graphics\objects\households\decoration\garbagebag02.xbg --game
   "C:\Games\Far Cry 2" -o snake.fc2model`. Tested on a loose copy: it exports with no `--rig`, and
   every node is unskinned (`skin_index` -1, no bind matrices).
2. In Blender, replace `STATEPRIM01`'s three LODs with the coil and `STATEPRIM02` and `03` with the
   dead snake. Every part carries two UV sets, colours and a tangent frame, so author both UV layers and
   a colour attribute. Vertices must stay within ±1.0 m (`pos_compress` 3.05 × 10⁻⁵).
3. `jackall-cli fc2model extract`, then `jackall-cli mod build`.

Two of the [fish's mesh problems](#making-the-mesh-today) do not apply. The skin-width bug cannot
reach it, because `export_xbg._skin` returns at once for a part with no weights *(read in the
add-on)*. And no rig is resolved. The path problem does apply: without renaming the pack's entries,
the snake overwrites `garbagebag02.xbg` and every garbage bag in the world becomes a snake. The
material is a copy of the bag's `.xbm` with snakeskin textures, since the add-on writes no materials.

**Collision** is the bag's `.hkx`: an 8-vertex hull for `StatePrim01`, a 16-vertex hull and a small
box for the pieces, and `HIGH_StatePrim01`, a MOPP over 214 triangles. It is about coil-sized, so
copy it unchanged. A snake-shaped hull is new tooling: `jackall-cli hkx` moves, turns and resizes
boxes, but nothing builds a hull or a triangle mesh from Blender, though `hkx encode` refreshes a MOPP
after an edit. Which body a bullet hits, the hull or the `HIGH_` mesh, was not read.

### Placement

One XML per entity in a generated layer, as [the hyena's](#placement): the snake at ground height from
the `.sdat` grid, the bite a few centimetres above its centre, and the trigger centred on it, each
with a new `disEntityId`. Links hold ids, so the three can sit in any order. No navmesh is needed.
The generator is the hyena's script with three entities and three links: new tooling, nothing left to
reverse.

### Obstacles

| Obstacle | Solved by | Cheapest one-launch check |
|---|---|---|
| An emitter's bite reaches the player | data: the emitter | no files: at the campfire beside `SafeHouse_15` (world 1, about 1,332, 3,222, sector 4020), type `#CDominoManager_GetInstance():SendRegisteredEventToEntity("2058516088527799154", "CStimsEmitterComponent_Enable")` and stand in the fire. Expect health to drop every second within 1 m. The event name is *(inferred)* from the Domino node's `CBaseTriggerComponent_SetEnabled`; if it is not found, wait for a soldier to light the fire |
| The snake survives its own bite | data: `bPlayerOnly`, or a Cut bite | the stand-in launch below: does it break by itself |
| It can be shot dead | data: the two states | the stand-in: shoot it |
| Death switches the bite and the hiss off | data: the three links | the stand-in: shoot it, then step on it |
| The hiss on approach | data: the `Enter` link | the stand-in: walk in, noting the distance it fires at, which also settles full or half extent |
| A new archetype at new mesh paths | data | the stand-in on the bag's own mesh first, then the snake mesh |
| The snake mesh | art; renaming a pack's entries is new tooling, or hand edits | the archetype with the mesh |
| Collision that fits a smaller snake | new tooling: a hull from a mesh | shoot just beside the coil |
| The hiss and death sounds | data: new banks | hear them |
| What the HUD shows for a bite | unknown | the stand-in launch |

The stand-in launch tests the rest: `Animals.DLC.Snake` still on the garbage bag's mesh, with the bite
and the trigger, placed beside that campfire. The dock's creak, `0x004C6529`, can stand in for the
hiss.

### Unknowns

- whether an emitter's stim reaches the player at all;
- whether the emitter's stim really carries no instigator, so that `bPlayerOnly` protects the snake;
- what the damage indicator shows for a bite with no instigator;
- whether `vectorSize` is a full or a half extent;
- which collision body bullets hit;
- whether a breakable prop ignores Cut.

## The lion

Researched 2026-10-19 as a new archetype, `Animals.DLC.Lion`: a lion mesh on the wildebeest's rig,
with a pounce and a bite keyed by hand into copied clips, and a brain that stalks the player
unprovoked. Nothing was built or seen in game. All of it is data, except two small tooling fixes that
any new skinned mesh needs. The cost is the gait: a lion that canters like a wildebeest.

### Which rig

The three large rigs, posed from their idle clips' first frame *(computed with JackAll's `MabPose`)*:

| Joint height, standing | cape buffalo | zebra | wildebeest |
|---|---|---|---|
| hip joint (`UpLeg`) | 1.25 m | 1.25 m | **1.09 m** |
| shoulder joint (`Arm`) | 0.91 m | 1.02 m | 0.95 m |
| elbow (`ForeArm`) | **0.69 m** | 0.88 m | 0.84 m |
| wrist (`Hand`) | **0.41 m** | 0.54 m | 0.48 m |
| head | 1.22 m | 1.49 m | 1.30 m |
| hip to shoulder joint, along the body | 1.55 m | 1.35 m | **1.22 m** |
| neck, `Neck` to `Head` | 0.67 m | 0.69 m | **0.60 m** |
| tail, `Tail` to `Tail1`, then a leaf | 0.39 + 0.41 m | 0.35 + 0.50 m | 0.30 + 0.48 m |

A lion stands about 1.0 to 1.2 m at the shoulder with its wrist a hand's width off the ground, a long
low body and a short neck *(general reference, not game data)*. No rig is close in the legs: every one
has the hoofed animal's long cannon bone, so the "wrist" sits 0.4 to 0.55 m up. The buffalo has the
lowest elbow and wrist and carries its head lowest, but it is 15% taller at the hip than the
wildebeest and its back 27% longer. The **wildebeest** is the best overall: the lowest hips, the shortest back and neck, and
a head level with its back.

Their locomotion, from 60 poses per cycle *(computed)*:

| Clip | cape buffalo | zebra | wildebeest |
|---|---|---|---|
| walk | 0.65 m/s | 1.26 m/s | 1.12 m/s |
| fast walk | 1.35 m/s | 1.85 m/s | 2.03 m/s |
| run | 5.90 m/s, a slow gallop with three or four feet down at once | 12.28 m/s, a gallop: each hoof down 10% of the cycle, then 0.23 s in the air | 8.35 m/s, a three-beat canter: one hind, then a diagonal pair, then the leading fore |
| back flex in the run (hip-to-shoulder distance) | 7% | **20%** | 7% |
| hips bob in the run | 0.18 m | 0.10 m | 0.21 m |
| head in the run | 1.07–1.48 m | 1.38–1.67 m | 1.16–1.32 m |

All three walks are lateral-sequence four-beat walks (left hind, left fore, right hind, right fore),
the footfall order a cat walks with too. The zebra's gallop is the most cat-like run, with its long
flight and a back that bends, but on the tallest legs with the head held high.

**No other Far Cry 2 clip is closer.** None of the 30-bone rigs has a rear, kick, jump or attack
clip: each has the same 16 (eat twice, head twitch, idle, three look-ats, run and two run turns, walk,
fast walk and four walk turns). The zebra's `PerformingJump` branch plays its gallop. The gazelle's
jump leaf names a clip hash no shipped clip has, probably cut *(inferred)*. The chicken's run-jump is
on its 11-bone rig. And no clip moves between these rigs: the four orders their bones differently, and
even mapped by name the zebra's gallop on the wildebeest's bone lengths lifts the hind hooves 0.12 m
above the fore hooves ([the rigs](../engine-internals/animals.md#the-rigs)). Moving a clip across
needs a retargeter that corrects the feet: new tooling.

**With a size plugin the zebra becomes an option.** The ostrich's plugin ([scaling the whole
animal](#scaling-the-whole-animal)) at 0.8 puts the zebra's hip joint at 1.0 m, its back at 1.08 m and
its gallop at 9.8 m/s, with the 20% back flex kept; the head stays high (1.19 m over 1.14 m hips) and
the wrist at 0.43 m. Without a plugin, use the wildebeest.

### How bad the hoofed gait looks

| Clip | On a lion mesh | Rating |
|---|---|---|
| idle, look-ats, head twitch | straight columns for legs, an ear flick; reads as a lion standing | fine |
| walk, fast walk | the footfall order is a cat's. The wildebeest's wrist, 0.48 m up, folds by up to 65° in each swing *(computed)*, so the forearm visibly kinks halfway down. The hooves lift only 5 to 7 cm | noticeable within about 20 m |
| run (the charge) | a rocking-horse canter with a stiff back where a lion gallops with a flexing spine; the hips bob 0.21 m. Head on, as the player mostly sees a charge, the back is least visible | the giveaway, but brief |
| eat | grazing with the muzzle on the grass | absurd; must never play, [below](#the-lion-archetype) |
| footfalls and voice | the walk and run clips fire `00456acb`, the switch every gallop shares; the idle fires `00456b60`, three wildebeest grunts *(seen in depload)* | wrong until the clips are copied with their events changed |
| death | the idle clip, then the ragdoll | fine |

Overall: from 30 m and more, a lion; up close, a big cat walking and cantering like a hoofed animal.
That matches the tier list's B. The two fixes that help most are species 7 with copied clips, which
silences the hooves and grunts, and the pounce, which replaces the last seconds of the charge, the
part the player watches closest.

### The lion archetype

A copy of `Animals.Quadrupeds.Wildebeest` (id 7) with these changes:

| Piece | Value | Basis |
|---|---|---|
| Fragment, names, id | `Animals/DLC/Lion.xml` in both worlds' `entitylibrary.fcb`, `Name` `DLC.Lion`, `hidName` `Animals.DLC.Lion`, the next free `disEntityId` | as [the hyena](#the-archetype) |
| Mesh | `objModel` and its `text_` twin: a new `graphics\characters\animals\lion\lion.xbg` | |
| Rig | `fileSkeleton`: the wildebeest's own `hips_ref.skeleton`, unchanged | the bind pose cannot change, [below](#the-lion-mesh) |
| Ragdoll | the wildebeest's `wildebeest_ragdoll.hkx`; `fMass` from 300 to about 190 | 19 capsule bodies whose centres are the rig's bind joints exactly *(read with `hkx dump`)*, so a mesh on the same rig fits it |
| Animation species | `CAnimal.selAnimalSpecies` 2 (Wildebeest) to start; 7 with its own MOVE branches for the pounce, the bite and silent clips | [pounce and bite](#pounce-and-bite) |
| Behaviour species | `CAnimalAgent.selAnimalSpecies` 7 | no smart-terrain point lists 7, so it never grazes and idles in place ([no STP](../engine-internals/animals.md#what-a-brain-could-reuse)); 7 still gets the large animals' 25 m sight and 60/20 m flee |
| Speeds | keep `fSpeedsWalk` 1.125, `fSpeedsRun` 7.9 | MOVE picks clips by these; the clip sets the real speed |
| Brain, GOSM | the lion brain [below](#stalking-and-hunting); a copy of `animalQuadruped.gosm.xml` with the pounce and bite states | |
| Sounds | `sndAlertSound`, `sndDeathSound` on new banks (the wildebeest has neither) | [sound](#lion-sound-and-textures) |
| `Stim_AnimalPanic` | `fRadius` 20 or more | a lion's alarm sends nearby zebras running |

### The lion mesh

Read and run on copies of the three animals with `jackall-cli fc2model` and BlenderFC2 in headless
Blender 5.2 *(verified unless marked)*:

| | wildebeest | cape buffalo | zebra |
|---|---|---|---|
| LOD tiers (switch distance, m) | 4 (15, 28, 68, 140) | 4 (15, 28, 68, 140) | 4 (15, 28, 61, 180) |
| LOD 0 clusters (vertices) | eye 47, alpha-blended mane 44, body 1,574 | head 38, body 1,448 | eye 36, mane 38, body 1,153 |
| body vertices, LOD 1 / 2 / 3 | 931 / 473 / 242 | 799 / 415 / 211 | 763 / 355 / 187 |
| body palette, LOD 0–1 / 2 / 3 | 30 / 28 / 19 bones | 30 / 28 / 19 | 30 / 28 / 26 |
| position range, each axis | ±2.0 m | ±3.0 m | ±2.0 m |

- **The palette is no limit.** A cluster holds at most 48 bones (`XbgFile.cs`, `rules.py`), and the
  body's LOD 0 and 1 already use all 30. Weights to a bone outside a part's palette are refused at
  export (`part.not-encodable`), not dropped, so LOD 2 and 3 must stay on their 28 and 19.
- **The lion fits the box.** On the wildebeest rig the nose is at +1.39 m and the tail tip at
  −1.71 m, so a 2.5 m lion with a 0.9 m tail along the rig fits ±2.0 m. A vertex past it fails at
  extract; Blender's check panel does not test it. `pos_compress` can be raised to the buffalo's by
  hand in `mesh.json` and re-encodes.
- **The mane has a home.** Cluster 1 is an alpha-blended `Generic` part on `Spine`, `Neck`, `Neck1`,
  `Head`, `Jaw` and both shoulders *(its use as a mane is inferred)*.
- **The tail is two segments,** `Tail` and `Tail1` (0.47 and 0.58 m with `TailNub`, about 1.06 m),
  straight back at hip height in the bind pose. Clips swing them up to 92° and 112° when idle, 44° and
  86° in the run. It bends at two joints and never curls; more needs a new rig.
- **The body's material** is `Skin` with six textures: mask (`_m`, DXT1), skin (`_d`, DXT5), normal
  (`_n`, DXT5, with a `_mip0` companion), a shared fur-detail normal, `BloodTexture` (the shared fur
  detail, tiled 14 to 24 times) and a rim-light texture. Eye and mane are `Generic`. The vertex
  colours are data: blue tints, green sets the fur detail *(which channel does what is inferred)*, so
  the lion needs authored colours. UV 1 holds junk but must be filled.

What the tools do today, with the code that decides it:

| Step | Today | Fix |
|---|---|---|
| a new vertex count | **the skin-width bug** (`export_xbg.py`, `_skin`): the influences per vertex are taken as the old weight array divided by the new count. Growing the wildebeest body from 1,574 to 5,823 vertices extracted without error and put 4,368 vertices fully on `Hips`; shrinking it to 876 failed at extract | tooling: one line, taking the width from `MeshGeometry.SkinStride`; with it simulated, both directions extracted with no wrong weights |
| more than 4 influences | the pack holds 8, the file 4 (`VertexEncoder.cs`), without renormalising; the fifth is dropped with no warning, since `validate.py` checks 8 | tooling: warn above 4. Retail uses at most 3 |
| new parts | Add as New Part fails on every animal: it borrows a rigid part's vertex format, and these LODs have none | not needed: reshape the body, eye and mane parts |
| dropping a LOD | not in Blender; deleting the last LOD from `mesh.json` by hand extracts and reads back as 3 | |
| a new material | Blender writes none, but editing the pack's material JSON and `mesh.json`'s `materials` list by hand writes a new `.xbm`. [Texturing a weapon](../modding/texturing-a-weapon.md) says nothing creates one; at tool level this does *(not checked in game)* | tooling: a command for it |
| new textures | replace the pack's PNGs, which are re-encoded with the recorded codec; or `xbt build` | |
| new game paths | hand edits to `manifest.json`, as for [the fish](#making-the-mesh-today) | tooling: a rename command |
| the bind pose | never written; only each node's `extent` is | the rig exporter |

Three traps were found on the way:

- **A renamed normal map still writes `c_wildebeest_n_mip0.xbt`,** over the wildebeest's own, because
  the companion's name is in the carried header (`XbtTexture.cs`, `Fc2ModelApplier.cs`). Dropping
  `header` and `companion_header` from the entry writes one self-contained file instead (not checked in
  game).
- **`--rig` with a full path, then `extract --all`, writes the skeleton back to that path,** outside
  the layer, because `Path.Combine` with an absolute path drops the layer folder
  (`Fc2ModelExtractCommand.cs`). Pointed at `tmp/` or the install, it overwrites the original. Export
  from a scratch copy of the rig, and never pass `--all` with it.
- **A changed shared entry is refused** (`Fc2ModelApplier.cs`); mark the renamed entries `owned` with
  their `origin_sha256` instead of passing `--all`.

The route is the fish's ([making the mesh today](#making-the-mesh-today)) with the wildebeest:
`fc2model export wildebeest.xbg --rig <scratch copy of its hips_ref.skeleton>`, reshape the three
parts on each LOD in Blender, then extract. Every new skinned mesh with a new vertex count needs the
skin-width fix first.

### Pounce and bite

Researched on copies: a clip keyed in BlenderFC2, a new MOVE state built with `jackall-cli move`, and
the GOSM and brain pieces the buffalo already uses. None of it has been loaded by the game.

**The clip.** An animal bank is one clip. Write Animation keeps the source clip's length, rate,
trajectory, tags and events, and writes all 30 bones, of which only `Hips` translates; Retime changes
the length and stretches the events and the trajectory with it *(read in `export_mab.py` and run: the
buffalo run retimed from 10 to 15 frames went from 5.90 to 3.93 m/s)*. A scratch round trip keyed a
1 m `Hips` arc and a foreleg reach into the retimed buffalo run, wrote it and read it back: 0.95 s,
`Hips` 1.40 to 2.45 m, the 3.74 m trajectory and the events unchanged.

The leap's forward travel has to be in the trajectory, which carries the animal; `Hips` can carry the
rise ([the displacement](../engine-internals/animals.md#movement-comes-from-the-clip)). Bases:

| Move | Copy | Why |
|---|---|---|
| pounce | the wildebeest run (0.60 s, 5.01 m) | its trajectory already covers a lion's 4 to 6 m leap; key the rise into `Hips`, the forelegs reaching and the hind legs pushing |
| bite or swipe | the wildebeest head twitch (2.67 s, no sound events) | silent and still; key a head lunge, `Jaw` open and one foreleg raised, then trim |

The run is 0.60 s. Retimed to about 0.9 s for a visible flight, it still covers 5 m, but at 5.6 m/s
against the 8.35 m/s run before it. A leap that keeps the speed, or covers more ground, needs the
trajectory edited: a small script over the pack's `root_translation`, new tooling. The writer drops the empty section 4 every retail clip has, which
`EvaluateSingleAnimNode` skips when no mask bit asks for it *(other readers were not checked)*.

**The MOVE state.** Animal states test `AnimalSpecies` (channel 27) with an EnumEqual per species,
each leading to a clip leaf, then fall back to `CDoNothing`. `Lion_Pounce` (CRC-32 of the lowercased
name, `0xF27D0961`) was cloned from `Quadruped_HeadTwitch` with its wildebeest leaf pointed at a new
clip path; `move assemble` appended it as the 1,701st state, and the graph decodes and verifies byte
for byte. For a one-shot, copy the chicken run-jump leaf's settings (loop override 0, not
interruptible, cut behaviour 1, a 0.13 s blend); head twitch loops and blends in over 0.5 to 1 s.
`m_flStartTime` and `m_flStopTime` do trim a clip: `SetupAnimation` (`0x09B7DF20`) clamps the start
to the cut time, and `GetStopTime` (`0x09B7D940`) returns the duration minus the stop time. Depload
lists one `CMovementResource` per GOSM state with its clips; adding the new state's entry with
`jackall-cli depload add` costs nothing, whether or not it is needed.

**The GOSM state, and why the buffalo's head toss failed.** The head toss stopped every charge task
and then sent `start_headtwitch`, a clip with a still trajectory: with no agent velocity and no clip
displacement the buffalo stopped dead, then looped the toss until told to stop. The pounce state
avoids both:

- a sink on the idle state for `start_pounce`, entering a state whose `animStateID` is `Lion_Pounce`;
- an animal event `UseRotation` (`EventType` 0), so the lion moves by the clip's own displacement and
  yaw with no agent velocity ([the flag](../engine-internals/animals.md#movement-comes-from-the-clip));
- an unconditional connection back to idle, as the smart-terrain states have, which should fire at
  the clip's end *(inferred)*; idle's `IgnoreRotation` clears the flag.

The alternative is to leave `UseRotation` off and keep the path follow running through the leap, so
the agent steers while the clip sets the speed. Every animal state is on MOVE layer −1 and no additive
or upper-body layer was found, so a one-shot cannot play over the run *(inferred)*.

**The brain.** Far Cry 3's tiger jumps from 3 m and swipes from 1.7 to 2.5 m; here the pounce starts
farther out, because the blend and the flight take time. As the ostrich's kick, with the buffalo's
numbers: within about 7 m,
`CTaskSendActionSignal` `start_pounce` (with an empty `actionSignalGroup`), `CTaskWait` for the blend
plus the time to the strike, then `CTaskBroadcastStims` Crush, level 26, radius 4.5,
`broadcastToAllAI` 1. A swipe from standing is the same with `start_bite`, Cut (5) and a lower level,
22 for about 160 health *(inferred from the curve)*. Landing on the exact frame takes the FCSE event
detour described for [the kick](#the-kick).

### Stalking and hunting

The [hyena's hunt plan](#behaviour) carries over, with the buffalo's charge for the attack. One task
makes it a stalk: **`CTaskCheckVisibleByPlayer`**, retail code no brain uses, which branches `Yes` or
`No` on whether the animal covers any pixel of the player's view. It runs on an animal agent, and
every retail animal archetype already has the `COcclusionQueryComponent` it reads
([the task](../engine-internals/animals.md#what-a-brain-could-reuse)). So the lion creeps up while
the player looks away and freezes when looked at:

1. **Idle:** agent species 7 finds no smart-terrain point, so the lion stands where it was placed. An
   independent `CScannerIsInDistance` on `<PlayerPosition>`, `InDistance` 80, starts the stalk.
2. **Stalk, beyond 25 m:** `CTaskCheckVisibleByPlayer`.
   - `No`: path-find to `PlayerPosition` and `CTaskAnimalPathFollow` at `Speed` 1, a walk, for about
     2 s, then check again;
   - `Yes`: stop and `CTaskWait` 1 to 2 s, the idle and look-at clips, then check again.
3. **Charge, inside 25 m:** sight makes the lion threatened and the alert sub-brain takes over: the
   buffalo's charge, with the world-blackboard claim under its own fact name so one lion attacks at a
   time, the 8 m overshoot, a turning radius, and avoidance off.
4. **Strike:** the pounce inside about 7 m, the swipe inside 3.5 m if the pounce missed.
5. **After:** run on past the player, stand 2.5 to 4 s facing them, charge again; beyond 40 m, or after
   a set number of hits, the retail flee.

Gunfire threatens the lion as it does any animal, so a shot within 40 m makes it charge at once
(the buffalo's 40 m test) and one from farther makes it flee.

Two limits, both *(inferred)*: `Yes` is any coverage on any viewport, so a lion at the screen's edge
counts as seen, where Far Cry 3's tiger tests the player's field of view; and whether grass hides it
was not read. If the occlusion query never runs on an animal, the task always answers `No` and the
lion simply walks in without freezing, a harmless failure.

### Lion sound and textures

The donors are Far Cry 3's tiger and leopard ([the big cats](../engine-internals/far-cry-3-animals.md#the-big-cats)).
Far Cry 2 has no cat sound of its own: no name list mentions a lion, tiger, leopard, growl or roar
*(banks are named by hash, so this is inferred)*.

**Sound.** Every cat bank holds Far Cry 2's own IMA-ADPCM, mono at 22,050 Hz, inside a version 4 bank
that `jackall-cli spk list` refuses. A scratch decoder turned all 159 cat streams into WAV files. Six
tiger idles went back in with `spk new 0x00fc0300 <six wavs> --rolloff 0x00442c37`, then `spk encode`.
`spk verify` passed on the bank, a random container of six samples. None of the WAVs was listened to.
Picks, by what Far Cry 3 plays them for:

| Lion sound | Donor bank | Goes to |
|---|---|---|
| alert, a growl | leopard `001A400D` (4 of 4.1–6.5 s) or tiger idle `001AA57E` (6 of 1.4–2.9 s) | `sndAlertSound`, played by the brain's `CTaskPlaySound` |
| death | tiger `001AA57C` (3 of 2.0–2.5 s) | `sndDeathSound` |
| pounce, swipe | tiger `001aa580` (pounce success), `001aa57b` (close attack) | a `CTaskPlaySound` before the strike, or the clip's own event, copied from a clip that already has one ([sound events](../engine-internals/animals.md#animation)) |
| footfalls | none needed | the copied clips with the hoof events dropped |

New ids get new banks, named after the event and loaded by path; that this needs no depload entry is
*(inferred, as for the hyena)*. Getting the decoder into JackAll as a version 4 reader is new tooling.
Until then the scratch decoder's WAVs and `spk new` do the job. The audio is IMA either way, so nothing
is transcoded.

**Textures.** The tiger's maps are 1,024² and all present. Its mask carries the signal in green, which
fits Far Cry 2's `MaskTexture1`, where specular is green alone; the leopard's carries it in red, which
needs a channel move. Normal maps share Far Cry 2's layout, X in alpha and Y in green *(handedness not
checked)*. `xbt extract` refuses Far Cry 3's version 104. The DDS strips out at the offset in the
header's word at `+8`. A tiger diffuse stacked into one 11-mip DDS was wrapped by `xbt build`, once in
`c_zebra_d.xbt`'s header and once in a fresh one, and both read back byte for byte; neither was loaded
in game. A lion slots in as:

| Far Cry 2 `Skin` slot | From |
|---|---|
| `SkinTexture` | `puma_v2_01_d`, a plain tawny coat, the closest to a lion *(inferred from the image)*; the tiger's stripes would need painting out |
| `NormalTexture1` | the tiger's or puma's `_n` |
| `MaskTexture1` | the tiger's `_m`, or the puma's `_s` with red moved to green |
| `NormalTexture2`, `BloodTexture` | the wildebeest's own fur detail, or Far Cry 3's `animal_shortfur02` |
| mane (the `Generic` alpha part) | the tiger's `fur_d` and `fur_m` fur card |

The catch is the UVs. Far Cry 3's maps are laid out for its own meshes. A lion mesh made from scratch
needs its own unwrap and a bake from the cat maps onto it *(inferred)*. Reusing a cat mesh's UV layout
would need the Far Cry 3 mesh reader, which is new tooling.

### Lion obstacles

| Obstacle | Solved by | Cheapest one-launch check |
|---|---|---|
| A new archetype on the wildebeest rig spawns | data | `Animals.DLC.Lion` on the wildebeest's own mesh, spawned from the console as in experiment 2 |
| The lion mesh | art; the skin-width fix and a rename command (new tooling), or hand edits | the archetype with the mesh: does it walk and run without tearing. The first skinned mesh with new topology seen in game |
| Never grazing | data: agent species 7 | the stand-in by a savanna point: it stands |
| Hooves and grunts | data: species 7, copied clips with their events dropped, MOVE branches | stand by the stand-in while it walks |
| Stalking | data: `CTaskCheckVisibleByPlayer` | the stand-in on the stalk brain, 60 m off: look at it, look away, look again |
| The charge and the hit | data: the buffalo's | covered by the buffalo |
| The pounce leap | data: the clip, the MOVE state, the GOSM state; the trajectory edit is a small script | on the deployed buffalo: a `Buffalo_Pounce` state on its retimed run with a `Hips` arc, `UseRotation` and a return to idle, sent at 7 m: does it hop forward and run on |
| The strike on the frame | FCSE, optional | |
| Cat sounds | Far Cry 3's banks through a version 4 reader (new tooling), or the scratch WAVs and `spk new` today | shoot the stand-in near: the alert, then the death |
| Cat textures | data: stripped DDS in Far Cry 2 headers; the leopard's mask channel moved | the archetype with the mesh, in daylight |
| The lion mesh's UVs | art: a new unwrap and a bake from the cat maps | the same |
| The canter | accept; or the zebra with a size plugin; or a retargeter (new tooling) | none needed |
| A two-segment tail | accept, or a new rig | |

The spawn, grazing, hoof and stalking checks share one launch: `Animals.DLC.Lion` on the wildebeest
mesh with agent species 7 and the stalk brain, spawned 60 m from the player.

### Lion unknowns

- whether the occlusion coverage is computed for an animal, and whether grass or a screen edge counts;
- whether a skinned mesh with new topology works in game, as for the fish;
- whether the GOSM's unconditional connection leaves the pounce at the clip's end;
- whether a MOVE state `move assemble` appended loads, and its clip without a depload entry;
- whether a `Yes` freeze reads as stalking or as a glitch.

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
