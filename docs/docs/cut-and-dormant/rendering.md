---
sidebar_position: 7
---

# Rendering — present but off, gated or capped

:::info[Verified via reverse engineering]
Code traced in the Steam **`Dunia.dll`** (v1.03); addresses are Steam's unless marked. The shader side
is read from the retail permutation lists (`fastinitdata_d3d9.bin`, `fastinitdata_d3d10.bin`) and the
compiled shader objects, which `index.pso` and `index.vso` resolve per permutation. Config values are
read from the retail `defaultrenderconfig.xml` (the 1.03 `patch.dat` copy is identical to
`common.dat`'s). Config keys were also checked against the strings of the server (`FarCry2_server`,
the symbolized Linux dedicated server) and of the GOG `Dunia.dll`. Nothing on this page was seen in
game: where a section says what a feature looks like, that comes from the shader source or the code
path, not from a screenshot.
:::

Far Cry 2's renderer holds more than the default PC game draws. A few features are compiled into the
shaders and shipped, but a gate in the engine never selects them on the default D3D9 path. Others are
limited by a quality tier or by a value that is the same at every preset. This page lists each one:
what it does, the gate, and what it takes to switch on. It also lists what is *not* cut, because
the shader option set and the preset ladder are complete.

The rest of the section: [overview](./overview.md), [prototype comparison](./prototype-comparison.md),
[settings and scripting](./settings-and-scripting.md) for non-render switches.

## Render tiers

`defaultrenderconfig.xml` holds one `quality[...]` block per subsystem (`Geometry`, `Shadow`,
`Vegetation`, `Environment`, `Water`, `Terrain`, `Ambient`, `TextureResolution`, `Post`) and a `RenderQuality`
block that picks one tier of each for a preset. The PC presets pick:

| Preset | Geometry | Shadow | Vegetation | Environment | Water | Terrain | Ambient | Texture |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `ultrahigh` | ultrahigh | ultrahigh | veryhigh | ultrahigh | ultrahigh | ultrahigh | high | ultrahigh |
| `veryhigh` | veryhigh | veryhigh | veryhigh | veryhigh | veryhigh | veryhigh | high | veryhigh |
| `high` | **medium** | **medium** | high | high | high | high | high | high |
| `highd3d10` | high | high | high | high | high | high | high | high |

(RE-verified, retail 1.03.) Four things follow from the table.

- **`high` is the lowered one.** Its Geometry and Shadow tiers are `medium`, with `LodScale`,
  `KillLodScale` and `ClustersLodScale` at 1.1 where `veryhigh` has 1.0, and the small-object shadow
  scales at 1.25 and 1.50. Its grass uses `GRASSQUALITYCONSOLE` (the high grass shader plus
  `CHEAPSIM`, a sway offset that does not preserve blade length), and its water reflection quality is
  6 where `veryhigh` has 7. `highd3d10` keeps Geometry and Shadow at `high`. `veryhigh` carries the
  values the September 2008 prototype used for its `high` ([prototype comparison](./prototype-comparison.md)).
- **No tier exists above `ultrahigh`.** The engine's own name table lists `optimal`, `ultrahigh`,
  `veryhigh`, `legacy`, `xenon`, `console`, `ps3` and `d3d10`, and nothing else. Vegetation has no
  `ultrahigh` tier at all: Ultra uses `veryhigh`, and no grass option exists above
  `GRASSQUALITYHIGH` in the shader.
- **Ambient tops out at `high`** for every upper preset: `MaxHemiMapDistance` 160, `ShadowMapSize`
  512, `SectorCountX/Y` 12, `SectorTextureSize` 128 for the sector sky-occlusion maps. Whether larger
  values work was not tested.
- **Shadow `high` equals `veryhigh`** value for value. Only `ultrahigh` is better (2048 maps, cascade
  ranges 4 / 20 / 140 m). `LeavesShadowRatio` is written `0.5f` on `ultrahigh`, `xenon` and `ps3`.

Features that exist only at the top of a tier ladder:

| Feature | Where it is on | Below that |
| --- | --- | --- |
| Road `PARALLAX` shader option | Terrain `ultrahigh` | removed at `low` to `veryhigh` |
| `TerrainAffectedByMuzzleFlash` | Terrain `ultrahigh` | 0 |
| Water reflection target 1024 x 1024, 4x MSAA | Water `ultrahigh` | smaller; `veryhigh` has MSAA mode 0 where the Xbox 360 variant has 4 |
| `HighQualityParticles` | Environment `veryhigh`, `ultrahigh` | off at `high` and below |
| `WaterRefraction` | Water `high`, `veryhigh`, `ultrahigh` | 0 (but see the gate below) |

