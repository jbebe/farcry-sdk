---
sidebar_position: 7
---

# The map

**Status:** designed 2026-11-07. The sheet is drawn: world 1 as a military topographic sheet with the
previous owner's notes, by `mods/map-overhaul`. Nothing is in the game yet, and none of the engine
findings below has been tested. Marked **(proposed)** are choices not yet agreed.

## The goal

- **A used military map someone gave you before the mission**, and no GPS.
- **Not a small rectangle in the hand.** The sheet is held open in both hands, and the player looks
  around past it at the ground it shows.

## Decisions

- **The sheet** (2026-11-06) is a NATO-style topographic sheet drawn from the world's own data:
  height-band tints, contours, white roads with bridges as part of them, buildings, grid and margin,
  faded a little on paper. Over it the previous owner's notes in blue ballpoint: a diamond and
  `G.P.` at each guard post, `S.H.` at safe houses, `A.D.` at arms dealers, bus stops, and the game's
  names for places with what is said of them. `mods/map-overhaul/README.md` has the details.
- **Held in two hands, open in full** (2026-11-07): the whole sheet, in 3×3 fold panels, so there is
  nothing to refold as the player moves.
- **No "you are here"**: the game's player arrow goes, and the player finds himself by the ground.
- **The sheet does not follow the camera up and down.** It stays level in front of the player and
  turns only with him, so he can look up past it at the hills around him while holding it.
- **Left mouse held: the camera moves in on the sheet**, and the mouse moves the view across it. It
  replaces left mouse's icon toggle.
- **Right mouse held: the monocular**, as now.
- **In a vehicle, a small part of the sheet lies in the lap**, about the size of the game's map, the
  paper folded crudely.
- **No GPS**: none in the hand, none in vehicles.
- **Objectives are a red sharpie circle** around the mission's location.
- **The sheet in the hand is fold panels**, flat planes with slight bumps and creases at the bent
  edges, like a map opened and folded a million times, and it has a realistic idle animation.

## What the engine gives us

- **The map is a gadget held in both hands.** `gadgets.Equipped.Map` has `bUseBothHands`, hand
  animation set 9 and the map strategy ([inventory](inventory.md#what-the-engine-gives-us)).
- **The sheet is 25.5 cm square**, its corners four bones of the gadget's own skeleton:
  `UpperLeftCorner` at (−0.109, 0.130, 0.065) and `LowerRightCorner` at (0.146, −0.125, 0.065), named
  in the gadget's `sMapCorner*Bone` fields. The markers are placed between them **(seen in data)**.
- **The GPS is a gadget of its own.** The map names the one in the hand, `archCompassArchetype`
  (`gadgets.Equipped.CompassSingle`), and each vehicle names its own, `archGPSVehicleArchetype`
  (`gadgets.Equipped.Compass_Vehicle`), which the patrol vehicles leave empty **(seen in data)**.
- **The player's arrow is a marker the map names**, `archPlayerMarker`
  (`gadgets.ObjectiveIcons.PlayerPosition`) **(seen in data)**.
- **The gadget carries a table of marker kinds**: `Objective` on; `PGP`, `SafeHouse`, `CellTower`,
  `WeaponShop`, `BusStation`, `Underground`, `HQ`, `MikesPlace` and `Other` off **(seen in data)**.
  What the table decides was not traced.
- **While the map is out**, input set `mapcompass`: releasing left mouse sends `toggleicons`, right
  mouse sends `draw_monocular` and `holster_monocular`, and R sends `flipside`
  (`common\config\inputactionmapcommon.xml`).
- **What the sheet can show** is three sets of textures: the world map, an area map for each level
  cell at 1 m a pixel, and detail maps of named places ([the map textures](../engine-internals/world-loading.md#the-map-textures)).

## Open

- **The panels' size.** The drawn sheet is 5.12 km with its margin and the area inside the neat line
  3.2 km, so 3×3 panels are about 1.7 km each over the whole sheet, or a little over 1 km each with
  the margin trimmed.
- **Where the first-person map is placed** relative to the camera, so its pitch can be dropped.
- **What the map strategy shows on the sheet**: one level cell's area map or a window that follows
  the player, how it zooms, and what the flip side shows.
- **Whether a sheet of several panels can show several area maps**, and whether bigger textures load.
- **The camera for left mouse**: moving in on the sheet and panning across it with the mouse.
- **Whether a gadget can be out while driving**, and how the lap sheet is placed.
- **The idle animation**: the map's clips in hand animation set 9.
- **The red circle**: what draws the objective marker, so a circle can replace it.
