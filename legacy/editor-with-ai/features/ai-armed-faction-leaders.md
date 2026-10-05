---
title: Faction leaders carry rifles and grenades
kind: component
bundle: editor-war-ai
status: located
systems: [ai, weapons]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/WeaponsService/Properties/InventoryPacks/Pack[warlord]/**"
exclude: []
requires: []
verified: diff
---

# Faction leaders carry rifles and grenades

The `.Armed` faction leaders in [`ai-people-palette`](ai-people-palette.md) fight with a rifle,
machine gun, machete, flare gun and grenades. In vanilla they carry only a pistol.

## How

`engine/gamemodes/gamemodesconfig.xml`, the `warlord` inventory pack. In the template world's
library the `.Armed` faction leaders and other warlord archetypes reference this pack
(`packInventoryPack` `warlord`, 23 archetypes). Ten world 1 archetypes reference it too.

- **77 `PrimaryWeapon` entries added**, spread over progression levels 0 to 27: `AK47`, `FNFAL`,
  `G3KA4`, `M16` and `MP5` (11 or 12 each). From level 7 there is also a 0.25 to 0.5 chance of the
  `M249_Saw_Merc` or `PKM_Merc` machine gun. Vanilla `warlord` has no primary weapon.
- **17 `SecondaryWeapon` archetypes changed**, so the pistol slot now mixes `MAC10`, `Uzi`,
  `DesertEagle`, `Star45` and `Makarov` rather than Makarov then Star45.
- **Added:** `MeleeWeapon` `weapons.HandToHand.Machete`, `SpecialWeapon`
  `weapons.Special.Flare_Gun.Flare_Gun_Merc`, and the `Gadget`s `gadgets.Grenades.M67` and
  `gadgets.Grenades.Molotov`, each `count="99"`.

The level that picks the weapons is the Weapons service's progression level (see
[AI](../../../docs/docs/engine-internals/ai.md#adaptive-behaviours)). In an editor map that is
whatever the editor's `CFCXWeaponsService` reports.

## Depends on

Nothing to load, but it only shows in the editor with
[`ai-editor-mode-services`](ai-editor-mode-services.md) and the palette. The pack is shared with
the campaign, so any campaign warlord that draws a weapon is armed the same way.

## Uncertain

- Which progression level the editor mode reports, and so which of the 28 columns an editor map
  uses, has not been checked.
- With 99 grenades of each kind, a faction leader likely never runs out.
