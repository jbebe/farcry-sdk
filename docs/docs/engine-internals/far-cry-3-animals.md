---
sidebar_position: 27
---

# Animals in Far Cry 3

:::info[Verified via reverse engineering]
Read from retail Far Cry 3's `worlds\fc3_main\generated\entitylibrary_full.fcb`, its animal brains and
`common\generated\nomadobjecttemplates.fcb` (unpacked from `fc3_main.fat` with FCBConverter), and its
animal assets, `fc3_main_depload.xml` and sound banks (unpacked with Gibbed's Dunia 2 unpacker from
DuniaTools). Field descriptions and stim
types were read in `FC3.dll` (Ghidra program `/FarCry3-Retail`), which has no symbols. Claims marked
*(inferred)* were not read in data or code. Nothing here was seen in game.
:::

Far Cry 3's predators hunt and attack the player and each other. Every behaviour is assembled in data,
but the building blocks are C++ classes that [Far Cry 2's animals](./animals.md) do not have. This page
records how Far Cry 3 does it and what its assets are, for
[porting attacking animals](../design/attacking-animals.md).

## Brains are behaviour trees

Far Cry 3's `.ai.rml` is not [Far Cry 2's](../file-formats/ai-rml.md). It is a 16-byte header and an
`nbCF` FCB holding a compiled behaviour tree of `CNode*` nodes and static task data. There is no
readable source XML. Task and parameter names survive; fact names are only hashes.

