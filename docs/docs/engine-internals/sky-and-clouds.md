---
sidebar_position: 16
---

# The Sky and Cloud System

:::info[Verified via reverse engineering]
Traced live via GhidraMCP against **`Dunia.dll`** (Steam v1.03) for the render-side code, and cross-
checked against **`FarCry2_server`** (the Linux dedicated-server ELF, unstripped symbols) for class
and member names. Investigated while scoping a "bigger/better skybox" mod request — the short
version is that there is no skybox to enlarge; the answer is a runtime system with its own,
separately-sized pieces.
:::

## There is no skybox

FC2 does not render a big background texture (a cubemap or a wrapped panorama) behind the world.
The sky is built from three independent pieces every frame:

1. A **gradient dome**, coloured by keyframed time-of-day curves — no texture at all.
2. **Procedurally generated clouds** — noise combined into a render target at runtime, not sampled
   from a shipped cloud texture.
3. A small set of **authored sprites and one mesh** for the sun, moon and stars.

Each piece has its own size ceiling, and they're wildly mismatched — which is the actual reason the
sky "looks like a bad skybox" even though it isn't one.

## What draws what

`CSceneSky`'s constructor (`Dunia.dll:0x1037ac50`) builds five drawables, each owning one shader:

| Object | Constructor | Shader | Options |
| --- | --- | --- | --- |
| Sky dome | `0x103d9040` | `SkyDome` | `SKY_STORM_BLEND`, `OPAQUE` |
| Star sphere | `0x103d9190` | `StarSphere` | `ADDITIVE` |
| Cloud noise | `0x103da580` | `CloudNoiseBlur`, `CloudNoiseCombine` | `LAYER1`, `LAYER2` |
| Sun disk | `0x103ddc00` | `SkyDisk` | `TEXKILL` |
| Sun/moon sprites | `0x103d9550` | `CelestialBody` | `VISIBILITY_TEST`, `TIME_OF_DAY_MAPPING`, `TIME_OF_DAY_COLOR`, `TEXKILL`, `ADDITIVE`, `FAKEHDR` |
| Cloud layer | `0x103dce80` | `CloudLayer` | `LAYER1`, `LAYER2`, `COMBINE_LOW_OCTAVES`, `MASK_DESTCOLOR`, `CLOUD_QUALITY_*` |

The dome and the cloud layer are both surfaces of revolution generated at load. The dome
(`0x103d7d50`) is 16 rings by 32 segments. The cloud layer (`0x103dcd80`) is a **shallow bowl**,
spun from a six-point profile of radius/height pairs stored at `0x10f95fc8`:

```
(-0.5, 0.20)  (0, 0.18)  (0.5, 0.11)  (0.8, 0.04)  (1.0, 0.01)  (1.5, 0.00)
```

The bowl flattens to zero height at its rim, and the cloud shader takes its noise coordinate from the
bowl's own XY. That is the reason the clouds appear to meet the horizon rather than passing over it:
the geometry they are painted on genuinely descends to eye level at the edge, so there is no
perspective for the clouds to have.

## The sky's own draw, and the state it reads

One function draws the whole sky: `Dunia.dll:0x1037A150`. It opens by fetching the renderer's scene
state, then calls each piece in turn, every one of them gated by a bit of a flags argument, so a
viewport can ask for some of the sky and not the rest:

| Bit | Draws | Function |
| --- | --- | --- |
| 1 | Star sphere | `0x103D8720` |
| 2 | Sky dome | `0x103D92D0` |
| 4 | Cloud layer | `0x103DA190` |
| 8 | Sun disc | `0x103DD7B0` |
| 0x10 | Sun and moon sprites | `0x103DC3C0` |

The storm factor splits it: the dome and clouds draw while it is above zero, the star sphere, sun
disc and a second cloud pass while it is below one, so a partial storm draws both and crossfades.

The scene state it reads from is reached through an accessor that is **one instantiation of a
template the renderer uses for every kind of component**, identical in bytes three times over in each
shipped build. Anything hooking it would be both ambiguous and liable to be handed some other
subsystem's state; arriving through one of the draws above is unambiguous by construction. The sun
disc's call passes `state + 0x148` as the direction to orient itself by, which is what makes the
whole block below addressable from outside.

