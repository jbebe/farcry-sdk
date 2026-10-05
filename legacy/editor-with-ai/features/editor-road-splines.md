---
title: Forty new roads, paths, rails and an airstrip in the editor
kind: component
bundle: editor-content
status: located
systems: [world, graphics, ui]
match:
  - "ingameeditor/spline_inventory.xml@**"
  - "_hash/*.xbg"
  - "_hash/{1097d6c0,13389c90,13cb6087,149fa1b7,1a45325b,1abd1c59,20387b09,24b17d20,2504803d,2555e194,274a5bfb,28ab68d5,2a756ff2,2bc092ef,2c841ee7,2ca29b86,2d17669b,2d516d93,2db48f26,2de82eb5,2e7c9ca1,2fc961bc,3540fdb1,3968392d,3ca01db3,458280b8,4bd5c9af,56472495,59ebdaca,5a820d16,5b0d1788,5b51b61b,5b9d60e2,5fe92967,60613000,65dd3e4e,662e4e6e,68253552,686c5798,6ab250bf,6b07ada2,6f0e5ef1,7b64d278,7eb7d404,805d8af8,852ac862,8bb89d87,8cfcde08,95eb6d0b,ac17aba4,ae0811cb,b6228747,b6c765f2,b911685b,ba65b602,ba78762f,c0225ccf,c07efd5c,c2850fda,caf0ac88,cb2c2119,d1bedaa4,d2ca04fd,e27b1f2a,e4536cf7,e8b68ebc,eaa934d3,f150f596,f34f4ff9,f7776e23,f9b99a55,fd5da4ba}.xbt"
exclude: []
requires: []
verified: diff
---

# Forty new roads, paths, rails and an airstrip in the editor

The editor's road tool goes from 15 to 55 spline types. There are recoloured asphalt, dirt and sand
roads, paths and rail beds for each biome, plus two vanilla road materials that had no entry: a
desert railway and a savannah airstrip.

## How

- `ingameeditor/spline_inventory.xml` (five text hunks; the file is compared as text): adds
  `FCX_TrainDesert01` ("Desert Train", `graphics\_materials\editor\Road_Desert_TrainDesert01.xbm`)
  and `FCX_AirStripSavannah01` ("Savannah Airstrip", `road_savannah_airstrip.xbm`) to the vanilla
  folders. Both materials ship in the vanilla worlds archive. It also adds nine sub-folders
  (Desert "Asphalt", "Sand", "Path and Train"; Savannah, Woodland and Jungle "Road" and "Path and
  Train") with 38 entries, `FCX_RoadDesert02`..`17`, `FCX_PathDesert02`/`03`,
  `FCX_RoadSavannah02`, `FCX_PathSavannah02`/`03`, `FCX_TrainSavannah02`, `FCX_RoadWoodland02`/`03`/`05`,
  `FCX_PathWoodland02`..`05`, `FCX_TrainWoodland02`, `FCX_RoadJungle02`..`05`,
  `FCX_PathJungle02`..`04`, `FCX_TrainJungle02`, each naming a new material such as
  `graphics\_materials\editor\Road_Desert_RoadDesert05_a.mlm`. A header comment credits "Far Cry 2
  - Multi... Editor v1.3.2.2".
- 38 new materials, `_hash/*.xbg`. They are `.xbm` materials, not meshes: each is
  `graphics\_materials\editor\road_<biome>_<type><nn>_<suffix>.xbm`, the entry's `.mlm` name with
  the compiled extension, named by CRC32. Each is a copy of a vanilla road material (internal names
  like `ROAD_DESERT_ROADDESERT10`) whose diffuse texture path is changed in place. The vanilla
  `officialdesertroad_d.xbt`, `officialroadgeneric_d.xbt`, `officialpathgeneric_d.xbt` and
  `officialtraingeneric_d.xbt` lose their first two letters to a code of the same length:
  `a5ficialdesertroad_d.xbt`, `gw3icialroadgeneric_d.xbt`, `sfficialtraingeneric_d.xbt`. The mask,
  normal, height and specular textures stay vanilla.
- 72 new textures, `_hash/*.xbt`, all named by CRC32: 36
  `graphics\terrain\_textures\roads\<code>ficial<kind>_d.xbt` (1024² with mips) and their 36
  `_mip0` companions (2048²).

## Uncertain

- **The file is not well-formed XML.** The last new folder (Jungle "Road") is never closed before
  `</SplineInventory>`. [`editor-collections`](editor-collections.md) has the same slip. Both come
  from a released editor mod, so the editor's parser probably tolerates it, but that has not been
  checked.
- As with the terrain textures ([`editor-terrain-textures`](editor-terrain-textures.md)), each new
  road texture's header names a vanilla companion (`dessert_crackedearth_n_mip0.xbt` for most), not
  its own `_mip0`. Up close the roads may show that donor's top level, and the 36 shipped companions
  may never be read.
- The `.xbm` materials keep their donors' internal names. Whether two materials sharing one internal
  name collide at load has not been checked.