The Xbox 360 and PS3 variants (`engine/settings/xenon`, `ps3`) are equal to or lower than PC, with
two exceptions: the 360 enables `AlphaToCoverage` at the root (PC has 0) and 4x MSAA on the water
reflection at `veryhigh`.

## Water refraction: shipped for D3D9, selected only on D3D10

`water.fx` has a `REFRACTION` option. It samples `RefractionRealTexture` at a pixel offset taken from
the water normal (`saturate(normalTS * 6).xy`, 5 px, scaled by `clamp(depth * 3.5, 0, 2.5)`),
rejects the offset when the linear depth there differs by more than 1 m so the foreground does not
leak into the water, and adds the result to the surface colour with `alpha = 1 - borderAlpha`.
Without it the water is a plain alpha blend, and rivers use a different border alpha.

*(RE-verified, Steam)*

- **The shaders ship.** In the D3D9 permutation list, `water` has 4,032 permutations and
  `waterriver` 4,704 that set `REFRACTION`. In the first (sun) pass their objects differ from the
  non-refraction ones, 1,008 pairs each, and 960 D3D9 objects bind `RefractionRealTexture` at `s5`.
- **The material gate.** The permutation builder `0x1037db40` tests `WaterRefraction` (config
  `+0x5a0`, Water group `+0x48`) at `0x1037ea18`, then calls `0x1040b100` and compares the result
  with 1 at `0x1037ea26` before it pushes the string `REFRACTION`. `0x1040b100` returns `DAT_11609674`,
  which `0x1032fe80` sets to 1 only when the platform flag has bit 4 (D3D10).
- **No scene copy on D3D9.** The string `"Refraction"` is referenced only by
  `CSceneRendererD3D10::PrepareFrameGraph` (`0x10356bc0`). The D3D9 frame graph
  (`CSceneRenderer_PrepareFrameGraph`, `0x10347040`) has no refraction target. `RefractionRealTexture`
  is a viewport global registered in `CViewportShaderParameterProvider` (`0x103788f0`, reference at
  `0x10378dd9`).
- **The config key is inert on D3D9.** `WaterRefraction="1"` is set at Water `high`, `veryhigh` and
  `ultrahigh`, and is ignored on the default path.

Turning it on takes an FCSE plugin: patch the compare at `0x1037ea26` (the GOG build needs its twin,
found by byte pattern) so the permutation is chosen when `WaterRefraction` is set, and copy the HDR
scene colour into a texture after the opaque passes and before the water draws, bound as
`RefractionRealTexture` (or at `s5` on the water draws). Depth is already available as
`DepthVPSampler`. The visible result was not seen; by the shader it wobbles the lake or river bed
through the surface and softens shorelines.

## Textures: `AlwaysMip0Loading` is off at every level

The `TextureResolution` group registers `SkipMips` (`+0x20`), `AlwaysMip0Loading` (`+0x24`) and
`MaterialTexturePriority` (`+0x28`) relative to the group base at config `+0x514`, so
`AlwaysMip0Loading` is config `+0x538`. The retail config sets it to 0 at every level from `low` to
`ultrahigh`, and in the console variants.

*(RE-verified, Steam)* `0x103f3010` returns `cfg+0x880 ? (cfg+0x880 >= 0) : (cfg+0x538 != 0)`. The
texture loader `0x1033a430` calls it for a texture whose header names a separate `_mip0` file and,
when it returns true, loads that file at once through the resource vtable (`+0x3c`) instead of
leaving it to the streamer. `0x104d4890` re-applies the setting to all loaded textures. The
description string next to the key reads "Disable loading of _mip0.xbt files." in the engine's table
(the table may be shifted by one entry).

Turning it on is a config edit: `AlwaysMip0Loading="1"` on `TextureResolution/quality[ultrahigh]`
and `[veryhigh]`. The game is a 32-bit process, so the cost is memory, which was not measured. What
the override at `cfg+0x880` is was not traced.

## Soft particles are tier-gated

A particle emitter has a `Soft` flag (flag bit 23) and a `SoftHighQuality` flag (bit 29).
`CSceneParticleEmitterRenderer::Render` (`0x103cb960`) adds the shader option `SOFT` (plus `FAR_SOFT`
when `FarSoftDistance > 0`) when bit 23 is set, or when bit 29 is set and config `+0x3bc` is non-zero.
Config `+0x3bc` is the Environment group's `HighQualityParticles` (`+0x40` in the group; the group
base is `+0x37c`). `HighQualityParticles="1"` is set only at Environment `veryhigh` and `ultrahigh`.

