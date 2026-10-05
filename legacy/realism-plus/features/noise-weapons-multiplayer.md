---
title: Multiplayer archetypes the mod's library differs in
kind: noise
systems: [weapons, player]
match:
  - "generated/entitylibrarypatchoverride.fcb/{weaponproperties,weapons,pickups,gadgets,oa_explosives}/**/multi{,_apr,_ufll}.xml#**"
  - "generated/entitylibrarypatchoverride.fcb/pickups/**/multi/*.xml#**"
  - "generated/entitylibrarypatchoverride.fcb/player/maincharacter/pawnplayernetwork*.xml"
  - "generated/entitylibrarypatchoverride.fcb/player/maincharacter/pawnplayernetworkkit/**"
  - "generated/entitylibrarypatchoverride.fcb/gadgets/equipped/compassmulti.xml"
  - "generated/entitylibrarypatchoverride.fcb/curves/stimeffectcurves/crushdamagemp.xml{,#**}"
exclude:
  - "generated/entitylibrarypatchoverride.fcb/weaponproperties/special/lpo50/multi.xml#Entity/Components/**"
  - "generated/entitylibrarypatchoverride.fcb/gadgets/objectiveicons/**"
requires: []
verified: diff
---

# Multiplayer archetypes the mod's library differs in

The base game's override library is mostly multiplayer archetypes (`.Multi`, `.Multi_APR`,
`.Multi_UFLL`, the network player). The mod's copy of that library differs from Steam's in many of
them; single-player never uses these archetypes, so none of this shows in the campaign.

## How

- **Renumbered ids.** `Entity/disEntityId` of every multiplayer archetype in these containers is
  shifted by 1 to 8 (for example `Primary.AK47.Multi` weapon properties `5478` -> `5473`): the mod's
  tool re-exported the library with entries added before them. A pick keeps the base game's ids.
- **A dropped field.** `CGraphicComponent/bIntelHackGliderOn` (`False`) is missing from 139 multiplayer
  weapon, pickup, grenade and explosive archetypes, as from an export made with an older class list.
- **Multiplayer values.** `RangeMultipliers` (range and multiplier per difficulty) and first-person
  spread on the multiplayer rifles, SMGs, pistols, shotguns, M249 and PKM, and the snipers'
  `RangeMultiplier` entries removed; `Stim_ImpactDamage` and
  victim stims of the multiplayer AS50 (`11` -> `24`, `16` -> `27`), Dragunov (`10` -> `23`), Dart
  Rifle and M1903 (`10` -> `26`); recoil of the multiplayer M16 and PKM; explosion levels of the
  multiplayer M67, M79 and MGL140 grenades (`21`/`19` -> `22`, MGL140 radius `5` -> `7`) and rockets
  (`26` -> `22`); `FireStickyStream.Multi` burn level `9` -> `10`; the multiplayer IED's three
  explosives pointing at the single-player `IED_Mine`, `IED_MortarShell` and `IED_PipeBomb`.
- **Network player and compass.** New whole archetypes `player.MainCharacter.PawnPlayerNetwork`,
  `PawnPlayerNetworkKit` and its six class kits with `Level1-4` and `VIP` (38 units), and
  `gadgets.Equipped.CompassMulti`, copies of the editor library's.
- **`Curves.StimEffectCurves.CrushDamageMP`.** Reduced from 12 knots to a straight two-knot line
  from `0,0` to `22.0749,-450.389`, the old curve's end point. Named for multiplayer beside
  `CrushDamageVehicle` and `CrushDamageVehicleMP`.

The values look like another patch level's multiplayer balance rather than edits for this mod
(inference): nothing in single-player names these archetypes.

## Uncertain

- No single-player use of `CrushDamageMP` was found (by name and by hash, in the mod's library and
  `world1`'s), but the curve's consumer is not traced, so a single-player effect is not ruled out.
- `WeaponProperties.Special.LPO50.Multi`'s value changes are excluded: the mod hands that
  flamethrower to single-player shotgunners (`weapons-enemy-loadouts`). Its renumbered id is here.
