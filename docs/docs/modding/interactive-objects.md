---
sidebar_position: 8.7
---

# Interactive objects

How the player uses things in the world: doors, crates, cases, beds, save points, terminals, guns,
vehicles and the rest. The first half explains the mechanism and surveys every retail kind of
interaction. The second half gives three ways to make a new one, cheapest first, with a lever, a
radio and a readable note as examples.

Marks: **(proven)** seen in a running game; **(RE-verified)** read in `FarCry2_server`'s code;
**(seen in data)** read in the retail files; **(inferred)**; **(untested)**. Nothing on this page has
been run in game. Counts are over the 250 sectors of `w1_b_2`.

## How "use" works

1. **The player asks every entity in front of him.** `CPawnInteractionMonitor` (a component on the
   player) sends each candidate a `CEntityEventIsUsable` event. Every component on that entity sees
   it, and a component that wants to be used answers with "usable", a **usage string** and a
   position (`IsUsableEntity`, `0x08fe2ff0`; `GetUsableEntitiesInFrontOfPlayer`, `0x08fe31b0`)
   **(RE-verified)**.
2. **The usage string picks a HUD icon.** `CFCXMainHudUI::CheckInteraction` (`0x08a604d0`) hashes the
   string and asks the HUD's prompt manager for a prompt of that name. If there is none, it falls
   back to a default **(RE-verified)**. The prompts are the `<hudprompt>` entries in
   `ui\…\hud.mgb.desc`: `interact` (the hand), `switchweapon`, `watch`, `ratchet`, `ied`, `map`,
   `reload` and a few more **(seen in data)**. Retail usage strings such as `door`, `USE M249` and
   `Pick LandMine` match none of them, so they show the default, which is the hand **(inferred)**.
   **A prompt is an icon, never text.**
