---
title: Editor-library archetypes copied into the override library
kind: noise
systems: [weapons, economy]
match:
  - "generated/entitylibrarypatchoverride.fcb/pickups/weapons/*.xml"
  - "generated/entitylibrarypatchoverride.fcb/pickups/weapons/*/{rusty,dropped}.xml"
  - "generated/entitylibrarypatchoverride.fcb/pickups/weaponscrate/*.xml"
  - "generated/entitylibrarypatchoverride.fcb/weapons/**/{persistent,mikes_rusty,ai}.xml"
  - "generated/entitylibrarypatchoverride.fcb/weapons/special/{m2,mk19}.xml"
  - "generated/entitylibrarypatchoverride.fcb/weapons/explosives/ied_base.xml"
  - "generated/entitylibrarypatchoverride.fcb/weapons/grenades/unstablem67.xml"
  - "generated/entitylibrarypatchoverride.fcb/gadgets/equipped/binoculars.xml"
  - "generated/entitylibrarypatchoverride.fcb/oa_explosives/explosives/{explodingcar_prefab,explodingtruck_prefab,kiln_thinpropanetank_missionobjective}.xml"
  - "generated/entitylibrarypatchoverride.fcb/oa_missionpickups/missionpickups/{6vbattery,carvertape_gm00_old,diamondcanister,fieldmanualmnt,fieldmanualopt,test,ziplocdiamonds}.xml"
exclude: []
requires: []
verified: diff
---

# Editor-library archetypes copied into the override library

88 whole archetypes the mod adds to `generated/entitylibrarypatchoverride.fcb` that the game's
single-player libraries do not have and that nothing uses.

## How

They exist only in the worlds' editor library (`entitylibrary_full.fcb`, which the campaign does not
load), and the mod's library export carried them over:

- **Pickups.** The parent pickups `pickups.Weapons.<Weapon>_new` of 19 weapons (AS50, Carl Gustaf,
  Dart Rifle, Dragunov, Ithaca, LPO-50, M16, M1903, M249, M79, MAC-10, MGL140, mortar, MP5, PKM, RPG-7,
  SPAS-12, USAS-12, Uzi) and `Weapons.SilencedMakarov_6P9`; 13 `.Rusty` variants; `Weapons.M67.Dropped`
  and `Weapons.Molotov.Dropped`; 26 `pickups.WeaponsCrate.<Weapon>Crate`.
- **Weapons.** `.Persistent`, `.Mikes_Rusty` and `.AI` entities of the Dragunov, FAL, MP5, SPAS-12,
  Desert Eagle, M79, MAC-10, PKM and RPG-7 (12 units, each identical to the mod's parent entity apart
  from its name and id, so carrying the mod's recoil changes); `weapons.Special.M2`,
  `weapons.Special.MK19`, `weapons.Explosives.IED_Base`, `weapons.Grenades.UnstableM67`.
- **Others.** `gadgets.Equipped.Binoculars`; `OA_Explosives.Explosives.ExplodingCar_PREFAB`,
  `ExplodingTruck_PREFAB`, `Kiln_ThinPropaneTank_MissionObjective`; `OA_MissionPickups.MissionPickups.6VBattery`,
  `CarverTape_GM00_OLD`, `DiamondCanister`, `FieldManualMNT`, `FieldManualOPT`, `test`,
  `ZipLocDiamonds`.

No archetype in the mod's library and none in `world1`'s single-player library names any of them by
full name, as a string or as a hash; the only references are between the copies themselves (each
`.Rusty` pickup's `archWeapon` names its `.Mikes_Rusty` weapon). The weapon entities' short names
(`Primary.MP5.Persistent`) do occur in `world1`, as the names of the matching weapon properties; if
the engine paired a weapon entity with its properties by that name, the copies would still change
nothing, being identical to the entity the game falls back on.

## Uncertain

- Placed world entities carry their own data, so a sector naming one of these as its template was
  not searched for; the single-player library's build left them out, which suggests none does.
- `Primary.AS50.AS50_Merc` (new, used by the sniper pack) and the network player and
  `CompassMulti` (multiplayer) are not here: see `weapons-enemy-loadouts` and
  `noise-weapons-multiplayer`.
