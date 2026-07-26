---
sidebar_position: 7
---

# Dunia.dll — The Editor-Facing API Surface (`FCE_*`)

Part of the Dunia.dll note set — see [the overview](./overview.md) for the binary identification.
Companion to [the Lua-exposed API surface](./lua-api-surface.md): that page maps the *mission/gameplay*
scripting surface (`RegisterLuaBinding`); this page maps a completely different, non-scripted surface
— the flat C API the stock map editor (`FC2Editor.exe`) uses to drive the engine directly via P/Invoke.

## Source: the stock editor's own decompiled interop layer

[`tools/third-party/FC2Editor_Source/`](../modding/sources.md) is decompiled C# source for the actual
shipped Far Cry 2 map editor — confirmed genuine via `AssemblyInfo.cs`'s
`AssemblyCopyright("Copyright (C) 2008 Ubisoft Entertainment")`, not a community reimplementation.
It's a thin WinForms shell: every `FC2Editor.Nomad` class is a managed wrapper around
`[DllImport("Dunia.dll")] private static extern ...` declarations, all named with an `FCE_` prefix
(`F`ar`C`ry`E`ditor). **338 such externs exist**, exclusively in `FC2Editor.Nomad` (`FC2Editor.Tools`
has none — its classes only call up into the `Nomad` wrappers).

**Confirmed behavior**: all 338 names already exist as correctly-named exports in this project's
`Dunia.dll` — the PC binary's export table itself carries these names, independent of anything in the
C# source. What it *didn't* have was correct signatures: every one of these functions decompiled as
a bare `undefined FCE_Xxx(void)` regardless of its real parameter count (e.g.
`FCE_CollectionManager_WriteMaskCircle` showed as taking no arguments, despite genuinely taking five:
`cx, cy, radius, id, update`). Since the C# `extern` declarations are Microsoft's own P/Invoke
marshaling — i.e. ground truth for the real calling convention, parameter count, order, and types —
they were used to set a correct typed prototype on every matching Ghidra function.

