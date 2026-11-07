---
sidebar_position: 7
---

# The map

**Status:** designed 2026-11-07. The sheet is drawn: world 1 as a military topographic sheet with the
previous owner's notes, by `mods/map-overhaul`, and held in the game as **Built** lists. Marked
**(proposed)** are choices not yet agreed.

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
  (`gadgets.Equipped.CompassSingle`). The one in vehicles is named by the player, in his pawn's
  `Inventory` as `archGPSVehicleArchetype` (`gadgets.Equipped.Compass_Vehicle`), on the base player
  and each of the 13 characters; the patrols' pawns leave it empty **(seen in data)**.
- **The GPS in the hand is saved with the game.** Emptying `archCompassArchetype` left it in the
  hand, because the map strategy saves its `CompassEntity` (Inventory Overhaul's test, 2026-10-27).
  Emptying the player's `archGPSVehicleArchetype` left the GPS on a Datsun's dashboard too, in a game
  loaded from a save made before (seen in game, 2026-11-07); a new game was not tried.
- **How the GPS reaches the hand and the dashboard.** The map's `OnEquip` puts its saved
  `CompassEntity` (`+0x108` on Windows, `+0xB4` on the server) in the hand with `EquipGadgetItem`,
  outside a vehicle only, and `OnUnequip` takes it back with `UnequipGadgetItem`; the monocular uses
  the same pair. The vehicle GPS is a handle in the player's inventory view (`+0x54` of its
  implementation on Windows), which `CVehicle::SVehicleImpl::AttachToSeat` binds to the vehicle's GPS
  bone and shows with `CEntity::SetIsVisible(true)`; `ReturnGPS` binds it back to the pawn and hides it
  **(RE-verified on Steam and GOG)**. Map Overhaul's plugin skips the pair for the compass and hides the
  vehicle GPS after `AttachToSeat`: neither shows, in a game loaded from a save made with both (seen in
  game, 2026-11-07).
- **The game draws its own icons over the sheet**: safe houses, locked guard posts, arms dealers,
  bus stops, masts, Mike's bar and the objective, on the area map, the town plan and the back (seen in
  game, 2026-11-07); see how it places them below.
- **The player's arrow is a marker the map names**, `archPlayerMarker`
  (`gadgets.ObjectiveIcons.PlayerPosition`) **(seen in data)**.
- **The gadget carries tables of marker kinds**: `MarkerTypes`, which kinds blink as they appear
  (`Objective` only), and `MarkerVisibility`, which kinds show on each side of the map, `World`, `Area`
  and `KeyLocation` (the town plan) **(seen in data)**. The mission targets are all kind `Objective`:
  the mission's own, the Underground's, the subvert offer, the buddies' missions and their targets, and
  the world exit; the mission givers are other kinds (`HQ`, `CellTower`, `WeaponShop`, `Underground`)
  **(seen in data)**.
- **How the map places its icons** **(RE-verified on Steam)**. Every frame the map is out,
  `UpdateMarkers` hands each marker the marker manager lists to `ProcessMarker` (Steam `0x106F1640`).
  If its kind shows on this side of the map, `WorldSpaceToGadgetMapSpace` (`0x106ED0B0`) turns its
  position into a spot on that side's picture, 0 to 1 across the cell's area map, the town plan or the
  world map, and lays it on the rectangle the corner bones span, flat; a marker off the picture is
  clamped to its edge and drawn as its arrow, turned toward it. The icon is an entity from a pool,
  hung with `CEntity::Bind` on the bone the strategy's `+0xC0` names, the lower-left corner, and moved
  there every frame. The markers' records are at the strategy's `+0x14C`, `0x1C` bytes each: the
  marker's handle, then at `+8` and `+0xC` its icon's and a second icon's.
- **While the map is out**, input set `mapcompass`: releasing left mouse sends `toggleicons`, right
  mouse sends `draw_monocular` and `holster_monocular`, and R sends `flipside`
  (`common\config\inputactionmapcommon.xml`). Left mouse also still sends `startshooting` and
  `stopshooting`, which Inventory Overhaul's notebook uses (seen in game).
- **The map strategy's `Update` runs every frame** whether the map is out or not; its equipment's
  wielder is the player while the map is out and through the holster, and no one from just after
  `OnUnequip` (seen in game, 2026-11-07, by a probe in Map Overhaul's plugin).
- **What the sheet can show** is three sets of textures: the world map, an area map for each level
  cell at 1 m a pixel, and detail maps of named places ([the map textures](../engine-internals/world-loading.md#the-map-textures)).

## Built

- **The sheet held open** (2026-11-07): 1:4 500, 84 cm, 50 cm in front of the eye with its side
  columns bowed 30°, filling a 4:3 view at the install's 87° with the hands on its edges on 16:9; the
  map's banks remade by `hold.py` (seen in game).
- **Level while the view looks up and down** (2026-11-07): the plugin turns the upper body back about
  the eye by the view's pitch while the map is out ([the look](../engine-internals/first-person-aiming.md#the-bodys-skeleton)),
  eased in and out over a quarter second (seen in game, by script, at 73° down and 78° up).
- **Left mouse held reads the sheet close** (2026-11-07): the view zooms three times closer over 0.3 s,
  the look slowed with it, and the upper body is turned back by however far the look turns, so the
  mouse moves the view across a still sheet; 0.6 rad off its middle the sheet comes along. Let go, it
  zooms out and the sheet swings back in front. The icon toggle on left mouse is gone (seen in game, by
  script: right, left and down over the sheet, let go, and the map put away while zoomed). The
  monocular raised with left mouse held is not tried.
- **Only the mission targets' icons, fixed on the paper** (2026-11-07, the user's picks): the game's
  own icons for now, the user making new ones later. Each sits at its true place on the whole sheet,
  hung on the fold panel it lies on, so it keeps its spot as the columns bow and flex and as the sheet
  is read close (seen in game, by script, with safe houses, arms dealers and bus stops shown for the
  check, each on its note, the bowed columns' too).
- **Bent, not cut, at the folds** (2026-11-07): the two folds between the columns are rounded over
  4 cm, and the bumps between creases are gone (seen in game, by script).
- **Lit as the world is** (2026-11-07): the paper no longer uses the map's lighting, which gave it half
  the sky's light whichever way it faced and never let it shade below a fifth of the sun; it takes the
  world's light as the hands do, its colour toned to 0.5 (seen in game, by script, under trees and in
  Pala's sun; the brightness is not yet judged by the user).

## Open

- **The panels' size.** The drawn sheet is 5.12 km with its margin and the area inside the neat line
  3.2 km, so 3×3 panels are about 1.7 km each over the whole sheet, or a little over 1 km each with
  the margin trimmed.
- **What the map strategy shows on the sheet**: one level cell's area map or a window that follows
  the player, how it zooms, and what the flip side shows.
- **Whether a sheet of several panels can show several area maps**, and whether bigger textures load.
- **Whether a gadget can be out while driving**, and how the lap sheet is placed.
- **The idle animation**: the map's clips in hand animation set 9.
- **The red circle**: the objective's icon is the map's `Objective` marker, an archetype the marker
  names (`gadgets.ObjectiveIcons.MissionObjective`); the user's own icons are to replace the game's.
