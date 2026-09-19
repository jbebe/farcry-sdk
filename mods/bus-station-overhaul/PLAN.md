# Bus Stop Revamp — modules touched and build order

## Context

FC2's bus stops are an empty house, 2–3 buses, no people, and 4 map props on the ground. You use a map to fast-travel through a cheap fade and an engine sound. The revamp:
- a lively stop (NPCs, one good bus, the other buses removed);
- a ticket dialog at the cash desk that picks the destination;
- boarding a full bus with *use* to run the existing travel logic.

This touches most layers of the toolchain, and several pieces are untested. So it is built as stacked features, each playable on its own.

### What the game actually does today (read from `tmp/gamefiles/worlds/worlds/domino/user/fasttravel/fasttravel.fasttravel.debug.lua`)

- **One Domino graph, `FastTravel`, covers every stop.**
  - 5 stops: Hub, B2, B4, D2, D4.
  - 20 `ProximityTrigger` boxes, one per route (`Trigger_B2toD4`, …). These are the "maps on the ground".
  - 5 `SpawnPoint_*` entity ids and 5 `MapMarker_*` map icons.
- **Every route runs the same chain:**
  - `Use` → `PopUpConfirmationMessageBox` (`FTL_TITLE`/`FTL_MESSAGE`) → `Yes`
  - → `PawnInvincibleState` → `SetActionMap.Push` → `PostFx` fade → `PlaySound` → `TeleportEntity` to that spawn point
  - → shared tail: `PlaySound` → `SetTimeOfDay.IncrementTimeOfDay` → un-invincible → `PostFx` off / `SetActionMap.Pop` → `Delay` → `SaveAfterTeleport`.
- **The trigger variables start as `nil`.** They are bound from the placed Domino entity's parameters in world `.fcb` data, so graph wiring and world data have to change together.
- **The destination is fixed by which trigger you used.** The revamp keeps the teleport tail as it is and replaces the head: pick a destination at one place, then go by using another.
- **Game Lua cannot raise a list dialog.**
  - `CGameMessageBoxHelper` exposes only confirmation and tutorial boxes to Lua (`docs/docs/engine-internals/lua-api-surface.md:75`).
  - A real list (`CGameMessageBoxList`) can only be raised from C++ (`docs/docs/magma-ui/engine-interop.md:616-628`).

## Modules the mod touches