| Offset | Field |
| --- | --- |
| `+0x134` | `SunHorizonScaleStartElevation` |
| `+0x140` | Sun disc HDR multiplier |
| `+0x148` | **Sun direction**, three floats, unit length, Z up |
| `+0x170` | `SunRange` |
| `+0x178` | `SunMaxHorizontalScale` |
| `+0x17C` | `SunMaxVerticalScale` |
| `+0x1B8` | Storm factor, 0 to 1 |
| `+0x1BC` | Time-of-day coordinate, the one every sky shader looks its colour ramp up with |

The names come from `CSky::LoadSky` writing the world's `<Sky>` attributes into those same offsets.

### `SunRange` does not change how big the sun looks

The sun disc's vertex shader builds its geometry as `float4(Position.x, 1, Position.y, 1)` scaled by
a `Scaling` vector the draw computes:

```
Scaling.x = ((SunMaxHorizontalScale - 1) * horizonBlend + 1) * SunRange
Scaling.y = SunRange
Scaling.z = ((SunMaxVerticalScale   - 1) * horizonBlend + 1) * SunRange
```

`Scaling.y` is the distance and `Scaling.x`/`.z` are the half-extents, so `SunRange` cancels out of
the ratio and only moves the disc further away at proportionally greater size. What is left is
`atan((SunMaxHorizontalScale - 1) × horizonBlend + 1)`, which at the shipped value of 1 is 45° — a
**90°-wide** element. The sun disc is the broad glow around the sun, not the sun. What reads as the
sun's body is the flare sprite's texture, whose shipped image is a hard-edged white disc filling the
inner half of its own bitmap with no falloff at all; every soft edge in the stock game comes from
bloom.

## Shaders, and where their source is

Every sky shader's compiled object can be located and replaced — see
[`shadersobj`](../file-formats/shader-objects.md) and
[replacing a shader](../modding/replacing-a-shader.md). The permutation compiled with no options
keys on the CRC32 of the shader's name, which resolves `celestialbody`, `skydome`, `skydisk`,
`starsphere`, `cloudnoisecombine` and `cloudnoiseblur` directly.

The September 2008 Xbox 360 prototype ships the **HLSL source** for all of them, which retail does
not. For `celestialbody` the prototype and the retail object are the same shader: recompiling the
prototype's logic against the retail binding table reproduces the shipped bytecode instruction for
instruction, differing only in the operand order of two commutative multiplies. Treat the prototype
sources as an accurate guide that still has to be checked per shader.

## Sun and moon: one shader, four shipped variants

`CelestialBody` draws the sun flare, the moon and the moon flare. Its parameters arrive in a single
`float4` at `c71` — `x` time-of-day coordinate, `y` visibility, `z` HDR multiplier, `w` horizon
factor — plus two samplers: the sprite and its time-of-day colour ramp.

Only four pixel-shader objects in the D3D9 tree bind `CelestialBodySampler`:

| Object | Variant |
| --- | --- |
| `3ffcc3dd` | `ADDITIVE` + `TIME_OF_DAY_COLOR` |
| `1a68de08` | `ADDITIVE` + `TIME_OF_DAY_COLOR` + `FAKEHDR` |
| `433f72b2` | `TIME_OF_DAY_COLOR`, fog applied |
| `34e1d970` | `TIME_OF_DAY_COLOR` + `FAKEHDR`, fog applied |

`ADDITIVE` skips fog and scales by `BloomAdaptationFactor`; the fog variants run
`ApplyFogNoBloom` instead. `FAKEHDR` appends the encode pair (`add -1`, `mul 0.125`) and drops
`AlphaBlendEnable` from the render state. `TEXKILL` is compiled out entirely on PC, which is why
eight permutations collapse onto four objects.

The flares are the `ADDITIVE` pair, so which one runs depends on whether HDR is on: `3ffcc3dd` with
`Hdr="1"`, `1a68de08` without.

The render state both `ADDITIVE` variants use sets only four states:

```
AlphaBlendEnable=true   BlendOp=Add   ZWriteEnable=false   CullMode=None
```

