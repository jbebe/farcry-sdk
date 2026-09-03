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

The sky/cloud pixel shaders themselves (`skydome.fx`, `cloudlayer.fx`, `cloudnoisecombine.fx`,
`cloudnoiseblur.fx`, `celestialbody.fx`, `starsphere.fx`, `skydisk.fx`, plus includes
`cloudshadows.inc.fx`, `skyfog.inc.fx`, `curvedhorizon.inc.fx`) compile into `shadersobj.fat` as
`shadernumber_XXXXXXXX.pso`/`.vso` — permutation IDs, not paths, and per
[asset-reachability.md](./asset-reachability.md) ~93% of that archive is unrecoverable by name. The
D3D9 tree has no `CTAB`/reflection data and is a dead end for recovery; the D3D10 tree
(`shadersobj/engine/shaders/obj10/`) is plain DXBC with `RDEF` intact, so names and constant buffers
survive `fxc /dumpbin` — but only on the `-3dplatform d3d10` backend.

The two `fastinitdata_*.bin` files used above to confirm the cloud permutations are themselves a
promising unexploited resource: a full shader name → permutation-defines table, for **every**
shader in the game, not just the sky ones. That's very likely the missing key to resolving
`shadersobj.fat`'s ~93% unknown rate — worth a dedicated pass independent of anything sky-related.