| # | Module / file type | What changes | Tool today | Gap |
|---|---|---|---|---|
| 1 | **World placement `.fcb`**: `worldsector<N>.data.fcb`, possibly `landmarknear/far_*` and `<world>.mapsdata.fcb` | Delete extra buses and the map props or triggers. Add the new bus, NPCs, cash-desk trigger, bus-door trigger. Rebind the FastTravel Domino entity's parameters | JackAll layer: per-entity `<id>.xml` adds, `_layout.xml` `<delete>` | No visual placement: coordinates are hand-typed from the read-only Map tab |
| 2 | **Entity library `.fcb`**: `entitylibrary.fcb`, `entitylibrarypatchoverride.fcb` | New static bus-prop archetype. NPC variants for idle civilians and passengers, derived from `Civilians.*` | JackAll per-archetype pieces | Whether patchoverride splits per entity is unconfirmed; it may need a whole-file override |
| 3 | **Domino user graph `.lua`**: `domino/user/fasttravel/fasttravel.fasttravel.lua` (+ `.debug.lua` twin for the viewer) | Rewrite the head of the graph (cash desk → select → bus → shared tail) | Whole-file replacement in the layer. `UserGraphWriter` round-trips but is unwired and cannot change wiring | Hand-authored generated-style Lua, or teach JackAll to write wiring. Lua override is community-disputed and must be proven first |
| 4 | **Domino system box `.lua`**: new `domino/system/*.lua` | A `PopUpDestinationList` box (select item → output pin / sets a variable) | Plain Lua file in the layer | Needs #5 underneath |
| 5 | **FCSE plugin (C++)**: new `mods/bus-stops/` | Register a game-Lua (4.1) global that raises `CGameMessageBoxList` with custom items and calls back into the box | FCSE Hook/Patch API. DevTools runs `#lua` for testing | **RE needed:** registering a C function in the game's Lua state. The list box recipe itself is documented |
| 6 | **Magma UI `.mgb`**: `common.mgb` message-box list layout | Optional: restyle it as a ticket window | JackAll `.mgb` round-trip editor | A plugin cannot load its own `.mgb` without copying FCSE internals |
| 7 | **Localization**: `oasisstrings.fragment.xml` per language | Destination names, "Buy ticket" / "Board bus" prompts, dialog title | JackAll fragment merge | None |
| 8 | **Art**: `.xbg` / `.xbt` / `.xbm` / `.hkx`, plus `*_depload.dat` entries | New bus model, via an existing asset or a Blender `.fc2model` edit, loaded in-world | BlenderFC2 + JackAll `fc2model`, `depload add` | `.hkx` collision can't be edited: the model keeps the donor's collision |
| 9 | **AI / animation data**: `.gosm`, AI reference points, faction | NPCs that idle, sit and don't flee or aggro. Passengers seated in the bus | Nothing specific | **Unknown:** ambient civilian behaviour, sit spots, seating NPCs in a vehicle |
| 10 | **Mission layers**: `<world>.game.xml\<Mission>.xml` | Only if the stop's contents should appear or change with story progress | JackAll mission pieces | None known |
| 11 | **JackAll (C#)**: tooling | Optional: Map tab "save placement" to per-entity XML. Optional: Domino wiring writer | — | Build only when hand-placement becomes the bottleneck |

## Build order (each step shippable and testable on its own)

**Step 0 — Recon (no mod output, docs only).**
- Locate a stop's entities in world1 via the Map tab or decoded sector XML:
  - buses, `hq_busstation`, the map props and triggers, spawn points;
  - the FastTravel Domino entity and its parameter bindings.
- Record which containers and mission layers they live in.
- Prove a Domino `.lua` override loads: change `FTL_MESSAGE`'s LocId, or add an extra `PlaySound`, and check it in game. This settles the disputed-override gotcha before anything is built on it.
- Write findings to `docs/docs/modding/` or `engine-internals/`.

**Feature 1 — Clean up (world `.fcb` only).**
- Remove the extra buses at one stop with `_layout.xml` deletes.
- Confirm in game: no floating collision, save/load clean.
- Roll out to all stops.

**Feature 2 — New bus (library `.fcb` + world `.fcb`, art optional).**
- Place one static bus per stop from an existing vehicle or prop asset, via a new prop archetype.
- The better model (#8) is a later drop-in swap of the same archetype.

**Feature 3 — New travel flow with stock UI (Domino graph + world `.fcb` + strings).**
- Cash-desk `ProximityTrigger` → chained yes/no boxes, one per destination ("Travel to X?" No → next) → store the choice in a graph variable.
- Bus-door trigger → `Use` → existing shared tail, teleporting to the chosen spawn point. If no ticket is bought: a tutorial box saying "buy a ticket first".
- Delete the map props and their 20 triggers.
- **This completes the full gameplay loop with no C++.** Everything after it is quality.

**Feature 4 — NPCs around the stop (library + world `.fcb`, AI research #9).**
- Hand-place friendly civilians at one stop. Check faction, idle behaviour, whether they react to gunfire, and whether they respawn.
- Add a ticket-seller NPC behind the desk.
- If hand-placing across all stops gets painful, do the **Map tab save** tooling (#11) here.

**Feature 5 — Passengers in the bus (research-heavy).**
- Try, in order:
  - (a) seat NPCs through a vehicle seat mechanism, if one exists (RE the seating code, or Lua `SpawnEntityFromArchetype` + an enter-vehicle call);
  - (b) posed static NPCs at seat positions, animation locked;
  - (c) passenger silhouettes baked into the bus model's art.
- Stop at the first that looks right.

**Feature 6 — Native destination list (FCSE plugin + new system box, optional `.mgb`).**
- RE how the game registers Lua globals, and expose `ShowDestinationList(items, callbackBox)` over the documented `CGameMessageBoxList` four-step recipe.
- Add the `PopUpDestinationList.lua` system box, and swap it in for Feature 3's chain.
- Optional: restyle the list in `common.mgb`.

**Feature 7 — Polish.**
- Departure sound or bus-engine loop, a longer fade, the correct map icon for the bus.
- Localized strings for all languages.
- Save-game compatibility check (trigger state `Usable` / `bEnabled` is saved).

**Feature 8 — Release.**
- `mods/bus-stops/` with `layer/` (+ `plugins/` once Feature 6 exists), `README.md`, CI and release workflows per `RELEASING.md`.

## Verification (per feature)

- Build the layer with `jackall-cli mod build` and install it to `C:\Games\Far Cry 2` (the deploy-every-change rule). The user launches the game and reports back; I never launch it.
- Test from a save at the stop. The DevTools console can script checks:
  - `#` Lua to teleport to a stop;
  - `-load` into the save.
- For each feature, check:
  - Feature 1: entities gone, no invisible collision.
  - Feature 3: every route (20) lands at the right spawn point, time advances, the save happens.
  - Feature 4: NPCs survive a save/reload and a firefight nearby.
  - Feature 6: the list shows localized names and cancel works.
- `npm run build` in `docs/` for any docs written along the way.

## Open decisions (can be settled at the step they matter)

- **Destination picker:** ship Feature 3's chained yes/no boxes as final, or also do the C++ list box (Feature 6)? The plan assumes chained boxes first and the list box later.
- **Bus model source:** an existing FC2 asset (to be identified in Step 0) or new or edited art.
- **Scope:** world1 only first, then world2. The graph is shared under `worlds/worlds/`, so both worlds' placement data changes.