It does **not** set `ZEnable`, so the flare inherits the depth test from whatever ran before it, and
it does not set `SrcBlend`/`DestBlend` either. Nothing about the flare's occlusion is decided in its
own render state.

### The visibility term is a real occlusion query, and it stays inside the flare

`Params.y` is not a constant. The `VISIBILITY_TEST` permutation draws the sprite with colour writes
disabled into a 16×16 "Temp query surface", and the result becomes the sprite's own visibility
multiplier. Standing behind a tree genuinely dims the flare, without the shader doing any work for it.

:::warning[Not the same thing as `SunOcclusionFactor`]
The `SunOcclusionFactor` global at `c63` sounds like this value and is not it. It is read by exactly
one shipped shader — `water.fx`, as `specular *= SunOcclusionFactor * shadowFactor` — and sampled in
a running game it holds `1.0` throughout play, in the open and behind cover alike. The flare's own
visibility never leaves the draw that computes it.

That matters to anything outside the renderer that wants to know whether the sun is visible: the
engine has the answer and does not publish it. Measuring it again with an occlusion query of your own
is the available route, and it has to be issued during the sky pass — see
[presenting a frame](./presentation-and-input.md) for why nowhere later will do.
:::

## What a shader is given, and what it is not

`CViewportShaderParameterProvider` (`Dunia.dll:0x103788F0`) registers the constants every shader in
the frame can read without asking, at fixed registers. The camera is fully described:
`ViewProjectionMatrix` at `c4`, `ProjectionMatrix` at `c8`, `ViewMatrix` at `c12`, `ViewPoint` at
`c47`, `CameraDirection` at `c46`, plus fog at `c48`–`c52` and `BloomAdaptationFactor` at `c58`.

**The sun's direction is not among them.** Nothing in the provider carries it, and the reason is
structural rather than an oversight: every draw that needs the sun is already positioned at the sun.
The flare and the sun disc are billboards placed there, so their own transform is the answer, and the
cloud layer takes `SunDirection` as a parameter of its own for that draw alone.

That is the whole difficulty behind any screen-space sun effect. Such an effect needs the angle
between the view direction and the sun, and no shader in the game can be handed both halves of it. A
fullscreen pass has the camera and not the sun; the flare's own shader has the sun and not, usefully,
the camera — a pixel there knows its distance from the sun but not the sun's distance from the centre
of the screen, and a wash built on the former paints a bright disc on the sky rather than a veil.

Two further facts about the flare quad, both established by testing rather than by reading:

- Its texture coordinate runs 0 to 1 with the sun at the centre, so distance across the quad is the
  angle from the sun, scaled by whatever `SunFlareTextureSize` makes the quad.
- Transforming the quad's local origin by its own `ModelViewProj` does **not** land on the sun, so
  the sun's screen position cannot be recovered inside the shader that way.

The consequence is that the angle has to come from the game side: read the sun out of the scene state
above, read the camera back off the Direct3D device, and do the work outside the shader.

## The dome: a gradient, not an image

`CSky` (`CSky::LoadSky` at `Dunia.dll:0x1018c880`, matched against the server's
`CSky::LoadSky(XmlConstNodeRef const&, CSceneSky&)`) owns the dome. Its colour comes from two
keyframed gradients, confirmed via `RegisterProperties` on both classes involved:

- **`CSkySetup`** (`FarCry2_server @0x0962ded0`) — `SkyDomeColorSunSide` (offset `+4`),
  `SkyDomeColorSunOppositeSide` (offset `+44`), both `CKeyFramedGradientEval`.
- **`CEnvironmentSky`** (`@0x0962c080`) — the same two curves, `kfgradSkyDomeColorSunSide` /
  `kfgradSkyDomeColorSunOppositeSide`, as they're stored in a world's `.managers.fcb`.

Each curve is a set of time-of-day keyframes, and each keyframe carries its own horizon-to-zenith
colour gradient. The engine interpolates both the keyframe position (time of day) and the gradient
itself (horizon to zenith) every frame. There's no per-pixel sky texture to enlarge — the dome's
"resolution" is however many keyframes and gradient stops an artist gave it.

## The clouds: generated on the GPU, into a hardcoded 512×512 target

