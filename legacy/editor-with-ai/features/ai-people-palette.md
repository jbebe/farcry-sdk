---
title: Soldiers, buddies and characters in the editor palette
kind: component
bundle: editor-war-ai
status: located
systems: [ai, buddies, ui]
match:
  - "ingameeditor/object_inventory.xml#Directory[+10]"
exclude: []
requires: [ai-editor-mode-services]
verified: diff
---

# Soldiers, buddies and characters in the editor palette

The map editor's object palette gains a "Utilities" folder of people. A map author can place APR and
UFLL soldiers of every class, the twelve buddies, faction leaders, mission characters and civilians
like any other object.

## How

`ingameeditor/object_inventory.xml`: one new top-level `<Directory Id="Directory_Utilities"
Display="Utilities" PcOnly="1">` (commented `Peoples`), added whole. Each `<Entry>` names an
archetype through `SourceName`. Every archetype it names is declared in the template world's
vanilla `entitylibrary.fcb`, the library the editor loads. The mod adds no characters of its own
here.

- **"Urban" (commented `War AI`)**, two sub-folders of 14 soldiers each:
  `enemy_archetypes.Red_Faction.*` and `enemy_archetypes.Blue_Faction.*`. Each folder has the roles
  `Assault`, `CarlGustaf`, `LightMachineGunner`, `MortarMan`, `RocketMan`, `ShotgunMan` and `Sniper`,
  each `_Caucasian` and `_Nubian`. All entries are `Display="ai"` with `ObjectCost="50"`. The folder
  also holds `enemy_archetypes.Special.SpecOps_Assault` and `SpecOps_Shotgun`, which are
  `ObjectCost="1"` and reuse the labels `Razor16`/`Razor37`.
- **Buddies:** `buddies.Buddies.<name>` and `<name>_Unarmed` for all twelve (Andre Hyppolite to
  Xianyong Bai), `ObjectCost="50"`.
- **Faction leaders:** the APR's Walton Purefoy, Prosper Kouassi, Oliver Tambossa, Nick Greaves and
  Arturo Quiepo, and the UFLL's Joaquin Carbonell, Leon Gakumba, Addi Mbantuwe, Anto Kankaras and
  Hector Voorhees. Each comes plain and as `.Armed`. The `.Armed` ones use the `warlord` inventory
  pack, which [`ai-armed-faction-leaders`](ai-armed-faction-leaders.md) arms with rifles.
- **Others:** `buddies.MissionSpecific.*` (UFLL functionary, XOD instructor, propaganda minister,
  radio DJ, chief gendarme, cab driver, the chief's brother, former king, Prince Oeduard, border
  commandant and guard, APR courier, a generic scripted soldier, listed twice). It also has
  `enemy_archetypes.Missions.Assassination_Target`, the civilians `buddies.Civilians.Male_Civilian`
  and `Female_Civilian_WithDress`/`_NoDress`, `buddies.Jackal.Jackal` and
  `Jackal_SpecialCharacter`, `buddies.Journalist.Reuben_Oluwagembi`, `buddies.GRIN.*` (operative,
  Father Maliya, Doctor Obua) and `buddies.ArmsMerchants.World2_ArmsMerchant`.

`ObjectCost` counts against the map's object budget.

Placed alone, without [`ai-editor-mode-services`](ai-editor-mode-services.md), these are pawns in
a game mode with no single-player AI services, which is why that page is required.

## Uncertain

- Most labels are raw archetype names or the placeholder `ai`, and no string is added for them. The
  palette shows them as written.
- Whether a buddy placed this way follows the player, or just fights on the player's side, depends
  on `CBuddiesManager`, and that has not been checked in game.
