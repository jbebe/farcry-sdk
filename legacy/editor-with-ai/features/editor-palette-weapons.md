---
title: Weapon pickups, crates and explosives in the editor palette
kind: component
bundle: editor-content
status: located
systems: [world, weapons, ui]
match:
  - "ingameeditor/object_inventory.xml#Directory[{5,6}]/**"
  - "languages/english/oasisstrings.fragment.xml#InGameEditor_Objects/{DLC_object02,DLC_object04,DLC_object05,DLC_object06,DLC_object07,DLC_object13,DLC_object14,FakeWeapon*,Physic_Weapon{01,02,03,04,05,06,07,08,09,10,11,12,13,14,15,17,18,19,20,21,24,25,26,27,28,29,30,31,58},Static_Weapon{01,02,03,04,05,06,07,08,09,10,11,12,13,15,16,17,20,21,22,23,24,25,26}}"
  - "languages/english/oasisstrings.xml#section[InGameEditor_Objects]/string[{DLC_object02,DLC_object04,DLC_object05,DLC_object06,DLC_object07,DLC_object13,DLC_object14,FakeWeapon*,Physic_Weapon{01,02,03,04,05,06,07,08,09,10,11,12,13,14,15,17,18,19,20,21,24,25,26,27,28,29,30,31,58},Static_Weapon{01,02,03,04,05,06,07,08,09,10,11,12,13,15,16,17,20,21,22,23,24,25,26}}]{,@value}"
exclude:
  - "languages/english/**/FakeWeapon33"
  - "languages/english/**string[FakeWeapon33]{,@value}"
requires: []
verified: diff
---

# Weapon pickups, crates and explosives in the editor palette

A map author can place usable weapons. The palette's Ammo and Weapons folder grows from 22 to 192
entries: every weapon as a respawning or a one-off pickup, the weapon crates, the Fortunes Pack
weapons, mounted guns, shells and IEDs, and the static display weapons. Explosives gains six
campaign objects that blow up.

## How

`ingameeditor/object_inventory.xml`:

- **Explosives** (`Directory[5]`, 6 changes), 11 to 17 entries:
  `OA_Explosives.Explosives.GasCan02_NEW.Multi` and the mission objects
  `OA_MissionObjectives.MissionObjectives.KilnPropaneTank`, `Kiln_ThinPropaneTank`,
  `TrainFuelCar_BK`, `NitrousTanks` and `RadioTransmitter03_WithGasTanks`.
- **Ammo and Weapons** (`Directory[6]`, 16 changes): 12 entries added among the vanilla ones, and
  four new sub-folders. Their XML comments name what each holds:
  - "Colonial" (comment `Respaunt`): weapon pickups (`pickups.Weapons.*`, among them
    `pickups.Weapons.AK47_new.AK47_Gold`) under `Physic_WeaponNN` ids.
  - "Industrial" (`Boxes`): `pickups.WeaponsCrate.*Crate` under `Static_WeaponNN` ids.
  - "Urban" (`No respaunt`): the `.Dropped` pickups.
  - "Other": the `OA_FakeWeapons.*` display weapons (`FakeWeaponNN` ids), flare, Carl Gustaf and
    M79 shells, `weapons.Secondary.IED.Multi`, the IED bases, mortar shells, rockets, grenades,
    machetes and the M2, M249 and MK19 vehicle and swamp-boat mounts.
  - The Fortunes Pack's `DLC1Weapons.DLC1.Pickup_Crossbow`, `Pickup_SawedOffShotgun`,
    `Pickup_SilencedShotgun` (plain and `.Dropped`), `WeaponCrate` and `.Multi`, under
    `DLC_objectNN` ids.

The `DLC_objectNN`, `FakeWeaponNN`, `Physic_WeaponNN` and `Static_WeaponNN` ids have label strings
in `InGameEditor_Objects`, 91 of them, which are this page's. They are placeholders ("FakeWeapon 1",
"Static_Weapon 2", "DLC_object 4"), so the palette shows numbers, not weapon names. The strings for
ids this palette does not use are on [`noise-editor-strings`](noise-editor-strings.md).

## Depends on

- The Fortunes Pack DLC for the `DLC1Weapons.*` entries.

## Uncertain

- `pickups.weapons.Molotov` names no archetype in the template library, so that entry spawns
  nothing.
- That the "Colonial" pickups respawn and the "Urban" ones do not is taken from the author's
  comments. It depends on each archetype's respawn settings, which were not read.
- Whether a player in an editor map can pick these up and fire them depends on the editor mode's
  services, which [`ai-editor-mode-services`](ai-editor-mode-services.md) changes.