This is the actual source of the bad-skybox look. `CloudLayer`'s render targets are allocated in
its constructor, `Dunia.dll:0x103da580` (body ends `0x103da6c3`):

```
103da692  6a 01                 PUSH 1
103da694  6a 22                 PUSH 22h              ; RT format
103da696  6a 01                 PUSH 1                ; mip levels
103da698  68 00 02 00 00        PUSH 200h             ; height  = 512
103da69d  68 00 02 00 00        PUSH 200h             ; width   = 512
103da6a2  88 46 4c              MOV  [ESI+4Ch], AL
103da6a5  c7 46 24 40 00 00 00  MOV  [ESI+24h], 40h   ; blur-mask size = 64
103da6ac  c7 46 1c 00 02 00 00  MOV  [ESI+1Ch], 200h  ; combine-pass draw size = 512
103da6b3  e8 88 28 03 00        CALL 1040cf40         ; CreateRenderTarget
```

Three sizes, all hardcoded, all confirmed by their consumers rather than guessed:

- **512×512** — the cloud noise render target itself (`[ESI+0x18]` from the call above).
- **512×512 draw size** at `[ESI+0x1C]` — the quad size `CloudNoiseCombine`
  (`Dunia.dll:0x103dabc0`, body ends `0x103dae64`) draws at. It loops four noise octaves with
  per-octave density weights and must match the target's dimensions.
- **64×64** at `[ESI+0x24]` — a *second*, much smaller render target used by the blur pass
  (`Dunia.dll:0x103dae70`, body ends `0x103daf26`, named `"Clouds Blur"`). It allocates a `size ×
  size` target and runs two separable blur passes at `1.0/size`, taking the step as a shader
  constant rather than a baked literal — this is the cloud-shadow / god-ray occlusion mask, and its
  resolution-agnostic blur is a good sign that raising the size is safe.

Confirmed shader permutations for `cloudlayer`, found in both `common/engine/shaders/
fastinitdata_d3d9.bin` and `fastinitdata_d3d10.bin` (a shipped name table: each shader name is
followed by its compiled permutation `#define`s, stored as 2-byte big-endian length-prefixed ASCII):

```
CLOUD_QUALITY_LOW  CLOUD_QUALITY_MEDIUM  CLOUD_QUALITY_ULTRAHIGH
LAYER1  LAYER2  COMBINE_LOW_OCTAVES  MASK_DESTCOLOR
```

`CLOUD_QUALITY_ULTRAHIGH` is compiled into **both** the D3D9 and D3D10 shader tables — it's not a
D3D10-only permutation. `COMBINE_LOW_OCTAVES` confirms the "low quality" combine path really does
skip the high-frequency octaves rather than just sampling them coarser, which is consistent with the
noise looking flat/blobby at the shipped 512×512 rather than merely soft.

`LAYER1` / `LAYER2` match `CEnvironmentCloud`'s two formation layers, confirmed via
`RegisterProperties` (`FarCry2_server @0x0962dfc0`):

```
CEnvironmentCloud            bEnable, fAnimationScale, FormationLayer1 (+12), FormationLayer2 (+36), Material (+60)
CSceneSky::CCloudFormation   fCoverage, fFallOffCurve, fNormalStrength, fParallaxStrength, fWindSpeedScale, bEnable
CEnvironmentCloudMaterial    fDiffuseLightingPower, gradDiffuseColor, gradAmbientColor,
                              fBackLightingPower, gradBackSunColor, gradBackMoonColor,
                              fSunLightColorSamplingScale, fSubsurfaceScatteringPower,
                              gradSubsurfaceScatteringSunColor, gradSubsurfaceScatteringMoonColor,
                              fSubsurfaceScatteringBias
```

`fNormalStrength` / `fParallaxStrength` are what give the clouds visible depth instead of reading as
a flat texture; `fSubsurfaceScattering*` is the sunset glow through cloud edges. All of it is tuned
by the shipped `.managers.fcb` for a 512×512 target — raising the target's resolution without
retuning these leaves the shape unchanged, just sharper.

