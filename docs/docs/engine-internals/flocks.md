---
sidebar_position: 28
---

# Flocks

:::info[Verified via reverse engineering]
Code traced in `FarCry2_server` (symbolized; addresses are the server's) and matched in GOG
`Dunia.dll` (marked GOG). Data read from world 1's `entitylibrary_full.fcb` and the world 1 and 2
sector files. Claims marked *(inferred)* were not read in code or data. Nothing here was seen in game.
:::

`CBoidsComponent` is Far Cry 2's flocking system: one entity that draws and moves a cloud of small
animated models with no AI, physics or sound. Retail uses it once, for `Animals.Winged.Birds`. This
page is the ground for [fish shoals](../design/attacking-animals.md#fish-shoals), a copy of the bird
flock under water.

## The component

The whole system is about twenty functions in `BoidsComponent.cpp` (srv `0x093A1540` to
`0x093A91A0`, GOG `0x1005A080` to `0x1005E3F0`). None of them calls physics, terrain, water, raycast,
AI or player code: every callee is maths, the resource manager, or the graphic and animation
components it creates.

| Virtual | Server | GOG | What it does |
|---|---|---|---|
| `PostLoad` | `0x093A3850` | | clamps `fPitchSoftMax` to 0..80 and `fSphereRadius` to 0 and up, loads the default resources, then creates a graphic and an animation component per subgroup |
| `Finalize` | `0x093A4BC0` | `0x1005E320` | per subgroup, `InitGraphicComponent` and `InitSimpleAnimationComponent` |
| `Update` | `0x093A7880` | `0x1005DB70` | steers, moves and draws every unit each frame |
| `OnEvent`, `GetWantedEventMask` | the `CEntityComponent` base | the base | nothing: the flock receives no events or stims |

GOG's vtable (`0x10D8BEF8`, task vtable `0x10D8BEE8`) overrides only the destructor, type info,
descriptor and `Finalize`; every other slot is a shared base function.

### The archetype's fields

`RegisterProperties` (`0x093A3CC0`) exposes these, at server offsets (GOG is 4 lower):

| Field | Offset | Retail Birds | What it does |
|---|---|---|---|
| `fZoneHeight` | `+0x10` | 4 | the zone's height, centred on the entity |
| `fZoneRadius` | `+0x14` | 30 | the zone's radius around the entity |
| `fForwardSpeed` | `+0x18` | 5 | constant speed, m/s; every unit always moves at it |
| `fPitchSoftMax` | `+0x1C` | 12 | the steepest pitch, degrees, clamped to 0..80 |
| `fTurnSpeed` | `+0x20` | 3 | how fast units turn toward their target, per second |
| `fTurnSpeedCurb` | `+0x24` | 1 | slows turns toward targets far off the current heading |
| `iNbWanderers` | `+0x28` | 8 | how many units wander instead of flocking |
| `fileDefaultModel`, `fileDefaultSkeleton`, `fileDefaultAnimationName` | `+0x2C` to `+0x34` | `bird01.xbg`, `pelvis_ref.skeleton`, `flapcyclea` | used by a subgroup that leaves its own empty |
| `SubGroups` | `+0x38` | 4 subgroups | see [Subgroups](#subgroups-are-how-units-are-drawn) |
| `Banking`: `fBankingAmount`, `fBankingSpeed` | `+0x44`, `+0x48` | 0.5, 1.5 | roll into turns |
| `Boundaries`: `fBounceMinLeniency`, `fBounceMaxLeniency`, `fBounceFlockLeniency` | `+0x4C` to `+0x54` | 0.1, 0.4, 0.4 | how sharply a unit must turn back before a bounce ends |
| `Wandering`: `fYawRandom`, `fPitchRandom`, `fSphereDistance`, `fSphereRadius` | `+0x58` to `+0x64` | 500, 90, 5.5, 5 | wanderers' random walk |
| `Flocking`: `fOmniAwareRadius`, then radius, angle and weight for separation, alignment and cohesion | `+0x68` to `+0x8C` | 1; 5, −0.707, 8.5; 7.5, 0.2, 8; 9, 0, 6 | the classic three boid rules |

Three more fields are code constants, not data: a night climb's ceiling (`+0x90`, 120 m), the current
climb (`+0x94`) and its rate (`+0x98`, 8 m/s); see [Day and night](#day-and-night).

## The zone

Units live in the entity's local space, starting at random points within 1 m of its origin
(`InitGraphicComponent`, `0x093A4770`). The zone is a vertical cylinder around that origin, and it
is soft: nothing clamps a position. `SteerToArea` (`0x093A4C10`) only changes where a unit steers:

- **Height.** Once a unit is more than 0.8 × `fZoneHeight` / 2 above or below the origin, its target
  pitch is set back toward the middle, ramping to the full `fPitchSoftMax` at the zone's edge.
- **Radius.** Past 0.9 × `fZoneRadius` from the axis, the unit picks a bounce target, the point
  mirrored through the axis, and turns toward it until its heading is within a random leniency
  between `fBounceMinLeniency` and `fBounceMaxLeniency` radians. Past the full radius the target is
  renewed every frame.

So the entity's position is the middle of the zone, not its floor: a zone of height 4 spans 2 m
above and 2 m below the placed point. The entity's own rotation turns the whole zone *(inferred:
unit positions are local)*.

Units can overshoot the zone. A wanderer's pitch is clamped to `fPitchSoftMax`
(`SteerToWander`, `0x093A6100`), but a flocking unit's pitch comes from its neighbours with no
clamp (`SteerToFlock`, `0x093A6E90`), and the correction starts only inside the top fifth of the
half-height. `TurnSmooth` (`0x093A56B0`) moves each angle `min(dt × fTurnSpeed, 1)` of the way to its
target per frame, so the overshoot is roughly the climb speed, `fForwardSpeed × sin(pitch)`, times
`1 / fTurnSpeed` *(inferred from the code, not measured)*.

### Nothing collides

No unit tests the terrain, the water surface, objects or the player. A zone that reaches into a hill
puts units inside the hill, and a zone taller than the water is deep lets fish out of it.

## How units move

`Update` runs every unit through the same steps each frame:

1. **Steer.** The first `iNbWanderers` units, counted across all subgroups in order, wander: a
   random walk of yaw (`fYawRandom`, degrees per second) and pitch (`fPitchRandom`, clamped to
   `fPitchSoftMax`). The rest flock: separation, alignment and cohesion over neighbours within each
   rule's radius and angle, weighted, falling back to wandering when the sum is near zero. The flock
   step gathers neighbours over every unit, so its cost grows with the square of the unit count.
2. **Keep to the zone** (`SteerToArea`), overriding the target as above.
3. **Turn** (`TurnSmooth`) and **move** (`MoveForward`, `0x093A55A0`) along the unit's local +Y at
   `fForwardSpeed`.
4. **Bank.** Roll eases toward `fBankingAmount` × the yaw rate in degrees per second, at
   `fBankingSpeed` (inlined in GOG `Update`; `HandleRoll`, `0x093A1540`, on the server).

A model must therefore face +Y, as the birds do.

## Day and night

The flock is a daytime system. `Update` reads the time of day and compares it with two times
`CDynamicEnvironmentManager::InitSunCycleInfo` (`0x09158040`) stores: sunrise (`+0x4DC`, copied
from `+0x2E0`; `SunRiseTimeHour` 6 by default) and the point where the sun-cycle curve reaches 0.53
(`+0x4E8`), about sunset *(inferred from the curve's name, not its values)*.

- **Outside that window**, the whole flock climbs: `+0x94` grows by 8 m/s and is added to every
  unit's height. Once it passes 120 m the entity is hidden.
- **Inside it**, the entity is shown again and the climb shrinks by 8 m/s back to 0.1 m.

So birds fly up and away at dusk and come down at dawn, about 15 seconds each way, and a flock loaded
at night rises out of view at once. The ceiling, rate and window are code, not data. GOG's `Update`
does the same with the fields 4 lower (`+0x8C`, `+0x90`, `+0x94`).

## Subgroups are how units are drawn

`SubGroups` is an Rml list. `LoadSubGroups` (`0x093A31D0`) reads every child element by position, so
the element names (`Subgroup0`, `New`) do not matter, and takes four attributes:

| Attribute | Rule |
|---|---|
| `iNumberOfUnits` | capped at 99 |
| `fileModel`, `fileSkeleton`, `fileAnimationName` | each falls back to the component's default when empty |

A child with 0 units and no files is dropped; retail's trailing `<New iNumberOfUnits="0"
fileModel="" />` is the editor's empty row.

For each subgroup, `CreateGraphicAndAnimComponent` (`0x093A2230`) adds two components to the
entity:

- a **`CGraphicComponent`** with shadow casting, shadow receiving and ambient shadows turned off,
  holding one object per unit, each the subgroup's model;
- a **`CSimpleAnimationComponent`** bound to that graphic component, with the subgroup's skeleton,
  playing its one clip on a loop (`PlayAnimation(..., true)`).

One animation component drives a whole subgroup, so its units share one pose and flap in step
*(inferred: one skeleton instance per graphic component)*. That is why retail splits 18 birds into
four subgroups, each on its own flap clip, `flapcyclea` to `flapcycled`.

Every frame `Update` writes each unit's matrix with
`CGraphicComponent::SetObjectMatrixWithoutBoundingBoxUpdate` (srv `0x094A7C60`, GOG `0x1050C400`).

## Culling

The entity's bounding box is set once, at `Finalize`, when `SetObjectMatrix` (`0x094AD870`) calls
`UpdateBoundingVolume` (`0x094AD390`) over the units' start positions, all within about 1 m of the
origin. The per-frame writes never update it, so the box stays a couple of metres wide while the
units range over the whole zone. If the renderer culls the entity by that box, a flock whose centre
leaves the view vanishes even though units are still on screen *(inferred: the render-side cull was
not traced)*. The fix would be a plugin that refits the box, or a flock small enough not to show it.

## Shooting and scaring

Nothing reacts to the player:

- **No collision shape.** The flock entity carries only `CBoidsComponent`, and the component creates
  only graphic and animation components. Bullets cannot hit a unit *(inferred: hits resolve against
  physics shapes)*.
- **No events.** `OnEvent` is the base, so gunfire, explosions and stims change nothing.
- **No player input.** No steering function reads a pawn, the player or a threat.

A flock that scatters from gunfire or can be shot needs an FCSE plugin.

## Loading

Nothing about a flock is in [`depload`](../file-formats/depload.md) beyond its meshes: world 1 lists
`bird01.xbg` and `bird02.xbg` with their materials and textures, and no `depload` file anywhere lists
`birds\pelvis_ref.skeleton` or the four flap clips. `PostLoad` and `LoadSubGroups` ask the resource
manager for each model, skeleton and clip by path (`GetResource<CGeometryResource>`,
`<CSkeletonResource>`, `<CAnimationResource>`) and register each with the entity, which is what
`hidResourceCount` 7 counts: two models, one skeleton, four clips *(the count is inferred)*. Unlike a
weapon's clips, a flock's clip goes through no animation package and no MOVE state.

The flock entity is placed in a sector's `main` layer like any other, so it loads and unloads with its
sector *(inferred)*.

## Retail use

`Animals.Winged.Birds` is the only archetype with a `CBoidsComponent`. It is identical in both
worlds' libraries. `Bat`, `Bird01` and `Bird02` exist in the libraries but are placed nowhere, and no
script or brain mentions a flock.

| | World 1 | World 2 |
|---|---|---|
| Placed Birds | 46, in 44 sectors | 78, in 72 sectors |
| Layer | all `main` | all `main` |
| Override `fZoneRadius` | 33 (50 ×12, 18 to 90) | 12 (80 ×6, 20 ×3, 40) |
| Override `fZoneHeight` | 29 (10 ×6, 2.1 to 27) | 12 (20 ×6, 10 ×2, 16) |
| Height above the ground | 15 to 77 m, median 31 | 2 to 93 m, median 33 |
| Over water | 15 | 27 |

Instances override only the zone's radius and height. No instance changes speed, wanderers,
subgroups, model or clip, and each carries an empty `CEventComponent`; `hidAngles`, where present, is
yaw only. No flock's zone reaches the ground under its entity.

Heights were read against each sector's terrain grid in its `.sdat`. The same header holds the
sector's water: two flags at `0x34` and `0x38`, a level float at `0x3C` and a water material name at
`0x44` *(layout read in data; the meaning is inferred)*. Most water sectors sit at 16.0 m (413 of
496 in world 1, 551 of 671 in world 2), and placed lilies (16.00 m) and swamp boats (median 15.8 m)
agree; some ponds are higher, at 42, 72.5 or 78 m.

## Far Cry 3's fish flock

Far Cry 3 makes its small river fish with the same component. `FC3_Animals:Winged/Fish_FP` is a
`CBoidsComponent` and nothing else, with no AI, graphics, physics or sound component; it is placed
directly in world sectors, at least 85 times, and nothing spawns it.

| Field | Far Cry 2 Birds | Far Cry 3 Fish_FP |
|---|---|---|
| `fZoneHeight` | 4 | 10; placed instances 1 to 15 |
| `fZoneRadius` | 30 | 30; placed instances 8 to 50 |
| `iNbWanderers` | 8 | 12; placed instances 10 to 18 |
| `fDisappearAtNightSpeed` | no such field | 0 |
| Units | 4 + 5 + 4 + 5 | 4 × 5 |
| Models | `bird01`, `bird02` | `river_fish01` to `04` |
| Skeleton, clip | `pelvis_ref`, four flap cycles | `spine_ref` (7 bones), one `fish_fulb_swim` for all |

Every other field, speed and pitch included, keeps the birds' values. `fDisappearAtNightSpeed` is the
only field Far Cry 3 added, and the fish set it to 0, which suggests it is the night climb's rate made
tunable *(inferred: not read in `FC3.dll`)*. Far Cry 3 has no water, depth or avoidance field either,
so its fish shoals are kept in the water by placement alone.

The river fish rig is a hub, not a chain: `Spine` at the centre, `Head` 13 cm forward, `Tail` 3 cm
back, and four fin bones. Its swim clip, 1 s, animates `Tail`, `Head` and the fins and leaves
`Spine` in place *(bone masks read, their meaning inferred)*. The individual `RiverFish` and the
`Coral_Reef_Fish` school are `CAquaticAnimalAgent`s, not flocks; see
[Animals in Far Cry 3](./far-cry-3-animals.md#water-and-fish).
