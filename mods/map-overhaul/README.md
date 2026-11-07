# Map Overhaul

A realistic map in place of the game's drawn one, and the GPS removed: world 1 as a military
topographic sheet, the kind handed over at a briefing, drawn from the world's own data. The idea is
issue #29, the design `docs/docs/design/map.md`. So far the whole sheet is held open in both hands on
foot, the map has lost the player's arrow, and the plugin keeps the GPS out of the hand and off the
dashboard (seen in game, 2026-11-07, from a save made with both). The sheet stays level as the view
looks up and down past it, and left mouse held reads it close; it bends at its two folds, takes the
world's light, and shows only the mission targets' icons, each at its place on the paper. In a vehicle it
is the hand's sheet where the game's map was, too big and not yet folded.
The held sheet always shows its own picture, so the area maps and town plans in the layer, and R's turn to
the world map, show nothing new on it.

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
paper's edge is 300 m outside it, in a margin that holds the title, legend, scale bar, layer tints and
north points. The printed scale is `SCALE`, 1:4 500, which makes the paper 84.4 cm in the hand.
`--region` and `--scale` draw part of the world for a closer look.

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
| `graphics\objects\mapcompass\sheet\` | the sheet in the hand (`sheet.py`): the paper at its printed size, centred on the map's root, open in 3×3 panels on hinge bones at the folds; the side columns rest 6° toward the eye and the rows flat. The paper is shaped as the clips hold it, the side columns turned 30°, and each fold between the columns is rounded over 2 cm either side of the crease, then cut along its middle into the panels on either side, so the sheet bends there; the clips' flex leaves at most a 1.2° kink (3° running). Drawn with a copy of the map's paper material named SHEET, so the game does not put the area map on it, showing the paper at 4096 px. The copy drops the `Map` flag, under which the `Generic` shader lights a map with half the sky's light whichever way it faces and never shades it below a fifth of the sun, so the paper takes the world's light as the hands do; `PAPER`, its diffuse colour 0.5, keeps it from coming out overbright |
| `graphics\characters\_common\animations\equipment\equipped_gadget\` | the map's 17 banks on foot, remade in place by `hold.py` for both hands (below) |
| `gadgets\Equipped\Map` | the map pointed at the sheet: its descriptor, a graphic object per panel and its skeleton, the four corner bones kept at the sheet's corners for the game's icons |
| `player\MainCharacter\PawnPlayer*` fragments | `archGPSVehicleArchetype` emptied on the player and each character; a saved game's Datsun still showed its GPS |
| `gadgets\Equipped\Map` fragments | also `archPlayerMarker` emptied: no arrow for the player on the map; the three `MarkerVisibility` tables keep only `Objective`, the mission targets, since the print and the notes show the rest; and the town plans' icons of what lies about and the diamonds' emptied |

Each texture keeps the game file's header and its levels, a `_mip0` the top level alone and the other
the rest of the chain, compressed to DXT1 by `tools\third-party\FCBConverter\texconv.exe`. The textures
are drawn from the game's data and stay local; the fragments, from world 1's and world 2's entity
libraries, are committed. `python tools\fc2drive\scenarios\map.py` takes pictures of the map in the
hand on a hill, in Pala and from a Datsun's seat.

### The hold

`hold.py` (Blender, through BlenderFC2) remakes each bank the map has on foot. The sheet's middle is 50
cm in front of the eye and 3 cm below it, facing it, its side columns bowed 30° toward the eye. At the 87°
field of view this install plays at, the sheet's edges lie just past a 4:3 screen's, and on 16:9 the
wrists show 1.10 of the way to the screen's edge. The game's field of view is a 4:3 screen's width and a
wider screen sees further to the sides (UFCP's `WidescreenScale`), measured against an earlier in-game
picture to within 10 px. A 75° view would leave the hands off a 16:9 screen.

The left hand holds the left column's edge 3 cm below the middle, as the idle's held the map's edge, and
the right one the right edge, mirrored. The sheet keeps half of each bank's sway of the map against the
idle, all of it in the draw and holster; where a hand would be over 92% of the arm's 52.2 cm from its
shoulder the sheet comes toward the body (5 cm walking, 10–16 cm in the draw, holster and jumps). The
columns flex 1.2° either way to the bank's rhythm, each to its own beat, and the middle column is
written at its rest too, since the game misplaces a bone left out under bones it writes (`mab.md`'s
writer rules). Running and sliding, where the
game carries the map lowered, the sheet hangs from the left hand where the bank has it. The game's map
banks are replaced at their own paths, so the map keeps its MOVE index and no MOVE fragment ships;
Inventory Overhaul's notebook is built from the map's banks in the install, so build it without this
layer.

`build_layer.py` gives `hold.py` the game's own banks, the map and the protagonist's body, copied out of
the extracted worlds archive into a small tree first. The install will not do once this layer is
deployed, since its patch then holds the banks already remade, and remaking those again throws the hands
off the sheet; nor will the whole archive, since a loose `fc2model export` from it takes an animal's
`pelvis_ref.skeleton` for the human one and poses the wrong body.

`preview_hold.py` draws the held sheet as the game's camera sees it at 87° on 16:9, with the 4:3
screen's edges in yellow, for checking a change without the game, from the pack and picture
`build_layer.py` leaves in `work/`:

```
blender -b --python preview_hold.py -- work\held.fc2model work\sheet.png _idle_ 0 preview.png
```

## The plugin

`MapOverhaul.dll`, an FCSE plugin (`src/`), does what the layer's data cannot, since a saved game keeps
its GPS whatever the archetypes say:

| Part | How |
|---|---|
| No GPS in the hand | the map strategy's `EquipGadgetItem` and `UnequipGadgetItem` are skipped for its own `CompassEntity` (`+0x108`), so the saved compass stays in the pocket; the monocular still comes up |
| No GPS on the dashboard | after `CVehicle::SVehicleImpl::AttachToSeat` binds the player's vehicle GPS to the dashboard and shows it, `CEntity::SetIsVisible(false)` hides it again |
| The sheet level as the view looks up and down (`level.cpp`) | while the map strategy's update has the map in a pawn's hands, the plugin follows that pawn's skeleton; each time it is posed, the spine and every bone below it but `Camera` are turned back about the eye by the `Camera` bone's pitch, so the arms and the sheet in the left hand keep their place against a level view; the turn eases in and out over 0.25 s as the map comes and goes. `python tools\fc2drive\scenarios\map_look.py` takes pictures from 73° down to 78° up |
| Left mouse held reads the sheet close (`read.cpp`) | the pause service's signal listener sees `startshooting` and `stopshooting` before the player's does; while the map is out a press zooms the view to 0.61 rad (35°, three times closer than 87°) over 0.3 s through the skills' first field-of-view transition, as iron sights do, with the look at 0.19 of its speed. While held, the hold also turns the upper body back about the eye's up axis by however far the look has turned since the press, so the mouse looks over a sheet that stays put; past 0.6 rad, where the sheet's edge reaches the middle of the view, the sheet comes along. Let go, the view zooms out and the sheet swings back in front over 0.3 s. `toggleicons`, the map's own use of left mouse, is taken while the map is out; right mouse still brings the monocular up, and over the zoom it takes the zoom over (not yet tried with left mouse held). `python tools\fc2drive\scenarios\map_read.py` takes pictures of it |
| The map's icons at their places (`markers.cpp`) | the game places each marker's icon by `WorldSpaceToGadgetMapSpace`, on the area map of the cell the player stands in, flat between the sheet's corner bones, and clamps the rest to the edge as arrows. While `ProcessMarker` places a marker of the map, that is answered instead with the marker's spot on the whole sheet, in the frame of the fold panel it lies on, on the bent paper and 2 mm in front of it; the bone the strategy hangs a new icon on (`+0xC0`, the lower-left corner) is that panel's until `ProcessMarker` returns, and an icon spawned before on another panel, a marker that moved across a fold, is hung again with `CEntity::Bind`. So each icon moves with its panel's bow and flex and stays on its spot as the view reads the sheet close (seen in game by script: safe houses, arms dealers and bus stops, shown for the check, sat on their notes on every panel, the bowed ones too) |

`build.ps1` builds it and stages it in `layer\plugins\map-overhaul\`; the game gets it with the layer,
from `jackall-cli mod build` with every other layer installed. Its six patterns are checked on both
builds with `python ..\..\scripts\verify_patterns.py --uplay <Steam Dunia.dll> src`; the rest of its sites
are addresses the address library maps.

## The full map

`stitch_map.py` joins world 1's 25 area maps, `levels\w1_<row a-e>_<column 1-5>\mapcompass_mip0.xbt`,
one per level cell, into `assets/world1-map.png`: 5120 px a side at 1 m per pixel, row `a` (the highest
Y) at the top and column `1` (the lowest X) at the left. The tiles go in as stored and meet without a
seam. The picture is the game's own, so it stays local; make it with

```
python mods\map-overhaul\stitch_map.py --game "C:\Games\Far Cry 2"
```

The scripts need Pillow, numpy, scipy and matplotlib, and a Release build of `jackall-cli`.