**No cloud texture ships with the game.** Grepping every hashlist and the extracted asset tree for
anything cloud-shaped outside UI (`ui/textures/common/clouds.xbt`, menu-only) and water
(`terrain/water/watercloud_n.xbt`) finds nothing — because there's nothing to find. The clouds are
pure noise, generated fresh every frame by `CloudNoiseCombine`/`CloudNoiseBlur`.

## The authored assets: nine small files, ~872 KB total

Everything else in the sky *is* an ordinary texture, all under `graphics/sky/dome/` in `worlds.dat`
and all named literally in every world's `<Sky>` element (see below). None has a `_mip0` companion —
each file is the complete texture, and none of the size limits below are enforced by the format,
only observed in the shipped data.

| File | Size | Dimensions | Mips | Format |
|---|---|---|---|---|
| `sky_color_sun.xbt` | 32,928 B | 64×512 | 1 | DXT5 |
| `sun_flare.xbt` | 87,540 B | 128×128 | 8 | uncompressed A8R8G8B8 |
| `sun_flare_tod_color.xbt` | 2,208 B | 512×4 | 1 | DXT5 |
| `moon.xbt` | 65,696 B | 256×256 | 1 | DXT5 |
| `moon_flare.xbt` | 87,580 B | 32×512 | 10 | uncompressed A8R8G8B8 |
| `moon_tod_color.xbt` | 1,184 B | 128×8 | 1 | DXT5 |
| `stars/background_d.xbt` | 216 B | 8×8 | 4 | DXT1 |
| `stars/milkyway_d.xbt` | 262,304 B | 2048×256 | 1 | DXT1 |
| `stars/star_d.xbt` | 344 B | 16×16 | 5 | DXT1 |
| `stars/starsphere.xbg` | 353,268 B | 10,320 verts, 3 submeshes | — | mesh |

`sky_color_sun.xbt`, `sun_flare_tod_color.xbt` and `moon_tod_color.xbt` are all colour **ramps**
(lookup tables, not images) stored as DXT5 — block compression applied to a smooth gradient, which
is close to the worst possible use of DXT and a plausible second source of banding independent of
the render path (below). `sun_flare.xbt` and `moon_flare.xbt` are the two known uncompressed
textures in the entire shipped game (see [`.xbt`](../file-formats/xbt.md)).

`starsphere.xbg` is a real mesh (10,320 verts × 32-byte stride, 3 submeshes) with three `Unlit`
materials, each a single `DiffuseTexture1` slot into `background_d`, `milkyway_d` and `star_d`
respectively; `milkyway_d` (additive blend) is already at the largest dimension observed anywhere in
the shipped texture corpus (2048 on its long axis).

### Every path is data, not hardcoded — but shared across 26 files

Each world's `<world>.game.xml` names all seven sun/moon/sky textures and the star mesh literally in
a `<Sky>` element:

```xml
<Sky SkyColorSun="graphics/Sky/dome/sky_color_sun.xbt" SunRange="2"
     SunMaxHorizontalScale="1" SunMaxVerticalScale="0.8"
     SunFlareTexture="graphics/Sky/dome/sun_flare.xbt"
     SunFlareTimeOfDayColorTexture="graphics/Sky/dome/sun_flare_tod_color.xbt"
     SunFlareTextureSize="0.05" SunFlareMaxUniformScale="3" SunFlareMaxBottomScale="0.7"
     MoonTexture="graphics/Sky/dome/moon.xbt"
     MoonTimeOfDayColorTexture="graphics/Sky/dome/moon_tod_color.xbt"
     MoonFlareTexture="graphics/Sky/dome/moon_flare.xbt"
     MoonTextureSize="0.1" MoonFlareTextureSize="0"
     MoonPitchAtSunrise="10" MoonYawAtSunrise="90"
     RotationAxisPitch="5" RotationAxisYaw="0"
     SunElevationNightMin="-0.4" SunElevationNightMax="-0.9"
     StarSphereGeometry="graphics/Sky/dome/Stars/StarSphere.xbg"
     SunRiseTimeHour="6" SunRiseTimeMinute="0" SunHorizonScaleStartElevation="0.5"
     SkyColorHDRMul="1.0" SunLightHDRMul="2" MoonHDRMul="10" SunFlareHDRMul="5"
     TimeScale="10" />
```

