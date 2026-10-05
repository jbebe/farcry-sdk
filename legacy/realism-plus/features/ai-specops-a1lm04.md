---
title: A1LM04 special forces get their own kit and loadouts
kind: component
bundle: gameplay
status: located
systems: [ai, missions, weapons]
match:
  - "generated/entitylibrarypatchoverride.fcb/enemy_archetypes/special/*.xml#**"
  - "levels/w1_d_4/generated/worldsectors/worldsector1500.data.fcb/red_faction.sniper_nubian_0.*.xml#**"
  # outside this page's containers, claimed precisely: the three packs and the kit model
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/WeaponsService/Properties/InventoryPacks/Pack[specops*]"
  - "_hash/ef3c4043.xbg"
exclude:
  - "**#**/FOVMultipliers/*"
requires: []
verified: diff
---

# A1LM04 special forces get their own kit and loadouts

The "SpecOps" soldiers of library mission A1LM04 (Direct Spear, whose objectives are the SpecOps'
communication equipment and medicine truck) dress differently from ordinary mercenaries and carry
fixed weapons: AK-47 or PKM, SPAS-12, or a Dragunov, each with a flare gun and three grenades. One
sniper of the mission's base becomes one of them.

## How

- **Archetypes.** `enemy_archetypes.Special.SpecOps_Assault` and `SpecOps_Shotgun`, copied into
  `generated/entitylibrarypatchoverride.fcb` (they redeclare `worlds/world1`), whose placed copies
  stand in `w1_d_4` sectors 1501, 1502 and 1581:
  - `CFileDescriptorComponent/fileName` `graphics\characters\mercenaries\merc_kit.xml` ->
    `merc_kit_specops.xml` (a label; the embedded descriptor still loads `Merc_Kit.xbg`), and the
    embedded kit descriptor gains `<criteria tag="truespecops" criteria="exclude" />` on 29 parts of
    the shoes, shirt, pants, hat, glasses and gear slots;
  - specialisation tags `specialops` -> `caucasian` plus a new `truespecops`, so they draw from the
    white mercenary wardrobe minus those 29 parts; `SpecOps_Shotgun`'s `PartOverwrite` list is also
    rearranged;
  - inventory pack `assault` -> `specopsassault`, `shotgun` -> `specopsshotgun`.
- **The sniper.** `Red_Faction.Sniper_Nubian_0` in `w1_d_4` sector 1500 (mission layer
  `missions\librarymissions\a1lm04\misnbase`) gets the same kit as an instance override: a
  `CFileDescriptorComponent` naming `merc_kit_specops.xml`, tags `caucasian` and `truespecops`, a
  rewritten `PartOverwrite` list, its `CGraphicComponent` model cleared to the kit's, and a `CPawn`
  with pack `specopssniper`.
- **Packs** (`gamemodesconfig.xml`, `WeaponsService` inventory packs, outside this page's usual
  area): `specopsassault` (AK-47 75 % / PKM 25 %), `specopsshotgun` (SPAS-12), `specopssniper`
  (Dragunov); each with a Star .45 or Uzi sidearm (the sniper an Uzi), the merc flare gun and three
  M67 grenades. Unlike the vanilla packs they do not vary by progression level.
- **`_hash/ef3c4043.xbg`** is `graphics\characters\mercenaries\merc_kit_specops.xbg` by its name
  hash: a copy of `merc_kit.xbg` with 29 part mesh names broken (`P_MC_Hat_Safari01` ->
  `P_MC_Hat.Safari01`, hair, caps, goggles, backpack and so on), as many as the parts the criteria
  exclude, presumably the same ones. The kit descriptors here still load `Merc_Kit.xbg`, so nothing in the mod loads it; it
  looks like a first attempt the criteria replaced (inference). The smuggler kit of
  `patrols-convoy-smugglers` uses the same trick and does load its copy.

The sight change on the two archetypes belongs to `ai-stealth-precombat`.

## Uncertain

- What the kit criteria leave them wearing is not checked in game.
- Boggalog's guide says new inventory packs cannot be created
  ([patrols](../../../docs/docs/modding/guide/patrols.md#guide---how-to-create-new-drivergunner-enemy-types));
  the mod adds them anyway, with name hashes in the archetypes. Whether the engine finds a pack it
  did not ship is not traced.