*(data census, retail 1.03)* In world 1's particle library 164 emitters have `Soft="0"` and
`SoftHighQuality="1"` (176 in world 2): wheel dust, engine smoke, torches, fires, explosions and the
sandstorm. A further 62 carry `SoftHighQuality="0"`. On `high` and below these emitters draw with
hard intersections against ground and walls. Soft particles read the linear depth that the depth
pass produces from `high` upward, so the cost is small.

To restore them below `veryhigh`, set `HighQualityParticles="1"` on Environment `high` and `medium`,
or set `Soft="1"` on the emitters in the world's particle `.rml`. That the flag at bit 29 is
`SoftHighQuality` is inferred from the data, which moved the value from `Soft` to
`SoftHighQuality` between the prototype and retail.

## Water reflection is capped at every preset

The reflection pass `CWaterReflectionRenderer_RenderWaterReflection` (`0x103a3250`) draws sky,
real trees, clusters, particles, objects, terrain and splines into a target of config `+0x598/+0x59c`
(1024 x 1024 at Ultra), with MSAA from `+0x5a4` (4 at Ultra) and a kill scale from `+0x594`
(`ReflectionMaxDistanceScaleModifier`, 1.0 at the top). A water material's reflection type is
`min(WaterReflectionQuality (+0x58c), the material's ReflectionQuality)`: 1 selects
`CUBEMAP_REFLECTION`, 2 or more `REAL_REFLECTION`.

Two values are the same at every level, including `ultrahigh`:

| Key | Value | Engine description |
| --- | --- | --- |
| `RealtreeReflectionMinLODLeaf` / `RealtreeReflectionMinLODTrunk` (Geometry `+0x6c` / `+0x70`) | 3 / 4 | "Minimum LOD for RealTree Leaves/Trunks in the reflection" |
| `WaterReflectionSizeThreshold` (Water `+0x30`) | 3 | "Control minimum radius of objects in reflection" |

Trees are therefore mirrored at their lowest LOD and small objects are skipped. Lower values (1 or 2
for the LODs, 1 for the threshold) are a config edit; the reflection pass costs more. The consumer
of the two LOD keys was not traced; the offsets are verified for the water group and the meaning
comes from the engine's own description strings.

## Road parallax only at Terrain Ultra

`road.fx` has a single-tap `PARALLAX` option that offsets the lookup by `HeightTexture1`. The retail
Terrain tiers `low` to `veryhigh` list `PARALLAX` under `remove` for the `Road` shader; `ultrahigh`
keeps it. The permutation builder `0x1037db40` adds it only when the material has `HeightTexture1`
and `HeightTexture1Priority <= MaterialTexturePriority` (config `+0x53c`, 5 at every level). Ultra
is reachable from the menu (`OPTIONS_DISPLAY_ULTRAHIGH`), so this is a tier limit rather than dead
code. Dropping the `remove` at `veryhigh` is a config edit. Decal `PARALLAX` (870 permutations) is
per decal material and is not tier-gated.

## Shadow filtering on the Fetch4 and simple-shadow-map paths

The shadow-receiving shaders compile three ways. `0x10385300` picks `HW_SM_COMPARE` when the D3D9
device accepts a `D24S8` depth texture (`CheckDeviceFormat` through device vtable `+0x100`,
`0x104227d0`), otherwise `FETCH4` (vtable `+0x108`, `0x10423680`: an AMD card, the device's `+0x10`,
whose `DF24` probe at `+0x10c` passes) and otherwise
`SIMPLE_SHADOWMAP`. The prototype's `sampleshadowati.inc.fx` renames the 8-tap soft kernel on the
ATI path to `disabled_SHADOW_CRUDE_BIG_KERNEL`, so the Fetch4 and simple paths do one bilinear 2x2
compare. The retail objects match: `SHADOW_CRUDE_BIG_KERNEL` changes the pixel shader for one third
of the permutation pairs (the `HW_SM_COMPARE` third) and is a no-op for the other two thirds.

*(RE-verified, Steam; the GOG address matches.)* Only a driver or wrapper that refuses `D24S8`
depth textures takes the soft-less path; which current drivers do was not checked. Restoring the
kernel means replacing the Fetch4 and simple shadow-receiving pixel shaders, a large set.

## The "keep red" post effect is gone from the shader

