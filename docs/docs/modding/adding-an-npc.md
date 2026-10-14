---
sidebar_position: 8.8
---

# Adding an NPC

How to put a new person in the single-player world: someone standing still, a soldier at a post, a
soldier on patrol, and a named character who talks. Also how sides and hostility are set, and
whether an NPC can be friendly, neutral or a vendor.

Two test layers that each place one NPC are in [`mods/npc-example`](https://github.com/jbebe/farcry-sdk/tree/main/mods/npc-example).
They are **built but not deployed**; [testing them](#testing-the-example-layers) is one build and one
launch each.

Marks: **(proven)** seen in a running game; **(RE-verified)** read in `FarCry2_server`'s code;
**(seen in data)** read in the retail files; **(inferred)**; **(untested)**. Nothing on this page has
been run in game yet. Counts are over `world1` and the 250 sectors of `w1_b_2`.

## What an NPC is made of

An NPC is an entity of class `CPawnEntity` spawned from an archetype in the world's
[entity library](../engine-internals/entity-instancing.md). The soldier archetype
`enemy_archetypes.Blue_Faction.Assault_Caucasian` carries **(seen in data)**:

| Part | Where | What it sets |
|---|---|---|
| Body and outfit | `CGraphicKitComponent`, `CFileDescriptorComponent` (the kit descriptor), `CGraphicComponent` | the `merc_kit.xbg` wardrobe; with `bRadomize` set, every outfit slot is re-rolled at spawn, so a placed instance's own part list is ignored **(RE-verified)** |
| Movement | `CCharacterPhysComponent`, `CAnimationComponent` | physics capsule, `fMaxSlope` 60 |
| The pawn | `CPawn`, with an `Inventory` object | weapon pack: `assault` here; packs are in `gamemodesconfig.xml` |
| AI | `CFCXAIComponent`, `Type = CPawnAgent` | brain `::MercBrain/MercBrain` in `scripts\game\newbrains\mercbrain.ai.rml`; senses, shooting; see [AI](../engine-internals/ai.md) |
| Side | `CPawnAgent.selArmy` | `0 BlueGuys`, `1 RedGuys`, `2 GreyGuys`, `3 NeutralGuys` |
| Identity | `CPawnAgent.selSpecialCharacterType` | `None` for soldiers; one of 51 named characters for everyone the story names, from `Player` and the buddies to the arms dealers and `Arms_Bazaar_Tout` |
| Role | `CPawnAgent.selODU` | `Grunt`, `Medic`, `Boss`, `Engineer`, `Sniper` |
| Health and hits | `CFCXCountersComponentAI` | |
| Saving | `CPersistComponent.selLevel` | 1 for soldiers: [not saved](../engine-internals/ai.md#garrisons-and-patrols) |
| Crowd budget | `DensityManagement` in the AI object | `bNeverDelete`, `bLastToBeDeleted` |

How these come alive at spawn, including how the brain resolves and when the agent registers with
the AI engine, is in [from placed data to a live object](../engine-internals/data-to-live-object.md#stage-6-npcs-get-an-agent-and-a-brain).

### The families retail ships

| Archetype | Brain | `selArmy` | Special type | Persist | Outfit re-rolled |
|---|---|---|---|---|---|
| `enemy_archetypes.Blue_Faction.*` | MercBrain | 0 | None | 1 | yes |
| `enemy_archetypes.Red_Faction.*` | MercBrain | 1 (`bOppositeArmy` True) | None | 1 | yes |
| `buddies.Civilians.Male_Civilian`, `Female_Civilian_NoDress` | SpecialCharacter | 3, Neutral | 33 | 2 | yes |
| `buddies.ArmsMerchants.World1_ArmsMerchant` | SpecialCharacter | 3, Neutral | 30 | 3 | no |
| `buddies.GRIN.GRIN_Operative` (the Underground) | SpecialCharacter | 2, Grey | 32 | 3 | |
| `buddies.MissionSpecific.UFLL_Functionary` | SpecialCharacter | 0 | 39 | 3 | |
| `enemy_archetypes.Missions.Assassination_Target` | SpecialCharacter | | | 2 | |

All **(seen in data)**. Soldiers fight; everyone on the special-character brain is a character the
scripts drive.

### What a placed one looks like

A placed soldier in a sector file is small. The retail guard `Blue_Faction.Assault_Caucasian_41`
(`worldsector4027`, layer `main`) carries **(seen in data)**:

- `tplCreatureType`, `hidName`, `disEntityId`, `hidPos`, `hidAngles`, `hidPos_precise`
- `CGraphicKitComponent` and `CGraphicComponent` with the baked outfit and ambient light; re-rolled
  anyway
- `CCharacterPhysComponent` with `fMaxSlope`
- **`CFCXAIComponent/AIObject/CPawnAgent/selArmy = 1`**, which overrides the archetype's side
- an empty `CEventComponent`

**The side is chosen per instance.** All 74 soldiers placed in `w1_b_2` set `selArmy` 1 on the
instance, whatever their archetype's faction name says. The archetype decides the look and the
loadout **(seen in data)**.

Whether a bare instance with no graphic or physics component spawns as well is not established
([unknowns](../engine-internals/entity-instancing.md#unknowns)). The example layers copy retail
instances whole to stay clear of that question.

## Where soldiers come from, and how many

**Every post's guards are placed by hand.** Soldiers are ordinary placed entities: 74 in `w1_b_2`,
709 in all of `world1` **(seen in data)**. Nothing generates a garrison from a "post" record. The
rules around them **(RE-verified**, in [AI](../engine-internals/ai.md#garrisons-and-patrols)**)**:

- **Respawn.** A soldier at persist level 1 is not saved. A killed one is remembered as deleted in
  its sector until that sector's `main` layer unloads, and then the full garrison comes back on the
  next load.
- **Reinforcements** come from `CReinforcementPoint`s, called in by a `CGameRegion` of type
  `ReinforcementRegion`. Such a region has an army, `iMercDensity` and `iMercDensityThreshold` (8–15
  and 5–10 in `world1`), and a budget.
- **Density.** `CZoneLogicRegion` scales density per platform (`DensityScalePC`, 1 or 1.2), and an
  agent's `DensityManagement` flags protect it from being removed when the area is over budget
  **(inferred** from the names**)**.

**Mission soldiers** are placed in mission layers and appear when the layer turns on: 24 in `w1_b_2`
for library mission `a1lm02`, 12 for convoys, 11 for an Underground job **(seen in data)**. See
[mission layers](./authoring-a-mission.md#mission-layers).

## What a soldier does at his post

The brain picks its activity from **smart-terrain points** nearby. These are entities of the
`STP_archetypes.*` archetypes (`Duty.Guard`, `Rest.Smoke`, `Rest.SitOnGround`, `Duty.CheckMap`,
`Duty.MountedWeaponSmartTerrain` and more). Each has a `CFCXAIComponent` whose `CSmartTerrain` object
names an animation (`Action.mmsAction = pawn_st_duty_guard1`) and a `fBroadcastRange` of 10–60 m
**(seen in data)**. A soldier within range of a point can take it up **(inferred)**. The retail
assassination target and guard 41 both stand among a dozen of them.

- **Guard posts** are simply soldiers placed among `Duty.*` and `Rest.*` points.
- **Defensive positions** are `CStrategicPoint`s with an army, a radius and `nMaxDefensive`, of types
  `Defensive`, `Zone`, `Protection`, `ClashPoint` and others **(seen in data)**.
- **Foot patrols** are a smart-terrain point of archetype **`STP_archetypes.Duty.Patrol`** whose
  `Action/Patrol/entPathName` holds the id of an **AI path**. The path is a `CBasicShapeEntity` of
  points (`AIPathShape_*`, `Shanty_Pat`, `ChemDepot_Pat`; 212 in `world1`'s mapsdata), and a soldier
  who takes the point walks it **(seen in data;** the walking is **inferred)**.
- **Vehicle patrols** are ghost-patrol entities in mapsdata, `GhostPatrols.*` archetypes, whose
  `Ghost/entPathToFollow` names a path ([AI](../engine-internals/ai.md#garrisons-and-patrols))
  **(seen in data)**.

## Sides and hostility

**Armies.** A pawn's `selArmy` is `BlueGuys` 0, `RedGuys` 1, `GreyGuys` 2 or `NeutralGuys` 3. Regions
and strategic points number theirs differently: `Red` 0, `Blue` 1, `Grey` 2, `Neutral` 3. Check
which enum a field uses before copying a number **(seen in data)**.

**What is known about who attacks whom:**

- A soldier ignores stimuli from his own army unless he is in the social state
  **(RE-verified;** [hearing](../engine-internals/ai.md#hearing)**)**.
- Soldiers of both factions attack the player; civilians, vendors and the Underground do not.
  Civilians, the arms dealer and the Underground are on the special-character brain, which has no
  combat plan of a soldier's kind **(inferred** from the brains**)**.
- **Social regions** set local rules of engagement. A `CSocialRegion` has `selSocialType` `FreeFire`,
  `Discretionary`, `Weapon-Safe`, `TrespassA/B/C` or `Security`. Pala's town is `Town_DFZ`, of type
  `Discretionary` **(seen in data)**. Inside one, soldiers start in the social state and use their
  `SocialMechanic` values: they stare for 6 s, react to a weapon aimed at them within 1.5 s, and charge
  within 15 m **(seen in data;** the behaviour is **inferred** from the names**)**.
- How the engine decides that Blue and Red soldiers do not fight each other, and what
  `bOppositeArmy` changes, was not traced. The squad code (`CArmy`, `CDispatcherSquadLieutenant`) is
  where to look.

**Can an NPC be friendly, neutral or a vendor?**

| Wanted | How | Status |
|---|---|---|
| Neutral, ignores the player | a civilian archetype: Neutral, special-character brain | retail does it; the example layer places one **(untested)** |
| Friendly, fights beside the player | buddies do this: `CFCXCountersComponentAIBuddy` and the buddy brain (`buddyworkspace.ai.rml`), managed by the buddy system | a free-standing friendly soldier is **not established** |
| A soldier who won't shoot | a soldier inside a `Weapon-Safe` social region, until provoked | **(inferred)** |
| Vendor | the arms dealer pawn (Neutral, special type 30) plus a `CBazaarComputer` entity; the computer opens the shop by itself ([interactive objects](./interactive-objects.md#retail-interactions)) | **(seen in data)** |

## Recipes

### 1. A person standing somewhere

Place a civilian archetype instance in a sector's `main` layer: `tplCreatureType
buddies.Civilians.Male_Civilian`, a new `disEntityId`, a position on walkable ground. With no
graph driving him, a special-character pawn idles where he stands **(inferred)**.

**Built:** `mods/npc-example/layer`, a copy of retail `Civilians.Male_Civilian_0` 1.5 m east of
where that one stands (sector 3944, `w1_b_2`), minus its mission component.

### 2. A soldier at a post

Place a soldier archetype instance among existing smart-terrain points, and set the side on the
instance: `CFCXAIComponent/AIObject/CPawnAgent/selArmy`. He refills after his sector reloads, like
every retail guard.

**Built:** `mods/npc-example/layer-merc`, a copy of retail guard 41 with `selArmy` 1, 2 m beside him
(sector 4027).

### 3. A soldier on patrol

Recipe 2, plus a patrol point near him:

1. An AI path: a `CBasicShapeEntity` with `hidShapePoints`, in mapsdata or the sector. Copy an
   `AIPathShape_*`.
2. An `STP_archetypes.Duty.Patrol` entity within its broadcast range of the soldier, with
   `Action/Patrol/entPathName` set to the path's id. Copy `Duty.Patrol_2` from `worldsector4028`.

Tool: JackAll Map tab, or hand-written fragments in the shape of the example layers. **(untested)**

### 4. A named character who talks

- **Text, no voice:** a civilian (recipe 1) plus a Domino graph: `PawnInteraction` on him for the use
  prompt, then a `PopUpTutorialMessageBox` or a confirmation box with your own `oasisstrings` lines.
  It is the voiceless giver of [the quest page](./authoring-a-quest.md#route-b-your-own-giver-with-text-instead-of-voice).
  **(untested)**
- **Voice:** blocked. Bark banks are numbered from the speaker's special-character type and a mission
  tag, both fixed engine tables ([blockers](./authoring-a-quest.md#blockers)). A new named
  character's voice needs an FCSE hook. Placing an existing named archetype brings that character's
  own generic lines only **(inferred)**.

### 5. A vendor

The arms dealer archetype standing next to a `CBazaarComputer`. What the shop sells is the weapon
bazaar's unlock list (the `WeaponBazaar` node, `GetWeaponBazaar():UnlockItem`). **(untested)**

## Testing the example layers

The GOG install currently carries FCSE, UFCP, DevTools and Vehicle Overhaul. `mod build` replaces the
game's `patch.dat` with vanilla plus the layers named, so name Vehicle Overhaul's too. From the
repository root:

```
tools\JackAll\src\JackAll.Cli\bin\Release\net10.0\jackall-cli.exe mod build --game "C:\Games\Far Cry 2" --layer mods\vehicle-overhaul\layer --layer mods\npc-example\layer --layer mods\npc-example\layer-merc
```

Then launch `"C:\Games\Far Cry 2\bin\FCSE.exe"`, continue a world-1 save, and go to `w1_b_2`:

1. **The civilian**, at about x 1563, y 3196: a man in civilian clothes standing there, the same
   every visit since civilians are saved.
   - *Nothing there:* the minimal instance does not spawn, or `main` did not take it.
   - *A man in a T-pose or underground:* the position or the kit.
2. **The soldier**, at about x 1771, y 3251, an outpost about 430 m east of `SafeHouse_15`:
   one guard more than usual, standing beside guard 41, taking up a duty animation.
   - He should fight you exactly as guard 41 does.
   - Kill him, leave the area and come back: he should be there again.

Restore with Vehicle Overhaul's layer alone:

```
tools\JackAll\src\JackAll.Cli\bin\Release\net10.0\jackall-cli.exe mod build --game "C:\Games\Far Cry 2" --layer mods\vehicle-overhaul\layer
```

Both layers pass `jackall-cli mod lint` against the GOG install.

## Gaps and what would close them

| Gap | What closes it |
|---|---|
| Whether the example civilian and soldier spawn at all | the test above |
| The fewest fields an instance needs | strip the example civilian one component at a time, one launch each |
| How a soldier picks a smart-terrain point, and whether broadcast range is the rule | trace `UseSTP` and the smart-terrain broadcast in the brain; or test with recipe 3 |
| Why Blue and Red soldiers don't fight each other, and what `bOppositeArmy` does | trace target selection in `CArmy` and `CDispatcherSquadLieutenant` |
| A free-standing friendly soldier | test a soldier with `selArmy` 3, or a buddy archetype placed without the buddy system |
| What a social region of each type does to a placed soldier | place recipe 2 inside `Town_DFZ`, then the same with `Weapon-Safe` |
| A new character's voice | the FCSE bark-table hook in [blockers](./authoring-a-quest.md#blockers) |
| A placed NPC in a world without navmesh | [world loading](../engine-internals/world-loading.md#what-is-not-established): AI there crashes the game; not a concern in `world1` and `world2` |
