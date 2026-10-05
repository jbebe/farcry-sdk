---
title: Lights, bodyguards, whole buildings and sound points in the editor palette
kind: component
bundle: editor-content
status: located
systems: [world, ai, audio, ui]
match:
  - "ingameeditor/object_inventory.xml#Directory[+11]"
  - "languages/english/oasisstrings.fragment.xml#InGameEditor_Objects/{Razor1,Razor15,Razor16,Razor33,Razor37,Razor40,razor74}"
  - "languages/english/oasisstrings.xml#section[InGameEditor_Objects]/string[{Razor1,Razor15,Razor16,Razor33,Razor37,Razor40,razor74}]{,@value}"
exclude: []
requires: [editor-razor-library]
verified: diff
---

# Lights, bodyguards, whole buildings and sound points in the editor palette

A new "Other" folder at the end of the palette with a grab bag: coloured omni lights, the factions'
bodyguards, a weapon shop and a safehouse placed in one click, insect effects and ambient sound
points, and a few props.

## How

`ingameeditor/object_inventory.xml`: one top-level `<Directory Id="Directory_Other" Display="Other">`
(commented `Other Decor`) is added whole (`Directory[+11]`, one change). 28 entries:

- sub-folder "Lighting": seven `IGE_Archetypes.AutoPrefab.OmniLight_*` (`_62`, `_63`, `_84`, `_90`,
  `_91`, `_92`, `_113`) from the template library;
- `buddies.APR.APR_BodyGuard` and `buddies.UFLL.UFLL_BodyGuard` (both `Display="01"`) and
  `enemy_archetypes.Corpses.AnimatedCorpse`;
- from the custom library ([`editor-razor-library`](editor-razor-library.md)), seven entries:
  `Custom.RaZoR_Object.ParticlesEffect1`, `15` and `16` ("Flies", "Butterflies", "Fireflies"),
  `WeaponshopCombined` ("Weapon Shop Completed"), `UrbanSafehouseCombined` ("Safe House
  Completed"), `SoundPoint1` ("Waterfall Sound effect") and `FliesSound` ("Fly Sound Effect");
- props: `IGE_Archetypes.AutoGen.UrbanStorageShack_01`, `FishNet01`, `FishNet03`,
  `ContainerGlass_01` and `OilPuddle_01`, `OA_Explosives.Explosives.GasBottle01`, `GasBottle2` and
  `GasBottle4`, `OA_MissionPickups.MissionPickups.DiamondBriefcase_LVL1`, and the multiplayer flags
  `multiplayer.Flags.RedFlag.Multi` and `BlueFlag.Multi`.

The seven `RazorN` label strings are this page's.

## Depends on

- [`editor-razor-library`](editor-razor-library.md) for the seven custom entries.
- The bodyguards are AI characters. Like the soldiers of
  [`ai-people-palette`](ai-people-palette.md), they probably act only with
  [`ai-editor-mode-services`](ai-editor-mode-services.md).

## Uncertain

- What the diamond briefcase does when picked up in an editor map is not checked.
