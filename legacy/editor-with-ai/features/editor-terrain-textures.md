---
title: New terrain textures and a retuned terrain look in the editor
kind: component
bundle: editor-content
status: located
systems: [world, graphics, ui]
match:
  - "ingameeditor/texture_inventory.xml#**"
  - "worlds/tmpla/generated/tmpla.game.xml#Layers/**"
  - "_hash/{007afa90,0262558d,03c650ba,0aacce9d,0b89f5f7,0da65e1a,0f7c25ca,0fd6b8c6,15d13a2a,16e6bf35,18cf4a6d,20269062,20c3386e,22902c4b,2696f4f4,2b869685,30c677aa,32619369,3fefa2e9,44927ac0,44ee402f,452ff189,455ff78a,489b3962,4a0624ca,527d6743,5370454f,537d3421,5601d01e,5788e41c,5a96945b,5b8ccf91,5b96c739,5c56a794,5e634466,5f631704,61368e24,77660d7a,82e6abba,84b4ed1b,87b5c208,8c86a51a,8dcf7ef9,8ff6c9cb,916b0676,92064c72,94061dc9,94b628e4,a233da51,a2519141,a69a819f,a9d60154,aa9d9cb4,aea592a1,af681feb,b0a17c3a,b0af5749,b1a12f58,b1e68caa,b454ac07,b554ff65,b94adc40,bdbf0c7d,c04c3732,cb26dfca,d3165234,e09e1f61,eda03eb3,ee767b84,f826f8da,fa135114,fee68129,fffc90df}.xbt"
  - "languages/english/oasisstrings.fragment.xml#InGameEditor_Textures/Misc_Junkyard_Small"
  - "languages/english/oasisstrings.xml#section[InGameEditor_Textures]/string[Misc_Junkyard_Small]{,@value}"
exclude: []
requires: []
verified: diff
---

# New terrain textures and a retuned terrain look in the editor

The editor's terrain brush palette goes from 27 to 55 textures. 24 are new recolours and variants
(jungle undergrowth, dried riverbeds, roadsides, sand, urban ground), and four vanilla terrain
layers that had no brush get one. Every terrain layer of the template world is also retuned: larger
tiling, a shared specular map, and rock faces painted from above only.

## How

Three parts that have to go together, since the brushes name the layers and the layers name the
textures.

**Layers**: `worlds/tmpla/generated/tmpla.game.xml`, `<Layers>` (`#Layers/**`, 649 changes). The
analysis did not split this descriptor (it is plain XML), and the mod inserts its layers among the
vanilla ones, so the comparison pairs layers by position and the changes only make sense applied
together. Compared by layer name:

- 24 new layers: `Jungle_Underbrush_Light_1`..`_9` and `_m1`, `_m3`, `_m4`, `_m5`,
  `Desert_Dried_Riverbed_m1`..`_m4`, `Desert_Sand_Rippled_1`, `Savannah_Undergrass_m1`,
  `Jungle_Roadside_1`, `Woodland_Roadside_1`, `Savannah_Roadside_1`, `Woodland_Urban_Ground_1`,
  `Savannah_Decal` and `Misc_Junkyard_Big`. The last renames vanilla's `Misc_Junkyard_Big ` (with a
  trailing space).
- Two vanilla layers get new diffuse textures: `Woodland_Underbrush_Light` (to
  `woodland_undergrass_d_1.xbt`) and `Savannah_Underbrush` (to `savannah_undergrass_d_1.xbt`).
- All layers: an empty `SpecularMap` becomes `graphics\terrain\_textures\desert\dgeneric_s.xbt`,
  `SpecularMapTiling` becomes 10 (from 1 to 200) and `SpecularShininess` 10. Ground layers go from
  a `Tiling`/`NormalMapTiling` of 14 to 35 (mostly 20) to 25, and most get `Projected` 1.
- Rock layers: each `*_Mountain_Rock`, `Heart_Rock` and `Mountain_Underbrush` `_Z` layer gets
  `Tiling` 1, and the matching `_X`/`_Y` layers 100. Four `_Z` layers get `MinSlope` 45 or 50.

**Brushes**: `ingameeditor/texture_inventory.xml` (28 changes). Five new directories (Misc, and a
second Desert, Savannah, Woodland and Jungle) list the new layers. Misc also brings in the vanilla
`Misc_Tire`, `Misc_Junkyard_Big` and `Misc_Junkyard_Small` layers, and Woodland gains
`Woodland_Underbrush_Light`. All four had layers in the template world and no brush. The six rock
brushes lose `ProjectionX`/`ProjectionY`, so they paint only their `_Z` layer. `Savannah_Underbrush`
gets new minimap colours (grey-green instead of brown) to match its new texture. The new
`Misc_Junkyard_Small` string labels that brush "Junkyard" and is this page's.

**Textures**: 73 new `_hash/*.xbt`. 70 are named by CRC32 of their path:

- 36 textures under `graphics\terrain\_textures\{jungle,desert,savannah,woodland}\`, each 1024²
  with mips: `jungle_underbrush_light_d_1`..`_9`, `_d_m1`/`_m3`/`_m4`/`_m5` and `_n_1`, `_n_3`..`_n_8`,
  `dessert_dried_riverbed_d_m1`..`_m4` and `_n_m3`/`_n_m4`, `desert_sand_rippled_n_1`,
  `savannah_undergrass_d_1`/`_d_m1`, `savannah_roadside_d_1`, `woodland_roadside_d_1`,
  `woodland_undergrass_d_1`, `jungle_roadside_d_1`/`_n_1`, `urban_ground_d_1`/`_n_1`.
- 34 `<name>_mip0.xbt` companions, 2048², one per texture except `urban_ground_n_1` and
  `woodland_roadside_d_1`.
- Three are not named. `_hash/94061dc9.xbt` hashes as `woodland_roadside_d_1mip0.xbt`, a companion
  with its underscore missing. `_hash/489b3962.xbt` (2048², no mips) and `_hash/916b0676.xbt` (1024²
  with mips) match no path tried. Nothing found references them.

## Uncertain

- Every new texture's header names a donor's companion, not its own. Examples are
  `dessert_crackedearth_n_mip0.xbt`, `dessert_mountain_rock_n_mip0.xbt` and
  `jungle_underbrush_light_n_mip0.xbt`, all vanilla normal maps. [The XBT
  notes](../../../docs/docs/file-formats/xbt.md) say the engine follows the header's path. If so,
  close up these textures show the donor's top level, and the 34 shipped companions are never read.
  Not checked in game.
- Two layers name textures that exist neither in the base game nor in the mod:
  `jungle_underbrush_light_n_2.xbt` (normal map of `Jungle_Underbrush_Light_2`) and
  `savannah_decal_n.xbt` (`Savannah_Decal`).
- The retune applies to every map built on the template, including existing ones. How far it changes
  their look, and whether `Projected` 1 on flat ground costs performance, is not measured.
