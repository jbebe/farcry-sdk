---
sidebar_position: 8
---

# Dunia.dll — The "Wilderness Script" Procedural Terrain Language

Part of the Dunia.dll note set — see [the overview](./overview.md) for the binary identification, and
[the editor API surface notes](./editor-api-surface.md) for how the map editor drives the engine in
general. This page resolves the "open lead" flagged there: a small, self-documenting reflection API
in `Wilderness.cs` (`FCE_Script_GetNumFunctions`/`GetFunction` → `.Name`/`.Prototype`/`.Description`)
turned out to be introspection over a genuine, fully-fledged scripting language for procedural terrain
generation — and its entire builtin function set, with full call signatures and descriptions, was
recovered **directly from static data in the binary**, with no need to run the game or editor at all.

## How it was found

`FCE_Script_GetNumFunctions` (`0x1088f860`) and `FCE_Script_GetFunction` (`0x1088da00`) decompile to:

```c
int FCE_Script_GetNumFunctions(void) {
  return *(int *)(DAT_11650a68 + 0x3c);
}
void * FCE_Script_GetFunction(int index) {
  return (void *)(index * 0x10 + *(int *)(DAT_11650a68 + 0x38));
}
```

`DAT_11650a68` is a global singleton pointer to the script-manager object; offset `0x38` holds the
function-table array pointer, `0x3c` its count. Each entry is a 16-byte, 4-field struct — confirmed by
the two accessor functions the C# source calls:

```c
void * FCE_ScriptFunction_GetName(void *ptr)        { return *(void **)ptr; }         // offset 0
void * FCE_ScriptFunction_GetDescription(void *ptr) { return *(void **)(ptr + 8); }   // offset 8
```

(`FCE_ScriptFunction_GetPrototype` wasn't found under that exact export name — see [the API surface
page's "not found" list](./editor-api-surface.md#not-found-in-the-binary) — but its slot is obvious
from the struct's construction below: offset 4.)

Tracing the one write site for `DAT_11650a68` (`FUN_108342a0`, the overall editor-context constructor)
leads to `FUN_10889440`, the table's actual builder. It's a long, flat sequence of `{name, prototype,
description, handlerFnPtr}` literal assignments — e.g.:

```c
local_18 = "GenerateCircle";
pcStack_14 = "map = GenerateCircle(x, y, radius, hardness, falloff, opacity);";
local_10 = "Generate a map containing a circle.\nx, y: Center position of the circle, in meters. "
           "(0, 0) is the center of the map.radius: The radius, in meters, of the circle.\n"
           "hardness: A factor between 0 and 1 representing the flat area of the circle.\n"
           "falloff: The exponent representing the shape of the falloff. 1 is linear.\n"
           "opacity: A factor between 0 and 1 representing the opacity of the circle.";
pcStack_c = (code *)&LAB_10888e00;   // native handler
puVar4 = FUN_100f7a50(param_1[0xf] * 0x10 + *piVar1, 1);  // table[count++] = {name, prototype, description, handler}
*puVar4 = CONCAT44(pcStack_14, local_18);
puVar4[1] = CONCAT44(pcStack_c, local_10);
```

Every one of the 37 registrations follows this exact shape — this is effectively the developers' own
in-source API documentation for the language, compiled straight into the shipped binary and normally
only visible one function at a time through the editor's script-help UI. Confirmed struct layout:
`{char* name; char* prototype; char* description; void (*handler)();}`, 16 bytes.

## The language

"Wilderness Script" is a small procedural-generation DSL: functions take/return opaque **map** handles
(2D scalar fields over the terrain) and **noise** object handles, composed via generator → operator →
apply-to-world steps. Statement syntax visible directly in the prototype strings themselves (e.g.
`map = GenerateCircle(x, y, radius, hardness, falloff, opacity);`) — assignment, C-style call syntax,
semicolon-terminated statements. Invoked via `FCE_Wilderness_Script(scriptName)` (load a script file by
name) or `FCE_Wilderness_ScriptBuffer(buffer, size, mapCallback, errorCallback)` (run from an in-memory
string, with a per-line error callback — suggesting scripts are plain text, parsed line-by-line).
`WildernessInventory` catalogs saved scripts as browsable inventory entries, run via
`FCE_Wilderness_ScriptEntry`. `FCE_Wilderness_Desert(gradientWidth, gradientHeight, distorsion,
noiseAdd, blurRadius)` is a separate, hardcoded native desert generator — not part of this scripted
language — plausibly one of several canned generators behind the **"File → New Map of Nature"** biome
pregeneration feature already noted in [Engine Theory](../modding/engine-theory.md); the general
scripted path is a strong candidate for how the other biome presets (Savannah, Jungle) are implemented,
if they aren't equally hardcoded native functions.

### Random

| Function | Description |
|---|---|
| `number = GetRandomNumber();` | Random number between 0 and 1. |
| `number = GetRandomRange(min, max, [optional] override);` | Random number between `min`/`max` (inclusive); `override`, if set, always returns that number. |

### Object lifecycle

| Function | Description |
|---|---|
| `noise = AllocateNoise(numOctaves, size, persistence);` | Create a noise object. `numOctaves`: detail level. `size`: meters, size of the first octave. `persistence`: multiplier applied to `size` after each octave. |
| `Release(obj);` | Release an allocated map or noise object. |

### Map generators

| Function | Description |
|---|---|
| `map = GenerateConstant(value);` | Map filled with a single value. |
| `map = GenerateLine(centerX, centerY, angle, width, height, falloff, distortNoise, distortRadius);` | A single distorted line. `angle` in degrees; `falloff` exponent (1 = linear); `distortNoise`/`distortRadius` bend the line path. |
| `map = GenerateLines(angle, width, height, falloff, distortNoise, distortRadius);` | Same as above, multiple lines. |
| `map = GenerateCircle(x, y, radius, hardness, falloff, opacity);` | A circle. `hardness` 0–1 = flat-top fraction of the radius. |
| `map = GenerateNoise(numOctaves, size, persistence);` | Noise map, values -1..1, built inline without a separate noise object. |
| `map = GenerateNoiseObj(noise);` | Noise map from a pre-allocated noise object (reusable across multiple generates). |
| `map = GenerateFromMap(sourceMap);` | Copy of another map. |
| `map = GenerateDistortion(sourceMap, numOctaves, size, persistence, distortionRadius);` | Noise-distorted copy of a source map. |
| `map = GenerateSlope(sourceMap);` | Slope of a source (height) map, in delta meters. |
| `map = GenerateConstraints(heightMap, slopeMap, minHeight, maxHeight, heightFuzziness, minSlope, maxSlope);` | 1/0 mask where height+slope constraints hold. **Identical parameter set to the manual Texture Painter's constraint-paint mode** (`ToolTexture.cs`'s `PaintConstraints_Begin` — see [Data Recipes](../modding/data-recipes.md)) — same underlying algorithm exposed twice, once as a UI brush mode, once as a script primitive. |

