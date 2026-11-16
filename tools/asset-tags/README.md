# Asset tags

Every model (`.xbg`), texture (`.xbt`), sound bank (`.spk`) and shader object (`.pso`, `.vso`, `.rs`)
of the retail game, tagged by what it is and where it belongs, so that a scene can be dressed from a
list: "a house interior", "a bus and people packing to leave".

| Table | Rows | One row per |
| --- | --- | --- |
| `models.tsv` | 2,961 | `.xbg` in `worlds`, `common`, `patch` and the DLC archives |
| `textures.tsv` | 2,916 | `.xbt` under `graphics\` and `ui\`; per-level map tiles and sector maps are left out |
| `sounds.tsv` | 8,282 | `.spk` that `jackall-cli spk label` labels (`tools/JackAll/assets/fc2.soundlabels.tsv`) |
| `shaders.tsv` | 27,516 | Direct3D 9 shader object that `jackall-cli shader label` labels (`fc2.shaderlabels.tsv`) |

## Finding assets

```powershell
python tools/asset-tags/find.py home interior            # models carrying both tags
python tools/asset-tags/find.py travel --kind container  # one kind only
python tools/asset-tags/find.py market --sounds          # --textures and --shaders for those
python tools/asset-tags/find.py jerrycan                 # a word in a path, description or archetype
```

A term that is a kind, tag or trait of the table matches only those, so `war` does not find
warehouses; any other term matches any part of the path, the description, the archetypes or the
detail. One line covers the variants of an object (`_01`, `_02`, broken and LOD meshes), a texture with
its maps, or sounds or shaders with one description; `--all` lists every file instead. A query
matching more than 60 objects lists only their names per kind until `--kind` narrows it.

JackAll ships the four tables and searches them in the Files tab with `tag:`, by the same rule. They
are the only labels it reads, every one `kind · detail`, or `kind · what` where a table has no
detail; the labelers' lists reach it through `build.py`.

## Thumbnails

```powershell
python tools/asset-tags/render.py travel --kind container   # a contact sheet of what find.py lists
```

`render.py` takes `find.py`'s terms, `--kind` and `--all` for models, renders each model it lists with
its textures (a `.fc2model` pack from `jackall-cli`, drawn by Blender through the BlenderFC2 add-on),
and draws numbered contact sheets of 20 models each. A render is kept, gitignored, in the main tree's
`tools/asset-tags/thumbs/` at the model's game path, so each model is drawn once; `--limit` caps a
run at 60 models. It reads the game install (`--game`, `C:\Games\Far Cry 2` by default) and needs
Blender 5.2 (`--blender`). A model drawn plain red has a texture the pack could not find.

## Columns

The hand columns are `kind`, `what` and `tags`. `build.py` writes every other column from the game
files and the labelers' lists and keeps the hand columns of the existing table, matched by path. A
sound's or shader's empty hand column is filled from its label: the kind is the labeler's category,
in the word the other tables use (a sound's `scene` is `dialogue`, `other` is `misc`; a shader's
`vehicles` is `vehicle`, `foliage` is `vegetation`, `ground` is `terrain`, `effects` is `fx`); a
shader's `what` is the shader and its lighting, and its tags the quality presets that use it (`all
presets` is all five).

| Column | Table | Meaning |
| --- | --- | --- |
| `kind` | all | what the asset is, one value from the lists below |
| `what` | all | a few plain words; a trailing `?` where the name was the only clue |
| `tags` | all | where it belongs and what it is like, from the lists below |
| `size` | models | width x depth x height in metres, from the mesh's box (`XOBB`) |
| `size` | textures | width x height in pixels |
| `placed` | models | how many campaign sector files place it; 0 for meshes swapped in at run time (broken, LOD), spawned (weapons, vehicles, characters) or never used in the campaign |
| `places` | models | the named places within 200 m of those sectors, most frequent first |
| `archetypes` | models | the archetypes of the full entity library that draw it, for the Map tab's archetype library; none means it is placed as a bare mesh (`StaticObject`) |
| `textures` | models | the diffuse textures its materials bind |
| `traits` | models | from its archetypes: `static`, `rigid` (moves when hit), `breakable`, `flammable`, `pickup`, `vehicle`, `character`, `animated`, `building`; from its name: `broken-variant`, `lod`, `mp` |
| `users`, `used_by` | textures | how many models bind it, and the first three |
| `inherited` | textures | the tags at least half of the models binding it carry; for a normal, specular or mask map, also the tags of the diffuse textures beside it in a material |
| `detail` | sounds, shaders | the labeler's detail: where the sound is used, or the shader, its passes, options and presets |

## Vocabulary

A row takes one kind and any number of tags. Only these words are valid; `merge.py` and `build.py`
list every other word in the tables.

### Model kinds

`building` a whole building or shell · `building-part` wall, roof, floor, door, window, stairs,
awning · `furniture` · `appliance` stove, fridge, fan, washing machine · `electronics` radio, TV,
computer, phone · `light` lamp, light fitting, street light · `container` crate, barrel, box,
jerrycan, sack, basket, bag, suitcase · `kitchenware` pots, plates, cups, cutlery · `food` ·
`bottle` · `textile` clothes, laundry, blankets, mattress, rugs, curtains · `decor` pictures,
vases, ornaments, plants in pots · `paper` books, documents, maps, photos, money · `tool` ·
`machinery` generator, pump, engine, industrial machine · `vehicle` · `vehicle-part` · `wreck` ·
`weapon` · `ammo` · `military-gear` helmets, packs, radios, sandbag kits · `medical` · `sign` ·
`poster` · `fence` · `barrier` sandbags, barricades, roadblocks · `infrastructure` bridges, poles,
power lines, pipes, towers, tanks, rails · `debris` trash, rubble, scrap · `tree` · `plant` bush,
grass, flowers · `rock` · `terrain` cliffs, terrain pieces · `animal` · `character` ·
`corpse` · `fx` particle and effect meshes · `helper` editor, level design, map icons, dummies ·
`misc`

### Texture kinds

`material` a tileable surface · `decal` · `terrain` · `water` · `sky` · `fx` particles, smoke,
fire · `vegetation` · `character` skin, clothes, faces · `object` an object's own texture · `sign` ·
`cubemap` · `ui-icon` · `ui-hud` · `ui-menu` · `ui-map` · `font` · `misc`

### Sound kinds

`ambience` · `music` · `object` a thing that sounds: radio, generator, fridge · `fire` · `water` ·
`vehicle` · `weapon` · `explosion` · `impact` · `foley` the body: steps, cloth, handling · `voice`
breaths, grunts, screams · `dialogue` · `animal` · `ui` · `effect` · `misc`

### Tags

Places, every one that fits:

- **Domestic:** `home` (a dwelling, inside or its yard) · `kitchen` · `bedroom` · `bathroom` ·
  `living-room` · `laundry` (washing and drying clothes)
- **Public and work:** `office` · `shop` · `market` · `bar` · `restaurant` · `hotel` · `church` ·
  `school` · `clinic` · `police` · `prison` · `hq` (a faction headquarters) · `government`
- **Military:** `military` (bases, barracks) · `checkpoint` · `armory` · `camp` (a temporary camp:
  tents, campfire, bedrolls)
- **Settlements:** `village` · `town` · `shanty` · `colonial` · `hut`
- **Production:** `farm` · `ranch` (cattle) · `fishing` · `lumber` · `mine` · `oil` (fuel depots,
  pipelines, refineries) · `brewery` · `greenhouse` · `slaughterhouse` · `factory` · `industrial` ·
  `workshop` · `garage` · `warehouse` · `construction` · `junkyard`
- **Transport:** `road` · `gas-station` · `bus-station` · `railway` · `airfield` · `port` (docks,
  harbour) · `river` (boats, crossings) · `travel` (what people carry or load when they move:
  luggage, bundles, mattresses, roof loads)
- **Crisis:** `refugees` · `aid` (humanitarian: relief supplies, aid tents) · `war` (combat
  aftermath: shell holes, bodies, burned-out things)
- **Other:** `cemetery` · `ruins` · `safehouse` · `radio-station` · `communications` (antennas,
  radios, phones) · `power` (generators, pylons, transformers) · `water-supply` (wells, tanks,
  pumps) · `wilderness`

Setting: `interior` · `exterior` · `savanna` · `jungle` · `desert` · `wetland` · `urban` · `rural`

Material: `wood` · `metal` · `concrete` · `brick` · `stone` · `mud` · `straw` · `tin` (corrugated
sheet) · `plastic` · `fabric` · `glass` · `rubber` · `paper` (and cardboard) · `paint`

Condition: `broken` · `burned` · `rusty` · `bloody` · `dirty`

Other: `apr` · `ufll` (faction-marked) · `story` (made for one mission) · `clutter` (small filler
for dressing a surface) · `landmark` (large and distinctive) · `usable` (gameplay: pickup, ammo,
health, save point, ladder, door) · `animated`

### Sound tags

Sounds take the tags above and these: `loop` · `one-shot` · `day` · `night` · `crowd` · `radio` ·
`machine` · `insects` · `birds` · `wind` · `rain` · `distant` · `close`

### Shader kinds

The shader labeler's categories, in the other tables' words where they have one: `vegetation` grass,
leaves and trunks · `terrain` terrain, roads, decals · `weapon` · `character` skin, hair, cloth ·
`vehicle` · `world-meshes` buildings, props and other models · `sky` · `water` · `fx` particles and
post effects · `engine` depth and render-state passes, debug drawing · `shared` an object several of
these share

### Shader tags

The graphics quality presets that draw it: `low` · `medium` · `high` · `veryhigh` · `ultrahigh`, or
`no-preset` when only a custom mix of settings does

## Regenerating

```powershell
python tools/asset-tags/merge.py <file>...   # a tagger's path/kind/what/tags rows into the tables
python tools/asset-tags/build.py             # needs tmp/gamefiles and a Release jackall-cli
```

Run `build.py` after a merge: the textures' `inherited` column follows the models' tags.

`build.py` reads the extracted archives in the main tree's `tmp/gamefiles`, the full entity library
of `tmpla` (decoded with `jackall-cli fcb decode`), every campaign `worldsector` and `landmarknear`
file, the gazetteer of `docs/docs/lore/set-dressing/reading-the-world.md`, and the labelers' lists in
`tools/JackAll/assets`. Sector `n` covers the 64 m square at `(n mod 80, n div 80) x 64`. After
`jackall-cli spk label` or `shader label`, run `build.py`, or JackAll keeps the old labels.