This is a cooked binary file per world — **26 of them** ship: `world1`, `world2`, `tmpla`, the 16
`mp_*` maps, and 6 DLC maps. Replacing one of the nine files *at its existing path* needs none of
them touched, since the path strings don't change. Only *renaming/relocating* an asset would require
editing all 26.

The same seven texture paths are also present as literal strings inside `Dunia.dll` itself
(`asset-reachability.md` already documented this half) — a second, parallel reachability path to the
same files, which is presumably why they stay loadable even though the depload manifest
(`world1_depload.xml`) only lists the star-sphere half (`starsphere.xbg` + its three materials +
`background_d`/`milkyway_d`/`star_d`), omitting the six sun/moon/sky textures entirely.

## Rendering: an 8-bit path by default, HDR available but unused

`CRenderQualityConfig::RegisterProperties` (`Dunia.dll:0x103f7f00`) registers `Hdr` (default
disabled) and `HdrFP32` (default disabled) alongside `Bloom` (default disabled). Frame-graph pass
names confirm two parallel paths exist: `HDRSurface`/`HDRTexture` (the real, disabled-by-default
path) versus `FakeHDRSurface`/`FakeHDRTexture` (LDR colour plus a brightness-in-alpha trick, the
default path). A smooth 8-bit gradient — exactly what the sky dome and its DXT5 ramps are — bands
visibly under the fake path in a way it wouldn't under the real one.

`CEnvironmentAtmosphericScattering` carries `hidNumPasses`, `curveIntensity`, `gradColorTint`,
`curveCloudMaskIntensity` — the god-ray / crepuscular-ray system, which consumes the 64×64 cloud
mask above as its occlusion input. `CEnvironmentAdaptiveBloom` (16 members) is the game's tonemap
and colour-grade stage — it touches the whole frame, not just the sky, since `_SkyColor` also feeds
world ambient lighting as a directional hemisphere term (see [`.xbm`/`.xbg`](../file-formats/xbm-xbg.md)).

Console/config surface confirmed via strings in `Dunia.dll`:

- `gfx_Draw_Skybox` — *"Activates drawing of skybox and moving cloud layers"* (the engine's own name
  for this system, despite there being no actual skybox texture).
- `DisableSky`, `DisableGodRayCloudMasking` — clean on/off toggles, useful for A/B comparison.
- `CLOUD_QUALITY_LOW` / `_MEDIUM` / `_ULTRAHIGH` quality tier names (see shader permutations above).

## Why the sky "looks like a bad skybox" without being one

Put together, the mismatch is the story:

- The dome colour has essentially unlimited precision (it's math, not a texture) but renders through
  an 8-bit path by default.
- The clouds — the most visually dominant, highest-frequency element — are capped at 512×512 for the
  whole sky, with a 64×64 shadow/occlusion mask underneath that.
- The authored sprites (moon, sun flare, stars) are all well under what the format allows (2048 on an
  axis, proven elsewhere in the shipped corpus).

None of these ceilings are enforced by any file format or archive constraint — they're runtime
constants and shipped asset choices, both changeable without a new engine feature.

## Shader source status

The sky/cloud shaders (`skydome.fx`, `cloudlayer.fx`, `cloudnoisecombine.fx`, `cloudnoiseblur.fx`,
`celestialbody.fx`, `starsphere.fx`, `skydisk.fx`, plus includes `cloudshadows.inc.fx`,
`skyfog.inc.fx`, `curvedhorizon.inc.fx`) compile into `shadersobj.dat` as
`shadernumber_XXXXXXXX.pso`/`.vso`, addressed by hash rather than by name.

That is no longer a dead end. The D3D9 objects carry a binding table naming every parameter as a
CRC32, the index tables at the root of the tree resolve a shader's no-option permutation from its
name, and a replacement compiled with `fxc` drops straight back in — the whole loop is
[`shadersobj`](../file-formats/shader-objects.md) and
[replacing a shader](../modding/replacing-a-shader.md). What is still unknown is how a permutation's
`#define`s fold into an index key, which is what would let every one of the 147,140 permutations be
addressed by name rather than found by its parameters.