### Map operators (`Apply*`, in-place)

| Function | Description |
|---|---|
| `ApplyAdd(map, value/valueMap)` | Add a constant or another map's values. |
| `ApplyMultiply(map, value/valueMap)` | Multiply by a constant or another map's values. |
| `ApplyMin(map, value/valueMap)` / `ApplyMax(map, value/valueMap)` | Clamp against a constant or map, per-cell. |
| `ApplyInvert(map)` | Invert values. |
| `ApplyClip(map, minValue, maxValue)` | Hard-clip out-of-range values. |
| `ApplyExtract(map, minValue, maxValue, newMinValue, newMaxValue)` | Extract a value range and redistribute it into a new range. |
| `ApplyResize(map, newMinValue, newMaxValue)` | Redistribute the map's full range into `[newMinValue, newMaxValue]`. |
| `ApplyNormalize(map)` | Redistribute into `[0, 1]`. |
| `ApplyStretch(map, center, stretch)` | Multiply values around `center` by a `stretch` factor. |
| `ApplyFalloff(map, falloff)` | Apply a falloff exponent (best on a normalized map). |
| `ApplyBlur(map, radius, [optional] maskMap)` | Blur by radius in meters, optionally masked. |
| `ApplyThreshold(map, minValue, maxValue, newValue)` | Set all values in range to `newValue`. |
| `ApplyThresholdFalloff(map, minValue, maxValue, falloff)` | Falloff applied only within a value range. |
| `ApplyErosion(map, density, deformation, channelDepth)` | Erosion simulation. **Identical parameter names to `TerrainManipulator.Erosion`/`ToolTerrainErosion.cs`'s manual F7 brush** (see [Data Recipes](../modding/data-recipes.md)) — the same erosion algorithm again exposed both as a UI brush and a script primitive (this one omits the brush-only `randomness` parameter). |

### Applying results to the world

| Function | Description |
|---|---|
| `TerrainSetMap(map)` | Use a map as the world's heightmap. |
| `TextureSetId(slot, id)` | Assign a texture to slot 0–3. |
| `TextureFill(slot)` | Fill the entire texture map with one slot. |
| `TexturePaint(slot, map)` | Paint a texture slot according to a normalized map. |
| `CollectionSetId(slot, id)` | Assign a foliage/prop collection to slot **-1 to 7** (-1 = none) — confirms 8 real collection slots, matching the road-slot cap (max 8) documented on [the editor API surface page](./editor-api-surface.md#splines-roadszones). |
| `CollectionClear()` | Remove all collections from the world. |
| `CollectionPaint(slot, map, minValue, maxValue)` | Paint a collection slot wherever the map's value falls in `[minValue, maxValue]`. |
| `WaterSetLevel(level)` | Set world water level, in meters. |

37 functions total, spanning: 2 random, 2 lifecycle, 11 generators, 15 operators, 7 world-application
calls (add up to 37, `GenerateConstraints`/`ApplyErosion` counted once each above).

## Why this matters

This is a **complete, developer-authored language reference**, extracted with zero runtime execution
— purely from decompiling one registration function. It means:

- A modder could, in principle, hand-write a Wilderness script (plain text, `var = Func(args);` per
  line) and load it via `FCE_Wilderness_Script`/`RunScriptBuffer` to procedurally paint terrain,
  textures, and foliage — height/slope-constrained biome generation, entirely scripted, without
  hand-sculpting.
- The overlap between this language's `GenerateConstraints`/`ApplyErosion` and the manual Texture
  Painter/Erosion brush tools (same parameter names, same likely underlying C++ implementation) is
  strong evidence the map editor's own "auto-texture" and "erosion" brush UIs are themselves thin
  wrappers calling into this same scripting layer, not a separate code path.
- **Not yet found**: any actual `.ws`-style script file (or whatever extension `FCE_Wilderness_Script`
  expects) inside the game's shipped archives, which would show real syntax in practice (comments,
  variable naming conventions, whether biome presets like Savannah/Jungle are implemented this way vs.
  as hardcoded natives like `FCE_Wilderness_Desert`). Worth a targeted search of `worlds.fat`/`common.fat`
  for a matching extension or a `wilderness`/`nature` folder next time either archive is being browsed.