The animal brains are `animaldefaultbrain`, `crocodilebrain`, `defensiveanimalbrain`, `snakebrain`,
`aquaticanimalbrain`, the squad brains, and the maneuver brains under `newbrains\animalsquadmaneuver\`.

## The animals

Land animals are `CAnimalAgentFC3`; fish are `CAquaticAnimalAgent`. The temper enum is 1 Predator,
2 Peaceful, 3 Friendly. The number after a name is its species value.

| Animal | Brain | Health | Temper | Damage | Mauling chance |
|---|---|---|---|---|---|
| `FC3_Quadrupeds/Tiger` (SumatranTiger, 41) | `animaldefaultbrain` | 800 | Predator | 40–70 | 50% |
| KomodoDragon (4) | `animaldefaultbrain` | 300 | Predator | 50–80 | 10% |
| Crocodile (1) | `crocodilebrain` | 200 | Predator | 20–50 | 100% |
| Cassowary (15) | `animaldefaultbrain` | 100 | Predator | 20–40 | mauling damage 0 |
| Buffalo (37) | `defensiveanimalbrain` | 700 | Peaceful, `bIsDefensive` | 35–60 | — |
| Hyena (21) | `animaldefaultbrain` | 100 | Predator | 10–40 | — |
| `SimpleAnimal/Snake` (23) | `snakebrain` | 2 | Predator | 100–120 | — |
| RiverFish (10) | `aquaticanimalbrain` | 1 | Peaceful | 0 | — |
| Coral_Reef_Fish (9, FishSwarm) | `aquaticanimalbrain` | 1 | Peaceful | 0 | — |
| Dogs_Dingo (20) | `defensiveanimalbrain` | 100 | — | — | — |

- **Tiger:** 120 kg, sprint 8, a jump attack from 3 m; mauling damage 30%, at most 50.
- **Komodo dragon:** sprints at only 4; each hit takes 34% of the player's health.
- **Crocodile:** biome `EarthAndWater`, speeds 0.53 to 1.57 m/s.
- **Cassowary:** a short-sight sensory template.
- **Buffalo:** attacks only when provoked.
- **Hyena:** a complete archetype with a handbook entry, but no assets; see
  [below](#the-hyena-ships-no-assets).
- **Snake:** a live agent; `_Stationary` variants also exist.
- **Dingo:** the only animal with `bSquadBehaviorPredator` and `bSquadBehaviorHunting` set.

The other predators are the leopard (health 450, mauling chance 100%), bull shark, great white, moray
and Asian black bear.

### The personality

Each animal's behaviour is tuned in an `SAnimalAgentPersonality` template, whose field descriptions
are read in `FC3.dll` at `0x10BBEA88`:

- detection radii (smell, stealth, immediate), sound distances and fear expiry
- wander, avoid and flee radii, and tracking trail points
- attack damage by distance, optionally a percentage of the player's health scaled by player level
- attack ranges: jump, critical, on-spot, side, and safe navmesh distances
- threat rating, broadcast radii, mauling fields and squad flags

## An attack from start to finish

1. **Detection** is a C++ sensory system (`AnimalSensorySystem` and `VisualSensorySystem` members,
   tuned by `aisensorysystem:*` templates). The tree reads it through `CTaskAnimalUpdateBestThreat` and
   `CTaskAnimalTrackPlayer`, which test field of view and distance.
2. **Stalking** is tree wiring over `CTaskStartTrailingTarget`, `CTaskGetNextTargetTrailPos`,
   `CTaskBTAnimalPathFollowWithTrack`, `CTaskBTPathFind` and `CTaskBTAnimalFindDestination`.
3. **The attack decision** is `CTaskUpdateAnimalAttackInfo`, in C++. It outputs `outCanCauseDamage`,
   `outIsJumpAttackSafe`, `outIsCloseAttackSafe`, `outPerformLowAttack` and `outCanQTECauseDamage`.
4. **The attack animations** are MOVE data in `common\graphics\move\movemgrnamed.bin`, such as
   `Animal_Attack_Jump`, `Animal_Attack_Side`, `tiger_fulb_closeattack_success` and
   `buffalo_fulb_attackbutt_*`.
5. **Damage** from a normal hit is applied in C++ from the personality's minimum and maximum, with the
   `CauseDamageOnPlayersAndNpcs` blood effect *(inferred)*.

### Mauling

The pinned "mauled" sequence is a paired quick-time event, run from the tree by
`CTaskAnimalQTEEnableOperation`, `CTaskAnimalQTEConditionOp`, `CTaskAnimalPlayQTE` and `CTaskPlayQTE`,
and `CTaskBTCheckGameAgentQTEStateInfo`. Paired player-and-animal clips exist for the crocodile
(`croc_jason_qte_bite`: start, loop, success, fail), tiger (`jason_stiger_bite_qte_*`), komodo
(`komodo_mini_qte_*`), snake (`snake_mini_qte_*`), rabid dog (`rdog_mini_qte_*`), shark, bear and
leopard. The crocodile and snake brains name their move files; the default brain names none, so code
probably picks the clips per species *(inferred)*.

If the player loses, the tree itself kills them with `CBTTaskSendEntityStim`: Cut at 1000 or 9999, or
Pierce at 10000.

### Animal task classes

All of these are C++:

- `CTaskAnimal*`: `StateOperation`, `SubStateOperation`, `StanceOperation`, `TemperOperation`,
  `UpdateBestThreat`, `TrackPlayer`, `UpdateFocusPosition`, `SetHeadFocusPriority`, `QTEEnable`,
  `QTECondition`, `PlayQTE`, `AnchorOperation`
- `CTaskUpdateAnimalAttackInfo`, `CTaskUpdateAnimalFleeInfo`, `CTaskCheckAnimalSpecies`
- `CTaskBTAnimal*`: `PathFollowWithTrack`, `PathFollowHelperOp`, `FindDestination`, `HealthOperation`,
  `ExecuteManeuver`, `RemoveFromSquad`, `FactBroadcast`, `GetSquadInfo`, `CrossNavMeshInfo`
- `CTaskBTAquaticAnimalGetOceanLevel`, `CTaskBTAquaticAnimalPathFollow`, `CTaskBTNoNavMeshMovement`,
  `CTaskBTForceRagdoll`

## Packs

A `CAnimalSquad` agent (`FC3_Squad/Predator_Default`, at most 10 members) runs a squad tree that picks
members and starts maneuvers: Tracking, Regroup, Dismiss and Assault (`CAnimalSquadManeuverAssault`).
Assault's data: `FormationRadius` 9, `MaxNumberAggressiveMembers` 1, `RetreatCasualtyPercentage` 0.5,
chase distances 18 and 15 m. One animal attacks while the rest circle. The maneuver classes are C++;
the behaviour is tree data.

## Water and fish

- **The crocodile** is a land agent (`EarthAndWater`) with a small brain of 126 nodes and no
  pathfinding. It tracks the player's field of view and distance, then lunges into a bite that always
  mauls: an ambush in place, not swimming navigation *(inferred from the task list)*.
- **Fish and sharks** are `CAquaticAnimalAgent`s that steer without a navmesh (`fAquaticNavMinDepth`,
  `GetOceanLevel`).
- `CTaskBTNavmeshOp` has a `WaterLayer` flag: "use the navmesh water layer".

Fish come in three kinds:

- `RiverFish`: individual `CAquaticAnimalAgent`s.
- `Coral_Reef_Fish`: species FishSwarm, one agent animated as a group (`crfish_group_swim`).
- `Winged/Fish_FP`: a pure `CBoidsComponent` flock of river-fish meshes with no AI, placed at
  least 85 times; compared field by field with Far Cry 2's birds in
  [Flocks](./flocks.md#far-cry-3s-fish-flock).

## Prey, corpses and fear

- **Predators kill prey:** `bSenseOtherAnimals`, `fPredatorOnSpotAttackPreyDistance`,
  `fKillAnimalCoolDownTime`, species checks in the tree, MOVE `Animal_Attack_Animal` and
  `leopard_fulb_leopardattackgoat`.
- **They eat corpses:** the `animal_eat_corpse` group and the `Animal_Eat_Corpse/Tiger` and
  `Hyena_EatCorpse` contextual actions. No throwable bait was found *(inferred)*.
- **Fear:** `Fire_FearStim`, `bIgnorFearStim`, `fFearEventExpiredTime` ("scared by vehicles, fire"),
  explosion and threatening-sound distances, and flee radii.

## What Far Cry 2 lacks

Searched by string in Far Cry 2's `Dunia.dll`. Far Cry 2 has `CAnimalAgent`, `CAnimalPersonality`,
`CAnimalSteeringEngine`, `CTaskAnimalPathFollow`, the threaten and run-away tasks, and
`CBoidsComponent`. It has none of:

- the behaviour-tree runtime
- the `CTaskAnimal*` attack, mauling and squad tasks
- `CAnimalSquad` and its maneuvers
- `CAquaticAnimalAgent` and movement without a navmesh
- a navmesh water layer
- paired quick-time events on the player

| Piece | Far Cry 3 data | Far Cry 3 C++ |
|---|---|---|
| Tuning (ranges, damage, detection) | personality template, archetype | the template reader |
| Detection and threat | sensory templates | sensory system, `UpdateBestThreat` |
| Stalking | tree wiring | trail and path-follow tasks |
| Attack choice | tree, ranges | `UpdateAnimalAttackInfo` |
| Attack animations | MOVE graph, `.mab` | MOVE runtime |
| Hit damage | minimum, maximum, percentage | damage application *(inferred)* |
| Mauling | paired clips, tree, kill stim | the QTE tasks and the player's QTE system |
| Packs | squad archetype, maneuver trees | `CAnimalSquad` and the maneuver classes |
| Fish | boids parameters, aquatic archetypes | boids, `CAquaticAnimalAgent` |
| Prey, corpses, fear | groups, contextual actions, stims | species and stim handling |

## The assets

Almost every animal lives in `fc3_main`'s subfats, so a dump that skips them misses the animals. `Gibbed.Dunia2.Unpack.exe --us -f animals` (in DuniaTools'
`bin\fc3_dunia`) unpacks 1,575 files. Meshes are under
`graphics\__fc3_graphics\_common\characters\animals\<animal>\`, clips under
`graphics\characters\_common\animations\animals\<animal>\`, and materials, named by id, in
`graphics\_materials`.

| Animal | Mesh `.xbg` (bytes) | Bones | Ragdoll `.hkx` (bytes) | Clips |
|---|---|---|---|---|
| sumatran_tiger | 385,764 | 57 | 39,836 | 91 |
| leopard_01, _02 | 336,244 | 47 | 35,420 | 80 |
| cassowary | 267,508 | 51 | 28,988 | 63 |
| crocodile (walk, jog, swim, kills) | 351,764 | 30 | 32,876 | 36 |
| komododragon01 | 164,504 | 35 | 35,052 | 68 |
| water_buffalo (also `_bh`, `_nb`; has attack clips) | 223,476 | 54 | 39,516 | 60 |
| rabid_dog rdog_01, _02 | 279,504 | 41 | 37,276 | 68 |
| thylacine | 386,824 | 41 | 35,356 | 58 |
| snake_01 | 79,648 | 32 | 17,612 | 20 |
| river_fish01 to 04 | 16 to 101 KB | 7 | river_fish01 only | 8 |
| coral-reef fish school, one skinned object | 393 to 555 KB | 125 | — | 15 group clips |

The tiger's normal and mask textures are named in its material but were not among the unpacked files.

The tiger, buffalo, komodo and crocodile rigs use the same bone names as Far Cry 2's 30-bone
quadruped (`Hips`, `Spine`, `Neck`, `Neck1`, `Head`, `Jaw`, `LeftArm`, …) plus extra bones.

### Formats against Far Cry 2's

JackAll reads none of these Far Cry 3 versions.

| Format | Far Cry 2 | Far Cry 3 | Difference |
|---|---|---|---|
| [`.xbg`](../file-formats/xbm-xbg.md) mesh | 42.6 | 52.3 | same chunk set plus a skeleton-reference chunk; the skin-descriptor chunk is 52 bytes, not 36. Gibbed's Dunia 2 geometry library has the chunk classes. |
| [`.skeleton`](../file-formats/skeleton.md) | file 18, object 7 | file 27, object 10 | same name hashing, different bone layout |
| [`.mab`](../file-formats/mab.md) | `0x4C` | `0x61` | layout unknown |
| ragdoll [`.hkx`](../file-formats/hkx.md) | Havok 5.5.0-r1, packfile 5 | Havok 2010.2.0-r1, packfile 8 (Skyrim's) | Skyrim tools such as hkxcmd should dump it *(inferred, not run)* |
| material | `.xbm` | `MAT\0` + FCB, shader `Skin_Advanced` | |
| [`.xbt`](../file-formats/xbt.md) | header 11 | 104: a 136-byte header, then a plain DDS | same mip-0 split |
| [`.sbao`](../file-formats/sbao.md) | | Ogg Vorbis after a 40-byte header | `jackall-cli sbao extract` read one: 32 kHz stereo |
| [`.spk`](../file-formats/spk.md) | | version 4 (`04 4B 50 53`) | 539 of 978 banks embed Ogg; the other 439 use an unknown codec |

### The hyena ships no assets

`FC3_Quadrupeds/Hyena` names `characters\animals\hyena\hyena.xml`, `hips_ref.skeleton`,
`hyena_ragdoll.hkx` and `.hkr`, and a graphic kit; a path list from the earlier unpack adds `hyena.xbg`
and textures `hyena_d`, `_n`, `_m` and `_eye_d`. None of them is in any Far Cry 3 archive. Every `.fat` but the sound ones was unpacked with those paths added
to the unpacker's name list, and only the handbook page `m_hyena.feu` and its picture `tx_hyena.xbt`
came out. As a control, the tiger's `hips_ref.skeleton`, which is absent from the stock list too, came
out the same way.

The rest agrees: `fc3_main_depload.xml` has no hyena entry of any kind, so no clip or bank. The
archetype's ten voice fields (idle, hurt, death, aware, tracking, tracing, attack, flee, panic,
unreachable) are unset; of its other sound fields only the bite impact is set
(`sndMeleeHitSoundIDBite` `0x001B024A`, type 12). Its personality, `aipersonality:animalpersonality.hyena`, gives damage 10 to 40,
an attack-safe distance of 2.65 m and broadcast radii of 20 to 25 m, and names no sound.

### Animal sounds

Sounds hang off the clips, as in Far Cry 2: `fc3_main_depload.xml` lists each clip's banks under it.
The banks are `soundbinary\<id>.spk` inside `fc3_main`'s subfats, not in `sound.dat`. The dog family's:

| Bank | Clips |
|---|---|
| `001698d6` | rabid dog alert cycles |
| `00195b38` | thylacine alerts |
| `0016dcc3`, `001aa476` | rabid dog attacks and attack turns |
| `001ae3df` | 73 rabid dog clips: attacks, hunting and more |
| `001ae484` | rabid dog alert-to-sprint |
| `001ae4c1` | rabid dog and thylacine deaths |
| `001aa45c` | rabid dog howl |

None of the 22 dog and thylacine banks embeds Ogg. Their audio is a 4-bit ADPCM-like stream that is
not plain IMA-ADPCM: decoded as IMA it is far smoother than noise but drifts to clipping, where a Far
Cry 2 IMA bank decodes cleanly *(inferred from that test)*. Reading them needs a v4 bank parser and
that codec.
