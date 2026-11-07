# Map Overhaul

A realistic map in place of the game's drawn one, and the GPS removed: world 1 as a military
topographic sheet, the kind handed over at a briefing, drawn from the world's own data. The idea is
issue #29, the design `docs/docs/design/map.md`. So far the sheet is on the game's own map in the hand,
and the map has lost the player's arrow (seen in game, 2026-11-07). The GPS in the hand stays, and so
does the one on a vehicle's dashboard in a saved game, though the player's archetype no longer names it.

## The sheet

`extract_world.py` reads world 1 out of an extracted `worlds.fat` (by default `tmp\gamefiles\worlds\worlds`)
into `work/world1/`: the ground's heights and each sector's water from the `.sdat` files, the road,
river and path splines from `world1.mapsdata.fcb`, and every placed entity with a bounding box, a
detail-map rectangle or a `mapsdata` role, from the sector files and the entity library. It takes a
few minutes and runs once.

`render_map.py` draws `assets/world1-sheet.png` from that, 5120 px a side at 1 m per pixel, the world's
own extent, so it slices back into the game's 25 area maps:

| Layer | Drawn from |
|---|---|
| Height tints, 20 m bands to 100 m, then 50 m | the heights, smoothed over 1.5 m |
| Contours every 10 m, labelled every 50 m; spot heights | the same |
| Water and shorelines | ground below its sector's water level, where the sector has still or river water |
| Roads, white in a black casing; tracks | the road and path splines |
| Bridges, as road | a road stops at each riverbank; where bridge decks stand between two road ends, the ends are joined |
| Railway | the track pieces, by their boxes |
| Buildings | models under `graphics\buildings\`, 6 m² or more, by their boxes |
| Names | the 21 detail maps' entities, in `PLACES`, and the summit as Mt Thabamolaetsa |

The neat line is the playable square the game's world map covers, a sector wider each side; the
margin outside it holds the title, legend, scale, layer tints and north points. `--region` and
`--scale` draw part of the world for a closer look.

Over it, `notes.py` writes what the previous owner marked in blue ballpoint, in Nothing You Could Do (a
Google font under the SIL Open Font License, fetched into `work/fonts/` the first time) with a little
tilt; `--clean` leaves them off:

| Note | Drawn from |
|---|---|
| A sketched diamond on each guard post, and `G.P.` | the 32 `Objectives.PGP.*` markers in `mapsdata` |
| Safe houses, a sketched house and `S.H.`; bus stops, circled | the `SafeHouse_*` and `BusStation` markers |
| Arms dealers, circled and `A.D.` | the five `weaponshopbld_01` shop buildings; the one by Mike's Bar has no `WeaponShop` marker |
| The game's names for places, and what its text says of them, after the printed name | `REMARKS`, from `docs/docs/lore/places.md` |
| A key in the top margin | |

A remark that would cover a printed name or another note moves a little higher or lower, or to the
other side of the name, and stays put if nothing is clear.

`paper.py` then lays the print on paper: faded a fifth of the way to a yellowed paper and a tenth
greyer, an uneven tone that only ever yellows, and a fine grain. The ink goes on last as a multiply,
skipping a little on the grain, so it reads as written on the sheet rather than printed with it.

## The layer

`build_layer.py` draws the sheet and writes `layer/`, which `jackall-cli mod build` installs:

| Part | What it is |
|---|---|
| `levels\w1_*\mapcompass*.xbt` | the sheet cut into the 25 area maps, `w1_c_3`'s malaria-bubble pair included |
| `worlds\world1\mapcompass*.xbt` | the world map: the sheet's playable square, 1024 to 4096 m, shrunk to 1024 px |
| `levels\w1_*\majorlocation_*.xbt` | the 21 detail maps drawn again as town plans, each its entity's square at 1024 px, lines and lettering 3 times the sheet's |
| `player\MainCharacter\PawnPlayer*` fragments | `archGPSVehicleArchetype` emptied on the player and each character; a saved game's Datsun still showed its GPS |
| `gadgets\Equipped\Map` fragments | `archPlayerMarker` emptied: no arrow for the player on the map |

Each texture keeps the game file's header and its levels, a `_mip0` the top level alone and the other
the rest of the chain, compressed to DXT1 by `tools\third-party\FCBConverter\texconv.exe`. The textures
are drawn from the game's data and stay local; the fragments, from world 1's and world 2's entity
libraries, are committed. `python tools\fc2drive\scenarios\map.py` takes pictures of the map in the
hand on a hill, in Pala and from a Datsun's seat.

## The plugin

`MapOverhaul.dll`, an FCSE plugin (`src/`), does what the layer's data cannot, since a saved game keeps
its GPS whatever the archetypes say:

| Part | How |
|---|---|
| No GPS in the hand | the map strategy's `EquipGadgetItem` and `UnequipGadgetItem` are skipped for its own `CompassEntity` (`+0x108`), so the saved compass stays in the pocket; the monocular still comes up |
| No GPS on the dashboard | after `CVehicle::SVehicleImpl::AttachToSeat` binds the player's vehicle GPS to the dashboard and shows it, `CEntity::SetIsVisible(false)` hides it again |

`build.ps1` builds it and stages it in `layer\plugins\map-overhaul\`; the game gets it with the layer,
from `jackall-cli mod build` with every other layer installed. Its four patterns are checked on both
builds with `python ..\..\scripts\verify_patterns.py --uplay <Steam Dunia.dll> src`.

## The full map

`stitch_map.py` joins world 1's 25 area maps, `levels\w1_<row a-e>_<column 1-5>\mapcompass_mip0.xbt`,
one per level cell, into `assets/world1-map.png`: 5120 px a side at 1 m per pixel, row `a` (the highest
Y) at the top and column `1` (the lowest X) at the left. The tiles go in as stored and meet without a
seam. The picture is the game's own, so it stays local; make it with

```
python mods\map-overhaul\stitch_map.py --game "C:\Games\Far Cry 2"
```

The scripts need Pillow, numpy, scipy and matplotlib, and a Release build of `jackall-cli`.