**Result**: 317 of 338 addresses updated (316 unique names; `FCE_Document_Export` exists at two
separate addresses in the binary, `0x1082b750` and `0x10a21200`, both updated identically). 100% of
resolved addresses took their new prototype without error. 22 names from the C# source have no
matching export in this binary at all — see [Not found](#not-found-in-the-binary) below; no address
was guessed for any of them. Two exports exist in the binary that this particular editor build never
calls (`ShutdownDuniaEngine`, `FCE_ObjectRenderer_SetActive`) — left untouched, confirming the export
table is a superset of what this WinForms shell consumes.

This means every function below now has a **decompiler-confirmed, source-verified signature** in the
Ghidra project — not inferred from disassembly, but taken directly from the original developers' own
interop layer.

## The API, grouped by subsystem

### Camera
Free-fly editor camera. `Position`/`Angles` (get/set), `FrontVector`/`RightVector`/`UpVector` (get,
orthonormal basis), `FOV`/`Speed`/`SpeedFactor`, `Input_Forward`/`Input_Lateral` (movement injection),
`Rotate(pitch, roll, yaw)`.

### Engine
Lifecycle: `InitDuniaEngine(hInstance, focusWnd, renderWnd, cmdLine, launchGame, forceGpuSync,
messagePumpCallback)` → `RunDuniaEngine` → `TickDuniaEngine` (per-frame pump) → `CloseDuniaEngine`.
Also `AutoAcquireInput`, `PersonalPath` (user data dir), `StormFactor`, `TimeOfDay`, `ConsoleOpened`,
`UpdateViewport(sizeX, sizeY)`.

### Localizer
`LocalizeText(section, text)` — the one Dunia export *not* `FCE_`-prefixed; backs all in-editor string
localization (`InGameEditor_PC`/`InGameEditor` sections).

### EditorDocument
Map lifecycle: `Load`/`Save(mapPath, mapName)`, `Reset`, `Validate`, `FinalizeMap`,
`Export(mapFile, exportPath, toConsole)`. Metadata: `AuthorName`/`CreatorName`/`MapName` (strings),
`BattlefieldSize`/`PlayerSize` (enums). Snapshot: `ClearSnapshot`, `IsSnapshotSet`,
`SnapshotAngle`/`SnapshotPos`, `TakeSnapshot`.

### TerrainManager / TerrainManipulator
`TerrainManager`: `WaterLevel`, `GetHeightAt(x, y)`, texture-layer slot binding
(`AssignTextureId`/`ClearTextureId`/`GetTextureEntryFromId`).

`TerrainManipulator` — the terrain sculpting brushes (each a `Begin`/apply/`End` triple, matching the
brush-model hotkeys F1–F7 already documented in [engine-theory](../modding/engine-theory.md)):
`Bump`, `Erosion(radius, density, deformation, channelDepth, randomness)`, `Grab(ratio)`,
`Noise(numOctaves, noiseSize, persistence, NoiseType)`, `RaiseLower`, `Ramp(ptStart, ptEnd, radius,
hardness)`, `SetHeight`, `Smooth`, `Terrace(height, falloff)`.

### Collections (foliage/prop scattering)
`CollectionInventory`: tree navigation (`GetRoot/GetChild/GetChildCount/GetDisplay/GetParent`) plus a
burn-profile flag. `CollectionManager`: `AssignCollectionId`/`ClearMaskId`/`GetCollectionEntryFromId`
(slot binding), `UpdateCollections(rect)` (refresh scattered instances), `WriteMaskCircle`/
`WriteMaskSquare(cx, cy, radius, id, update)` (paint the density mask). `CollectionManipulator`:
`Paint`/`Paint_End` (interactive brush).

### CoordinateSystem
`GetAxisFromAngles`/`GetAnglesFromAxis` — Euler ↔ 3-axis orthonormal basis, both directions.
`GetAnglesFromDir` (in `Vec3.cs`) — direction vector → Euler angles.

### Editor
Callback registration (engine → managed, all `void*` function pointers): `Update_Callback`,
`Event_Callback`, `LoadCompleted_Callback`, `SaveCompleted_Callback`, `EnableUI_Callback`. State:
`IsIngame`, `IsInitialized`, `IsLoadPending`, `ValidateIngame`, `ToggleIngame`, `GetFrameTime`.
Screen/world conversion: `GetScreenPointFromWorldPos`, `GetWorldRayFromScreenPoint`. Raycasts:
`RayCastPhysics`/`RayCastPhysics2` (object-ignore vs. selection-ignore overloads), `RayCastTerrain`.

### EditorObject
Per-instance placed-object handle. Lifecycle: `Create_FromEntry(entry, managed)`, `AddRef`/`Release`,
`Clone`, `Destroy`. State: `IsLoaded`, `GetEntry`, `Position`/`Angles`, `GetBounds(world)`,
`IsVisible`, `SetHighlight`, `SetFreeze`. Placement: `DropToGround(physics)`,
`ComputeAutoOrientation(pos, angles, normal)`, `GetPivot`/`GetClosestPivot(minDist)`,
`SnapToClosestObject`, `GetPhysEntities`.

### EditorObjectSelection
Multi-object selection set. Membership: `Create`/`Destroy`, `Get(index)`/`Count`, `Add`/`AddSelection`,
`Clear`, `Clone(cloneObjects)`, `Delete`, `ToggleObject`/`ToggleSelection`,
`GetValidObjects`/`RemoveInvalidObjects`. Geometry: `GetCenter`/`SetCenter`, `ComputeCenter`,
`GetWorldBounds`. Transform: `MoveTo(pos, MoveMode)`, `Rotate(angle, axis, pivot, affectCenter)` (plus
`Rotate3`/`RotateLocal3`/`RotateGimbal` per-axis variants), `DropToGround(physics, group)`,
`SnapToPivot(source, target, preserveOrientation, snapAngle)`, `SnapToClosestObjects`. Undo-adjacent:
`ClearState`/`LoadState`/`SaveState`.

### EditorSettings
Global editor toggles — visibility (Grid, Icons, Fog, Water, Shadow, Collections), snapping
(AutoSnappingObjects, AutoSnappingObjectsRotation, AutoSnappingObjectsTerrain,
SnappingObjectsToTerrain, GridResolution), gameplay/dev (Invincible, KillDistanceOverride,
SoundEnabled, CameraClippedTerrain, `EngineQuality`: Low…UltraHigh/Optimal/Custom).

### Gizmo
The 3D move/rotate manipulator widget. `Create`/`Destroy`, `Position`, `Axis` (3-basis-vector frame),
`Active` (`FC2Editor.Nomad.Axis` enum: X/Y/Z/XY/XZ/YZ), `Redraw`, `Hide`,
`HitTest(raySrc, rayDir) → Axis`.

### Small value-type helpers
`ImageMapEngine`: `GetSize`, `ConvertTo24bit(data, stride, min, max)`, `Clone`. `Points`:
`Create`/`Destroy` — opaque point-list handle used by terrain wireframe drawing. `PaintBrush`:
`Create(circle, radius, hardness, opacity, distortion)` — the shared brush descriptor used across
every terrain/texture/collection paint call. `PhysEntityVector`: physics-entity handle list used by
raycast-ignore and drop-to-ground calls. `Snapshot`: `Create(width, height)`, `GetData(data, width,
height, pitch)` — offscreen render target for object thumbnails.

### Render
Immediate-mode debug/gizmo drawing: `BeginGroup`/`EndGroup`, `Arrow`, `Dot`, `SegmentedLineSegment`,
`WireBoxFromBottomZ`, `WireRegionFromTerrain`, screen-space `ScreenCircleOutlined`/
`ScreenRectangleOutlined`, and terrain-projected `Terrain_Circle`/`Terrain_Square(center, radius,
penWidth, color, zOrder)` — the brush-radius preview rings visible while sculpting/painting.

### ObjectInventory / ObjectManager / ObjectRenderer / ObjectViewer
`ObjectInventory`: placeable-object catalog tree, plus pivot/orientation metadata (`GetPivotCount`,
`IsAutoOrientation`, `IsAutoPivot`, `GetZOffset`). `ObjectManager`:
`GetObjectFromScreenPoint(includeFrozen, ignore)` (click-to-pick raycast), `GetObjectsFromMagicWand`
(select-similar), `GetObjectsFromScreenRect` (marquee select), `UnfreezeObjects`. `ObjectRenderer`/
`ObjectViewer`: async thumbnail-render pipeline for the object browser (`RenderObject(entry)` →
offscreen `Snapshot` → cached PNG by entry ID) plus the preview-viewport pane.

### Splines (roads/zones)
`Spline` (base class): `Create`, `AddPoint`/`InsertPoint`/`RemovePoint`/`RemoveSimilarPoints`/`Clear`,
point indexer, `HitTestPoints`/`HitTestSegments`, `OptimizePoint`, `Draw(penWidth, controller)`,
`FinalizeSpline`, `UpdateSpline`/`UpdateSplineHeight`. `SplineController`: selection/editing overlay
(`SetSpline`, `SelectFromScreenRect(rect, penWidth, SelectMode)`, `MoveSelection`, `DeleteSelection`).
`SplineInventory`: road-type preset catalog tree. `SplineManager`: `CreateRoad(id)`/`DestroyRoad(id)`,
`GetRoadFromId(id)`, `GetPlayableZone()` — road slots (max 8) plus the single playable-area zone.
`SplineRoad`/`SplineZone` extend `Spline` with `Entry`/`Width` and `Reset()` respectively.

### TextureInventory / TextureManipulator
`TextureInventory`: texture catalog tree. `TextureManipulator`: `Paint(center, amount, id, brush)` —
terrain texture-layer painting — plus `PaintConstraints_Begin(minHeight, maxHeight, heightFuzziness,
minSlope, maxSlope)`/`PaintConstraints`/`PaintConstraints_End` — a height/slope-constrained
auto-texturing pass (e.g. "paint rock only above this slope").

### UndoManager
`RecordUndo`/`CommitUndo`, `Undo`/`Redo`, `UndoCount`/`RedoCount` — the transaction API wrapped around
every editing operation above.

### Validation
`Validation.ValidateGame()`/`ValidateGameMode(GameModes)` → `ValidationReport` runs the map-integrity
checks referenced by `ToolValidation.cs`. `ValidationReport`: `GetCount`, `GetRecord(index)`.
`ValidationRecord`: `GetFlags` (`None`/`Validation = 0x20`), `GetMessage`, `GetObject` →
`EditorObject`.

### BudgetManager
`MemoryUsage` (int) / `ObjectUsage` (float, fraction-of-budget) — the resource-budget meters shown in
the editor UI.

### Wilderness (procedural terrain generation) — see below
`GenerateDesert(gradientWidth, gradientHeight, distorsion, noiseAdd, blurRadius)`. Three ways to run a
**"Wilderness script"** against the terrain: `RunScript(scriptName)`, `RunScriptBuffer(buffer,
mapCallback, errorCallback)`, `RunScriptEntry(entry)`. `WildernessInventory`: catalog tree of saved
Wilderness scripts.

## Open lead: a self-documenting script-reflection API

`Wilderness.cs` also exposes a small, separate reflection surface: `NumFunctions` (get) →
`FCE_Script_GetNumFunctions`, and `GetFunction(index)` → `FCE_Script_GetFunction`, returning a
`FunctionDef` handle whose `.Name` / `.Prototype` / `.Description` properties are backed by
`FCE_ScriptFunction_GetName` / `FCE_ScriptFunction_GetPrototype` / `FCE_ScriptFunction_GetDescription`.

This is distinct from the `RegisterLuaBinding` mission-scripting surface in
[lua-api-surface.md](./lua-api-surface.md) — it's the introspection API for whatever language drives
`FCE_Wilderness_Script`/`RunScriptBuffer` procedural terrain generation. `GetName` and `GetDescription`
now have confirmed prototypes in Ghidra; `GetPrototype` wasn't found under that exact name (see below).

**Not attempted yet, but worth a dedicated session**: calling `FCE_Script_GetNumFunctions` and
iterating `FCE_Script_GetFunction` would enumerate every built-in Wilderness-script function by name,
signature, *and description* straight out of the running engine — a complete language reference
without touching a single script file, the same way the editor itself must populate a
function-autocomplete/help panel.

## Not found in the binary

22 of the 338 C#-declared names have no matching export in this build of `Dunia.dll` — verified
individually, not just via a bulk substring search; no address was guessed for any of them:

`FCE_ImageMap_Destroy`, `FCE_Inventory_Object_AddPivot`, `FCE_Inventory_Object_ClearPivots`,
`FCE_Inventory_Object_GetParent`, `FCE_Inventory_Object_SavePivots`,
`FCE_Inventory_Object_SetAutoPivot`, `FCE_Inventory_Object_SetPivot`,
`FCE_Inventory_Object_SetPivots`, `FCE_Inventory_Object_SetZOffset`,
`FCE_Inventory_Spline_GetParent`, `FCE_Inventory_Texture_GetDisplay`,
`FCE_Inventory_Texture_GetParent`, `FCE_Inventory_Wilderness_GetDisplay`,
`FCE_Inventory_Wilderness_GetParent`, `FCE_PhysEntityVector_Create`,
`FCE_ScriptFunction_GetPrototype`, `FCE_Spline_Destroy`, `FCE_Spline_GetNumPoints`,
`FCE_SplineController_Destroy`, `FCE_ValidationRecord_GetSeverity`, `FCE_ValidationReport_Destroy`,
`FCE_Wilderness_Desert`.

Most cluster into two shapes: pure mutators on inventory entries (`SetPivot`, `SetZOffset`,
`SetAutoPivot`, `AddPivot`, `ClearPivots`, `SavePivots`), or `Destroy`/`GetParent`/`GetDisplay`
siblings of functions that *were* found right next to them in the same class. Reads as the linker
folding/deduplicating near-identical destructor or trivial-getter stubs across the four parallel
inventory classes (Object/Spline/Texture/Wilderness all share the same shape) — plausible, but
unconfirmed; genuinely absent under this exact name either way, not chased further here.

A quirk worth remembering when reading other decompiled signatures in this DLL: a `void`-looking C#
wrapper doesn't always mean the native function is `void` — `FCE_SplineZone_Reset` and the
`FCE_ScriptFunction_Get*` family return a pointer that their C# callers silently discard.
