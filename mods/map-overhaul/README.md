# Map Overhaul

A realistic map in place of the game's drawn one, and the GPS removed: world 1 as a military
topographic sheet, the kind handed over at a briefing, drawn from the world's own data. Nothing is in
the game yet; the idea is issue #29.

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
| A sketched diamond on each guard post, and `!GP!` | the 32 `Objectives.PGP.*` markers in `mapsdata` |
| Safe houses, a sketched house and `SH`; bus stops, circled | the `SafeHouse_*` and `BusStation` markers |
| The game's names for places, and what its text says of them, after the printed name | `REMARKS`, from `docs/docs/lore/places.md` |
| A key in the top margin | |

A remark that would cover a printed name or another note moves a little higher or lower, or to the
other side of the name, and stays put if nothing is clear.

`paper.py` then lays the print on paper: faded a fifth of the way to a yellowed paper and a tenth
greyer, an uneven tone that only ever yellows, and a fine grain. The ink goes on last as a multiply,
skipping a little on the grain, so it reads as written on the sheet rather than printed with it.

## The full map

`stitch_map.py` joins world 1's 25 area maps, `levels\w1_<row a-e>_<column 1-5>\mapcompass_mip0.xbt`,
one per level cell, into `assets/world1-map.png`: 5120 px a side at 1 m per pixel, row `a` (the highest
Y) at the top and column `1` (the lowest X) at the left. The tiles go in as stored and meet without a
seam. The picture is the game's own, so it stays local; make it with

```
python mods\map-overhaul\stitch_map.py --game "C:\Games\Far Cry 2"
```

The scripts need Pillow, numpy, scipy and matplotlib, and a Release build of `jackall-cli`.
