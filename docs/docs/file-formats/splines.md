---
sidebar_position: 15
---

# Splines — Roads, Rivers and Paths

:::info[Verified via reverse engineering]
Class and field names come from `FarCry2_server`, which keeps full C++ symbols. The layout below was
read out of the shipped `world1`/`world2` `mapsdata.fcb` files.
:::

Roads, rivers and paths are not a separate file type and are not stored per sector. All three are
splines held in the world's [`mapsdata.fcb`](./fcb.md), under three sibling containers:

| Container | Holds |
|---|---|
| `RoadSplines` | drivable roads |
| `RiverSplines` | river courses |
| `PathSplines` | foot paths |

## Placement in the file

Each container sits at `MissionLayer` → `Entity` → *container*, and one such group exists per level
cell. A campaign world therefore carries 25 groups of each kind, most of them empty because that cell
has no road, river or path of its own.

Shipped counts:

| World | Road splines | Path splines | River splines |
|---|---|---|---|
| `world1` | ~224 | ~92 | ~20 across 7 cells |
| `world2` | ~223 | ~90 | ~24 across 3 cells |

Control-point counts run from 2 to 30 per spline, well inside the editor's documented 100-point cap.

## Spline layout

A container holds a `Splines` node whose children are `Spline` records. Each `Spline` carries a
`GraphId` and four child groups:

| Group | Count | Fields |
|---|---|---|
| `ControlPoints` | N | `Position` (Vector3), `Tangent` (Vector3), `Widths` (two floats) |
| Segment bounds | N − 1 | `Center` (Vector3), `Radius` (float) |
| Arc-length samples | N − 2 | a rising curve parameter, a rising distance, an integer index |
| Spline bounds | 1 | `Center` (Vector3), `Radius` (float) |

`Position` is in global world coordinates, the same space as entity `hidPos`. `Widths` holds two
floats — 3.6 and 3.6 on a sampled road — which matches a left and right half-width.

Only `Position`, `Tangent` and `Widths` are authored. The segment bounding spheres, the arc-length
table and the whole-spline bounding sphere are derived from the control points and have to be
recomputed by anything that edits a spline.

Alongside `Splines`, `RoadSplines` carries a short list of records holding only a `GraphId`. Its
purpose is not identified.

## Runtime use

Two independent systems consume these splines:

- `CSplinePrimitiveComponent`, a renderable component, draws them.
- `CTaskSplinePathFind`, `CTaskGetClosestSplinePos` and `CTaskCheckPosOnSpline` navigate along them,
  so vehicles and NPCs follow spline geometry.

An incorrect edit therefore affects AI navigation as well as appearance.

## Editor-side classes

The stock editor's own spline objects are separate from the runtime ones: `CFCXEditorSplineManager`,
`CFCXEditorSplineRoad`, `CFCXEditorSplineZone` and `CFCXEditorToolRoad`, with the API surface exposed
as `FCE_SplineManager_CreateRoad`, `FCE_SplineManager_DestroyRoad`, `FCE_SplineRoad_GetWidth` and
`FCE_SplineRoad_SetWidth`. In the editor's authoring output the equivalent data is written by
`SplineManager` into [`ige/map.xml`](./fc2map.md#authoring-source).

## Open questions

:::caution[Open]
- How a spline group is bound to its level cell — the identifying fields on the parent `Entity` have
  not been read.
- What the `GraphId`-only record list under `RoadSplines` is for.
- Whether road surface geometry is generated from the spline at load time or baked into sector meshes.
:::