The prototype's `posteffect_adaptivebloom.fx` has a `SINCITY_EFFECT` option: desaturate, then push
pixels whose red exceeds twice green plus blue toward pure red by `SinCityEffectIntensity`. Retail
compiles 12 `SINCITY_EFFECT` final-pass permutations, but each resolves to the same pixel and vertex
objects as its plain twin, and no D3D9 object binds `SinCityEffectIntensity`; the name has no code
reference either. The renderer still builds the mask (`0x1038dc00`).

*(RE-verified, Steam)* The data that would ask for it is `PostFx.Infamy.Loop`, `.Start` and `.Stop`,
the only sequences that set `bloodEffect="1"`. Those belong to the Infamy feature, which is stripped
elsewhere too:

- `PostFx.InfamyStrike.*` (7 sequences) and `PostFx.InfamyRedLine.*` (2) ship with every track
  reduced to a single zero key. The empty `InfamyAttackWI` is reused as the outro of the malaria
  attack.
- `PostFx.Infamy.Start/Stop` and `InfamyRedLine` are referenced nowhere. `PostFx.Infamy.Loop`
  (`infamybufflevel`, an `infamy.png` pulse plus the keep-red flag) keeps its content, but its hash
  `0xD402430B` appears in no code, state machine or Lua script.
- `PostFx.Exhaustion.*` (and its misspelling `PostFx.Exhaution.*`) have no trigger in world 1, world
  2, `common/`, `patch/` or `Dunia.dll`.

The sequences the game does start (`Desert.*`, `StaminaFX.*`, `Run.*`, `Sniper.RadialBlur`,
`InfamyStrike.*` names in the entity library, `Malaria*`, `Wounds.*`) are named in the entity library;
whether each trigger fires is a gameplay question. Bringing the red keep back takes new final-pass
shader objects with index edits, plus Lua or a plugin that calls `EnablePostFx`. The Infamy bark
events and the mix slot are covered in [AI and combat](./ai-and-combat.md).

## Lit and normal-mapped particles are supported but barely authored

The particle shader compiles `NORMALMAP` (640 permutations), `SHADOW_OCCLUSION` (640), `AMBIENT` and
`DIRECTIONAL` to distinct objects. The renderer adds `NORMALMAP` from emitter flag bit 25
(`NormalMapped`) when the emitter has a normal texture, and `SHADOW_OCCLUSION` when a
shadow-casting light matches the emitter's light mask. In the data (retail 1.03) `NormalMapped="1"`
is set on 6 of 1,027 emitters, so smoke is lit by the scene only in a handful of cases. Lighting more
emitters is a data edit that needs normal textures. This is a capability the engine uses lightly,
not a cut.

## Level of detail: `specialLodKillScale`

`engine/gamemodes/gamemodesconfig.xml` sets `specialLodKillScale="1.2"` on the `Single` and
`FCXSingle` game modes, and on no editor or multiplayer mode. `CGameModeDesc::LoadProperties`
reads it as a float (server `0x093f8670`, Steam `0x105788a0`) into the mode descriptor, server
`+0x24` (constructor default 1.0 at `0x093f7fc0`), Steam `+0x4c`. No server caller of
`GetCurrentGameModeDescription` reads `+0x24`, and the server has no renderer, so the consumer is on
the client; it was not found in `Dunia.dll`.

By its name and its neighbour `KillLodScale` (config `+0x58`; "Change the Kill LOD scale", 1.0 at
Ultra, 1.25 at `low`, 1.5 at `legacy`), the campaign multiplies the kill-LOD distance by 1.2, which
would cull scene objects at roughly 83 percent of the distance in single-player only. The effect and
its size are inferred. Removing it is a data edit of the two attributes in the patch archive's
`engine/gamemodes/gamemodesconfig.xml`.

## Config keys that do nothing

The engine registers many render keys that retail leaves unset, and the config sets a few the
engine never reads.

| Kind | Keys |
| --- | --- |
| Read by the engine, absent from retail config (all are performance or debug switches; none adds an effect) | `UsePredicates`, `MinProjectedRatio`, `UsePartialPrecision`, `ClustersZPassMaxLOD`, `DisableTerrainPerPixel`, `DisableTerrainDecalDepth`, `DisableWaterDepth`, `DisableDynamicLigths`, `DisableSplinePrimitives`, `SectorAmbientSingleFrame`, `GammaRampR/G/B`, `Contrast`, `RealTreeForceTBuffer`, `GrassForceTBuffer`, `WidescreenFOV`, the `Display*` debug keys |
| Set in config, no string in Steam or GOG `Dunia.dll` | `TerrainComputeMaxErrorLODs`, `TerrainMaxErrorTolerance` (Terrain), `DepthPassXenonAuto` (a console-only key) |