3. **Pressing use** sends the entity `CEntityEventOnUsed`, which the answering component handles
   **(RE-verified:** both event names are in each answerer's code**)**. The entity then raises a
   **`Use`** event that event links (`InputEvent = Use`) and Domino callbacks (`"Use"`) listen to
   **(seen in data)**.

Fifteen places in the engine call the event's `SetUsable` **(RE-verified)**:

- **Named by their own strings:** the ladder ("Climb Ladder"), the mounted weapon ("bound mounted
  weapon"), a climbing helper (`CheckClimb`, `TryClimb`), and the multiplayer flag ("Take flag").
- **Inside a known class's code**, between its constructors and destructors: `CUsableComponent`,
  `CDoor`, `CBedroll`, `CPickup` and its subclasses, `CBazaarComputer`,
  `CProximityTriggerComponent` and `CExplosive`.
- **By address only (inferred):** `CMagicCrate` and the vehicle.
- **The rest:** a generic setter, and one handler at `0x08fc4670` that was not identified.

## Retail interactions

| Interaction | Archetype example | Component that answers use | Prompt | What use does | Persist level | Saved fields | In `w1_b_2` |
|---|---|---|---|---|---|---|---|
| Door | `Interactive.InteractiveDoors.Urbain01_AnimatedDoor01` | `CDoor`, with `CTriggerComponent` and an animated `CCompoundPhysComponent` | `door` → hand | push or pull it open | 3 | `bEnabled`, `UsedOnce`, `LastDoorAction` | 8 |
| Medkit cabinet (health) | `OA_MissionPickups.MissionPickups.MedKit` | `CMedicStation` (an opening pickup) | hand | opens, refills syrettes | 1 | `bOpened`, `bActive` | 10 |
| Ammo crate / pile | `pile_archetypes.PickupPiles.AmmoPile` | `CPickupPile` | hand | refills ammo; also destructible | 1 | `Destroyed`, `bActive` | 2 |
| Loose ammo, syrette, water | `pickups.Ammo.Small_Ammo_Pickup`, `pickups.Health.Syrette` | `CPickupMultipleAmmo`, `CPickupAmmo`, `CPickupHealth` | hand | take it | 1 | `bActive` | |
| Weapon on the ground | `pickups.Weapons.AK47_new` | `CPickupWeapon` | `switchweapon` | swap weapons | 1 or 3 | ammo, `Reliability` | |
| Mine, pipe bomb | `weapons.Explosives.IED_Base.IED_Mine` | `CExplosive` | `Pick LandMine` → hand | take it | 2 | the explosive's state | |
| Diamond briefcase | `OA_MissionPickups.MissionPickups.DiamondBriefcase_LVL1` | `CPickupDiamond` (an opening pickup with a beeper) | hand | opens, pays `nDiamonds` | 3 | `bOpened`, `bActive` | 15 |
| Tape, courier pouch | `OA_MissionPickups.MissionPickups.CarverTape` | `CPickupMissionItem` | hand | take it; graphs listen with `PickupListener` | 3 or 1 | `bNeverPicked` | |
| Cell tower panel | `OA_MissionPickups.MissionPickups.CellTowerPanel` | `COpeningPickup` | hand | the phone contract ([mission page](./authoring-a-mission.md#worked-example-a-phone-assassination)) | 3 | `bOpened` | |
| Bedroll | `Interactive.Save.BedRoll` | `CBedroll` | `watch` | sleep and pass time | none | `bEnabled`, `savedTimeScale` | |
| Save point | `ProximityTrigger_11` (placed, no archetype) | `CProximityTriggerComponent` with `bUsable` | hand | its `Use` link sends `CMissionHandlerEvent._SaveGame` | 3 | `bEnabled`, `Usable` | 1 |
| Bus station | `ProximityTrigger_BusStation_W1B2toHub` | `CProximityTriggerComponent` with `bUsable` | hand | a Domino graph listens to `Use` | | | 4 |
| Weapon shop computer | `Interactive.Computer.BazaarComputer` | `CBazaarComputer` | hand | opens the shop menu | none | | |
| Safe-house weapon crate | `Interactive.MagicCrates.PrimaryCrate` | `CMagicCrate` | hand; `CantUseText` when not bought | take a weapon of the crate's category | 1 | | |
| Mounted gun | `weapons.MountedWeapons.M249_SandBags` | the mounted weapon | `USE M249` → hand | mount it | 1 | its implementation | 14 |
| Vehicle | `vehicle.Land.*` | the vehicle | hand | get in | 1 or 3 | driver, health, parts | |
| Ladder | `OA_Ladder.Ladders.LadderDogon14steps` | `CLadder` | hand | climb | 1 | `Users` | |
| Mission document | `props.Props.Folder_AnimReady` | `CUsableComponent` | hand | raises `Use`; a graph decides | 2 | `bEnabled` | |
| Explosive barrel | `OA_Explosives.Explosives.ExplosiveBarrel_NEW` | none | — | not usable: damage drives its `CCompoundPhysComponent` states, two of which explode | 1 | compound state | |
| Destructible prop | 261 archetypes with `CCompoundPhysComponent` | none | — | not usable: damage or events step a state machine | mostly 1 | compound state | |
| Radio | `object_archetypes.Appliances.BoomBoxRadio` | none; `CRadio` plays a broadcast list, the global `EnableRadioBroadcast(bool)` switches them | — | not usable; shooting breaks it | 1 | `bIsPlayingRadio` | |
| Malaria pills | no world pickup | — | — | pills come from scripts: `SetMalariaPillCount`, `GetMalariaPillCount` | | the player's `RemainingPills` | |

Archetypes, components, prompts, persist levels and counts are **(seen in data)**. "Saved fields"
are the members the [component registry](../engine-internals/entity-component-schema.md#flag-bits)
marks as written to savegames **(RE-verified)**.

### What persists

A field marked "saved" reaches the save only if the entity itself is saved. A world-placed entity is
saved at `CPersistComponent` level 2 and up, and at level 1 only when the persistence budget is -1
([garrisons](../engine-internals/ai.md#garrisons-and-patrols)) **(RE-verified)**. So:

- **Level 3 or 2 keeps its state:** opened diamond cases, doors, mission items, the save trigger, a
  used document.
- **Level 1 resets when its sector reloads:** medkit cabinets, ammo piles, loose ammo, mounted guns.
  This is why a cabinet is full again when you return **(inferred)**.
- **Mission-layer states** are saved separately, in the save's `MissionManager` list
  ([campaign state](../file-formats/savegame.md#campaign-state-outside-the-entities)) **(RE-verified)**,
  so anything a layer switch did survives a reload.

## Three ways to build a new one

### Way 1: a usable trigger and an event link (data only)

The retail save point is the template, and it needs no script. It is an invisible box entity with
three components **(seen in data)**:

```xml
<object type="CTriggerComponent"><value name="static" type="Bool">True</value></object>
<object type="CProximityTriggerComponent">
  <value name="bEnabled" type="Bool">True</value>
  <value name="vectorSize" type="Vector3"><x>1</x><y>1</y><z>1</z></value>
  <value name="bUsable" type="Bool">True</value>
  <value name="selUsableBy" type="UInt32">2</value>          <!-- PlayerOnly -->
  <value name="sUsageString" type="String"></value>          <!-- empty: the hand -->
  <object type="UseRestrictions">
    <value name="bLookAtCheck" type="Bool">True</value>
    <value name="fMaxDifLookAt" type="Float">0.3</value>
    <value name="bAngleCheck" type="Bool">False</value>
  </object>
</object>
<object type="CPersistComponent"><value name="selLevel" type="UInt32">3</value></object>
```

plus a `CEventComponent` whose link fires on **`InputEvent = Use`**. The visible object is a
separate prop placed in the same spot. What the link sends decides what the object does. These
event classes appear in retail links **(seen in data)**:

| Event class | `hidEventName` | Does | Good for |
|---|---|---|---|
| `CMissionHandlerEvent` | `_SaveGame` | saves the game | a save point |
| `CMissionHandlerEvent` | `_MissionSetState` | `Game:MissionSetState(missionName, state)`: switches a mission layer | a lever that opens a gate |
| `CMissionHandlerEvent` | `_SetTimeOfDay`, `_Game_PlayBinkVideo`, `_Game_PlaySequence`, `_SetWaterLevel` | the matching `Game` call | a clock, a TV, a sluice |
| `CSoundEvent` | `PlaySound` | plays `sndSound` on the target | a radio that plays a clip |
| `CLightEvent` | `ActivateLight`, `DeactivateLight` | switches a light | a light switch |
| `CCompoundPhysChangeStateEvent` | `ChangeState` | steps a compound object to its next state | a breakable that falls apart on use |
| `CCompoundPhysForceStateEvent` | `ForceState` | jumps a compound object to state `nIndex` | |
| `CNewParticlesSystemStartEvent` / `StopEvent` | `PlayParticles` / `StopParticles` | starts or stops an emitter | smoke, sparks |
| `CMapElementEvent` | `Available` | changes a map marker | reveal a place on the map |

**`CMissionHandlerEvent` runs Lua.** Its name `_X` calls `Mission:Event__X` in
`scripts\missiontools.lua`. That file is plain Lua, loaded by the engine's `LoadLuaScripts`
**(RE-verified** for the load**; seen in data** for the name rule: `_SaveGame` ↔
`Mission:Event__SaveGame`**)**. Its arguments come from the link's `Parameters`, a small binary-XML
blob. The retail train-station trigger carries:

```xml
<Parameters missionName="Missions/HubAiDensity/Capitol/TrainStation" state="1" />
```

`jackall-cli rml decode` reads such a blob and `rml encode` writes one. That attributes arrive in
the handler's argument order is **(inferred)** from the matching names (`missionName, state`).

Tools: the JackAll Map tab places the trigger, the prop and the link; the parameter blob is
hand-made with `jackall-cli rml`. Everything in this way is **(untested)** by this project, but every
piece of it ships in retail data.

### Way 2: your own handler in `MissionTools.lua`

Way 1 with a new event name. Add a function to `scripts\missiontools.lua`:

```lua
function Mission:Event__MyMod_ReadNote(title, text)
    CGameMessageBoxHelper_GetInstance():CreateTutorialMessageBox(title, text, "", Mission, "Event__MyMod_NoteClosed");
end
function Mission:Event__MyMod_NoteClosed()
end
```

and link `Use` → `CMissionHandlerEvent._MyMod_ReadNote` with
`<Parameters title="MYMOD_NOTE_TITLE" text="MYMOD_NOTE_TEXT" />`. The call is the one the
`PopUpTutorialMessageBox` node makes **(seen in data)**. Any Lua global works the same way:
`Game:AddDiamonds`, `SetMalariaPillCount`, `CGameMissionMgr_GetInstance()`. **(untested)**

The catch: `MissionTools.lua` ships whole, so two mods that both add handlers replace each other's
file. Keep handlers few and prefixed, or use way 3.

### Way 3: a Domino graph

For anything with state: an on/off toggle, a count, a second step. Two nodes listen for use:

- `ProximityTrigger` on a usable trigger: control-outs `Enter`, `Leave` and **`Use`**. 121 retail
  graphs use it, the bus stations among them **(seen in data)**.
- `UsableEntity` on any entity with a `CUsableComponent`: `StartListening`, then **`OnUse`**.
  `SetAsUsable` and `SetAsUnusable` send `CUsableComponent_Enable` and `_Disable`, and `SetSize` calls
  `SetUsableEntitySize`. No retail graph uses this node **(seen in data)**.

Keep the graph's state in `Globals` so it is saved
([script state](../engine-internals/domino-scripts.md#script-state-in-the-savegame)), and host the
graph as in [route C of the mission page](./authoring-a-mission.md#route-c-a-contract-outside-the-shipped-graph).

## Recipes

### A lever that opens a gate

Way 1, no script:

1. Put the gate (any prop; a checkpoint barrier or a wall section) in a new mission layer,
   `Missions/MyMod/GateClosed`, declared in `<world>.game.xml` with default state on. Tools: a JackAll
   `game.xml` mission fragment, and the Map tab with `_layout.xml` for the layer.
2. Place the lever prop, and a usable trigger around it copied from the save point.
3. Link the trigger's `Use` to `CMissionHandlerEvent._MissionSetState` with
   `<Parameters missionName="Missions/MyMod/GateClosed" state="0" />`.

The gate disappears when the lever is used, and stays gone after a reload because layer states are
saved. There is no animation: retail's gates and barriers have damage states, not open and closed
ones. An animated gate needs a compound object with an "open" state, which is new model work.
**(untested)**

### A radio you switch on

- **One-shot (way 1):** a usable trigger beside a radio prop, linked `Use` → `CSoundEvent.PlaySound`
  with your `sndSound`, targeting the radio entity. Copy the retail link's `sndtpType 11`,
  `bAssignOnEntity True` and `bInterruptible True`. It plays the clip each time it is used; it cannot
  switch off.
- **On and off (way 3):** `ProximityTrigger.Use` toggles a flag in `Globals` and either plays the
  sound with `PlaySound` or stops it. The retail `CRadio` cannot be driven per radio from data:
  it has no use handler, and its only switch, `EnableRadioBroadcast`, is global.

### A readable note

- **Way 2:** the `_MyMod_ReadNote` handler above, with the title and text as `oasisstrings` keys
  added through `oasisstrings.fragment.xml`.
- **Way 3:** `ProximityTrigger.Use` → a `PopUpTutorialMessageBox` node with `TitleLocId`,
  `TextLocId` and `ButtonLocId`.

The prompt is the hand. A "Read" label would need a new `<hudprompt>` with its own icon in
`hud.mgb` (a JackAll `.mgb` fragment) and that name in `sUsageString` **(untested)**.

### A pickup that gives malaria pills

Way 2: a handler that calls `SetMalariaPillCount(GetMalariaPillCount() + 1)` and hides the prop by
switching its layer off with `Game:MissionSetState` (or way 1's `_MissionSetState` on a second
link). **(untested)**

### A paper the player keeps

Copy the Predecessor's envelope, `MissionPickups.PartnerMissionEnvelope`, a `CPickupMissionItem`.
Give it another model in its descriptor and graphic component, and persist level 3 so that it stays
picked. `CPickupMissionItem::Pick` (Steam `0x106C8040`) runs the base pickup's `Pick` with a copy of
the picker, then clears `bActive` (`+0x15`) and `bNeverPicked` (`+0xD1`) **(RE-verified)**. When the
item has a string at `+0xC8`, it then hands something built from it to the picker's entity
**(inferred)**. What the player carries afterwards is up to a plugin. Inventory Overhaul hooks `Pick`
and gives the item named by its archetype's FCSE entity data (`mods/inventory-overhaul/notebook/papers.py`,
`src/pickups.cpp`). It is placed by a sector fragment in `main` **(untested in game)**.

## What needs an FCSE plugin

Data and Lua cover any use that **triggers something**. These need engine code:

| Want | Why data can't | Plugin approach |
|---|---|---|
| **A text prompt** ("Read note", "Turn on radio") | prompts are icons chosen by name; the HUD has no text field for them | write the text into a HUD element when `CheckInteraction` picks the entity |
| **Hold to use**, a progress bar, a use that takes time | the use step is one event | a component that answers `IsUsable` and runs its own timer |
| **The player's hands doing something** (a first-person animation on use, like doors and ladders) | those animations belong to the answering C++ components | a component that drives the player's animation, or reuse of an existing one |
| **Carrying or moving an object** | no retail component does it | new component |
| **Per-radio on/off of the retail `CRadio`** and its broadcast list | no event or Lua call reaches one radio | call `CRadio`'s methods from a plugin |
| **A new kind of pickup reward** with its own HUD feedback | pickups are fixed C++ classes | new component; or way 2 for the reward without the feedback |
| **Changing use range or angle for every object** | `CPawnInteractionMonitor`'s query is code | detour the monitor |
| **Per-object state richer than a layer or `Globals`** | | FCSE entity data ([persistence](./persistence.md#fcse-entity-data-per-entity)), already working |

A new answering component is the general plugin route. FCSE already registers a component class
with the engine's factory ([ABI](../engine-internals/fcse-entity-data-abi.md)); giving it an
`OnEvent` that answers `CEntityEventIsUsable` is the missing piece.

## Open questions

- Which `<hudprompt>` the fallback actually picks, and what the `IsUsable` answerer at
  `0x08fc4670` is.
- Whether a `CUsableComponent` entity's `Use` reaches event links as well as Domino callbacks. Retail
  data has no such link to copy.
- Whether `CMissionHandlerEvent` passes `Parameters` attributes by order or by name.
- What `Game:MissionSetState`'s `state` values mean beyond 0 and 1.
- What prop stands at the retail save trigger (1297.5, 3120.9) in `worldsector3860`.

## Cheapest next experiments

One launch each of the GOG install (`"C:\Games\Far Cry 2\bin\FCSE.exe"`), on a world-1 save, with the
console on `~`. Do not save afterwards.

1. **The handler path, no files.** Stand in `SafeHouse_15` (world 1, about x 1339, y 3230; see the
   [engine experiments](../engine-internals/data-to-live-object.md#1-switch-a-layer-from-the-console)) and
   type `#Mission:Event__MissionSetState("Missions/SafeHouse/Upgrades/W1/Level_06", 1)`. Expect the
   upgrade's medkit cabinet to appear. That proves `MissionTools.lua`'s handlers and the `state`
   value, which the lever recipe rests on.
2. **The note pop-up, no files.**
   `#_t={Event_Continue=function() end}; CGameMessageBoxHelper_GetInstance():CreateTutorialMessageBox("BSQ03_01_NBM_01","BSQ03_01","",_t,"Event_Continue")`.
   Expect a tutorial box titled "Destroy the ammo pile inside the Ranch House." with Flora's journal
   line as its text. That proves the readable-note call.
3. **A data-only lever.** One layer: a usable trigger copied from `ProximityTrigger_11` into
   `SafeHouse_15`'s sector 4020, linked `Use` → `_MissionSetState` with
   `<Parameters missionName="Missions/SafeHouse/Upgrades/W1/Level_06" state="1" />`. It needs no new
   layer, since it reuses the retail one. Expect the hand icon on the spot, and the cabinet appearing
   when you use it.
