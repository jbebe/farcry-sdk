---
title: Editor palette strings for entries this palette does not have
kind: noise
systems: [ui]
match:
  - "languages/english/oasisstrings.fragment.xml#InGameEditor_{Objects,Textures}/*"
  - "languages/english/oasisstrings.xml#section[InGameEditor_{Objects,Textures}]/**"
exclude:
  - "languages/english/oasisstrings.fragment.xml#InGameEditor_Objects/{dlc1_vehicle1,dlc1_vehicle2,dlc1_vehicle3,dlc1_vehicle4,Razor1,Razor2,Razor3,Razor4,Razor5,Razor6,Razor7,Razor8,Razor9,Razor10,Razor11,Razor12,Razor13,Razor14,Razor15,Razor16,Razor17,Razor18,Razor19,Razor20,Razor21,Razor22,Razor23,Razor24,Razor33,Razor34,Razor35,Razor36,Razor37,Razor39,Razor40,Razor41,Razor48,Razor50,Razor55,razor57,razor58,razor59,razor60,razor69,razor70,razor71,razor72,razor73,razor74,razor75,razor76,razor78,razor81,razor85,razor87,razor88,razor89,razor90,razor91,razor94,DLC_object{02,04,05,06,07,13,14},FakeWeapon{01,02,03,04,05,06,07,08,09,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32},Physic_Weapon{01,02,03,04,05,06,07,08,09,10,11,12,13,14,15,17,18,19,20,21,24,25,26,27,28,29,30,31,58},Static_Weapon{01,02,03,04,05,06,07,08,09,10,11,12,13,15,16,17,20,21,22,23,24,25,26}}"
  - "languages/english/oasisstrings.xml#section[InGameEditor_Objects]/string[{dlc1_vehicle1,dlc1_vehicle2,dlc1_vehicle3,dlc1_vehicle4,Razor1,Razor2,Razor3,Razor4,Razor5,Razor6,Razor7,Razor8,Razor9,Razor10,Razor11,Razor12,Razor13,Razor14,Razor15,Razor16,Razor17,Razor18,Razor19,Razor20,Razor21,Razor22,Razor23,Razor24,Razor33,Razor34,Razor35,Razor36,Razor37,Razor39,Razor40,Razor41,Razor48,Razor50,Razor55,razor57,razor58,razor59,razor60,razor69,razor70,razor71,razor72,razor73,razor74,razor75,razor76,razor78,razor81,razor85,razor87,razor88,razor89,razor90,razor91,razor94,DLC_object{02,04,05,06,07,13,14},FakeWeapon{01,02,03,04,05,06,07,08,09,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32},Physic_Weapon{01,02,03,04,05,06,07,08,09,10,11,12,13,14,15,17,18,19,20,21,24,25,26,27,28,29,30,31,58},Static_Weapon{01,02,03,04,05,06,07,08,09,10,11,12,13,15,16,17,20,21,22,23,24,25,26}}]{,@value}"
  - "languages/english/oasisstrings.fragment.xml#InGameEditor_Textures/Misc_Junkyard_Small"
  - "languages/english/oasisstrings.xml#section[InGameEditor_Textures]/string[Misc_Junkyard_Small]{,@value}"
verified: diff
---

# Editor palette strings for entries this palette does not have

English editor strings the mod adds whose keys nothing in this mod's palettes uses. The editor looks
a palette entry's label up in `InGameEditor_Objects` (or `InGameEditor_Textures` for a terrain brush)
under the entry's `Id`. No entry, folder or brush in this mod has these ids, so they are never shown.
They are left over from Janne252's own palette, which this mod's palette files (from the "Multi...
Editor v1.3.2.2" lineage) replaced.

In both `languages/english/oasisstrings.xml` and `oasisstrings.fragment.xml`:

- **`InGameEditor_Objects`, 151 keys**: Janne252's folder names (`Janne252_folder` "Extra Objects",
  `Janne252_DLC2_folder` "DLC2 required Ingame!", `Janne252_DLC_folder` "Fortunes Pack Objects",
  `Janne252_particles_folder`, `Janne252_boosted_cars_folder` "Faster Vehicles",
  `Janne252_crazy_cars_folder`, the weapon folders and others), `Directory_Custom`,
  `Directory_Custom_made`, `Directory_Normal`, `Directory_Particles`, `Directory_Retail`, labels
  for custom-library archetypes this palette leaves out (`Razor25`..`32` and `38` the weapon shop
  and safehouse parts, `Razor42`..`46` the no-sync bottles and collision block, the swamp boats
  `razor61`..`68`, "Crazy Datsun 01"/"02" `razor92`/`93`, plants), unused weapon placeholders
  (`DLC_object01`, `03`, `08`..`12`, `15`..`21`, `FakeWeapon33`, and the higher-numbered
  `Static_Weapon*` and `Physic_Weapon*`), `dlc1_vehicle6`, `dlc_ladders` "Metal ladders",
  `Razor_Placeholder`, a key written with dots (`Custom.RaZoR_Object.Natural.GroundDetail1`, which
  the dot-to-underscore lookup cannot hit), and two Finnish test keys, `testi` and `vanhat` ("test",
  "old").
- **`InGameEditor_Textures`, 85 keys**: Janne252's custom texture brushes, `Janne252` "Janne252's
  Custom Textures", its folders (`Janne252_Wood`, `_Dirt`, `_Ceramic`, `_Concrete`, `_Colors`,
  `_Misc`) and 78 `Custom_*` brushes (concrete, "Snow", wood, plywood, dirt, bricks, ceramic, colour
  swatches, "Meat 01"/"02", "Brown crop 01".."03"). This mod's texture palette
  ([`editor-terrain-textures`](editor-terrain-textures.md)) has none of these brushes, and the mod
  ships no textures for them.

The same keys appear in the seven other languages' fragments as part of a much larger rewrite of
those files. They are not on this page.