*(data and string census)* The renderer also answers four console commands: `gfx_ConfigChange
<preset>`, `gfx_ShowConfig`, `gfx_DensityLodScale <name> <scale>` and `gfx_widescreenmode`.
`DensityLodScales` (`Logic.LightJungle`, `Logic.Savannah`, `Logic.Woodland`) is 1.0 at every PC
tier, and above 1 only on `legacy` and the PS3 variant; values below 1 were not tested.

The other dead keys in the engine's settings are on [settings and scripting](./settings-and-scripting.md).

## What is not cut

*(RE-verified, Steam, from the permutation lists)*

- **No shader option was dropped.** Every option the 2008 prototype's shaders define is still in
  retail; retail adds `AMBIENT_SPECULAR` on `aaa` and `realtreetrunk`, `HIGHQUALITYNORMALS` on water,
  `CLOUD_QUALITY_ULTRAHIGH`, alpha-to-coverage everywhere, and the adaptive-bloom `QUERY` and
  `RESOLVE_SMOOTH`.
- **Options that never compile have a reason.** `SPU_GRASS`, `SPU_GRASS_TEXTURE`, `NEAR_GRASS`,
  `MERGE_NOISE` and `POW_LUT` are PS3 only; `TBUFFER_INSTANCING` is D3D10 only. `FIRST_PERSON` on
  the weapon and skin shaders has no code and no domain, and `mesh_highlight NOFOG` and `primitive
  OMNI/SPOT` are dead defines.
- **Options that compile to nothing on PC by design:** the Xbox resolve merges (`resolve
  RESOLVE_DOF/BLUR/NOISE/HUE_SATURATION/SMOOTH`, `STANDARD_DEFINITION`), `LAST_POSTFX`, `WRITEZ`,
  `DUALSOURCEALPHA`, `NOCOLORWRITE`, `mesh_vehicle DIFFUSE_MAP_DETAIL` (it only feeds the debug
  view; the dirt layer reads `DiffuseTexture2` directly) and `shadowvsm VERTICAL`, the variance
  shadow map's blur stub. `aaaleaf TRANSLUCENCY_MASK` is set by the engine from the material but
  the shader never implements it, so real-tree leaves get uniform translucency; `mesh_bigleaf`
  does implement it.
- **D3D10 adds one visual feature over D3D9: refraction.** The two permutation lists differ by
  `FETCH4` and `SIMPLE_SHADOWMAP` (D3D9 only), `TBUFFER_INSTANCING`, leaf `ALPHA_TEST` and
  `d3d10resolve`. The D3D10 frame graph adds `Refraction`, `HDR SS/XS`, `Linear depth SS` and
  `DepthMSAA_RT`; D3D9 has `FakeHDR*` and `Depth2D`. `HdrFP32` (D3D10 presets only) is a
  target-format option. The other `platform == 1` checks (`0x103c6260`, `0x103c18a0`, `0x1039c740`,
  `0x1039cea0`, `0x1033af20`) select resource or instancing paths.
- **Distortion runs on D3D9.** Heat haze from fires, muzzle heat and explosion shockwaves (61
  emitters; 15 are switched off in the data) use a `Distortion` target in the D3D9 frame graph, and
  nine objects bind `DistortionSampler`.
- **Water details are by design:** `HIGHQUALITYNORMALS` changes the shader only together with
  `REAL_REFLECTION`, and `CAUSTICS` and `REFRACTION` apply only in the sun pass.
- **`PostFx.Visual.Test`** (constant film grain, strength 0.035) is on, not off: `patch/engine/
  gamemodes/gamemodesconfig.xml` starts it through `DefaultPostFxService/AutostartsPostFx`.
- **Sky, clouds and the sun** are covered in [the sky and cloud system](../engine-internals/sky-and-clouds.md);
  the shader objects and their index tables in [shader objects](../file-formats/shader-objects.md).

## Uncertain

- The reflection renderer turns one global off when `view+0x868 < 7` (`0x10381bc0(0)`); what it is
  was not traced.
- The consumers of `SoftShadows`, `UsePredicates` and `RealtreeReflectionMinLOD*`, and the
  `cfg+0x880` override of `AlwaysMip0Loading`, were not traced.
- Whether a driver on current hardware passes the `D24S8` depth-texture probe, so that the Fetch4
  path is rarely taken, was not checked.
- `PostFx.Infamy.Loop` is named in the entity library, but whether that field starts it was not
  traced; no code, state machine or Lua script uses its hash.
