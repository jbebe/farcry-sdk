---
sidebar_position: 3
---

# Attacking Animals

**Status:** feasibility checked 2026-10-19 from data and code; the buffalo charge built in
`mods/wildlife-overhaul` the same day; [the hyena](#the-hyena), [fish shoals](#fish-shoals) and
[the ostrich](#the-ostrich) researched as new archetypes the same day; nothing seen in game. The behaviour cannot be copied from Far
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
| fish packs | `Fish_FP`, Far Cry 3's own fish flock; river fish (7 bones) | medium | the bird rig instead of a fish rig, a night-climb plugin, a skinning fix in BlenderFC2; [details](#fish-shoals) |
| snake | the snake (32 bones) | high | a new rig, so the rig exporter |
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

**B: works, but borrowed animation shows**

- **Lion.** A new mesh on the buffalo or zebra rig. A horse's gallop under a big cat will be noticed. A
  pounce can be keyed by hand into a copied bank.
- **Ostrich.** A chicken clone with an ostrich mesh, scaled up whole by the engine's per-animal size
  factor, which a small plugin sets. Lengthened leg bones do not work: the clips fix the hips at
  chicken height. It inherits all seven chicken clips, and run-jump is already a two-footed forward
  strike. Animating as a chicken (5) while behaving as a zebra (2) gives it 25 m sight instead of a
  chicken's 3 to 5 m. See [the ostrich](#the-ostrich).
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
   `m_flStartTime` and `m_flStopTime` may trim the clip to the leap *(inferred from the names; every
   retail value is 0)*. Start the leap when the player is about 7.5 m ahead at a scale of 4. That the
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
- whether `m_flStartTime` and `m_flStopTime` play part of a clip;
- whether the brain's wait lands the hit on the strike frame.

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
