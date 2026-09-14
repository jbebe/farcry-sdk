---
sidebar_position: 13
---

# Environment Presets

Fog, light, sky colour, clouds, bloom, god rays, depth of field and wind are data. A world's
descriptor names presets by GUID, and the presets themselves live in the world's `managers.fcb`. Both
halves can be overridden as fragments with JackAll. How the engine blends and applies them is in
[time of day, light and shadow](../engine-internals/time-of-day-and-lighting.md).

:::info[Verified via reverse engineering]
Read out of the retail `world1` and `world2` files, decoded with JackAll. Member names come from
`RegisterProperties` in `FarCry2_server`, and curve evaluation from `CCurveObj::GetValue` there.
:::

## Where a world's environment lives

### The descriptor names the presets

`<world>.game.xml` holds an `<Environment>` element. Its attributes, and those of its `Jungle`,
`Desert` and `Storm` children, are the GUIDs of presets; the zone sets are blended in by the
environment manager's zone blending. Retail `world1` and `world2` carry the same element:

| Slot | Base | `Jungle` | `Desert` | `Storm` |
| --- | --- | --- | --- | --- |
| `Lighting` | `Default.Clear.Lighting` | `Default.Jungle.Lighting` | `Default.Clear.Lighting` | `Default.Storm.Lighting` |
| `Fog` | `Default.Clear.Fog` | `Default.Jungle.Fog` | `Default.Desert.Fog` | `Default.Storm.Fog` |
| `Cloud` | `Default.Clear.Cloud` | — | `Default.Desert.Cloud` | `Default.Storm.Cloud` |
| `SkyA` | `Default.Clear.Sky` | — | — | `Default.Storm.Sky` |
| `Wind` | `Default.Clear.Wind` | `Default.Jungle.Wind` | `Default.Desert.Wind` | `Default.Storm.Wind` |
| `AdaptiveBloom` | `Default.Clear.AdaptiveBloom` | `Default.Jungle.AdaptiveBloom` | `Default.Desert.AdaptiveBloom` | `Default.Storm.AdaptiveBloom` |
| `AtmosphericScattering` | `Default.Clear.GodRays` | `Default.Jungle.GodRays` | `Default.Desert.GodRays` | `Default.Storm.GodRays` |
| `DepthOfField` | `Default.Clear.DepthOfField` | `Default.Jungle.DepthOfField` | `Default.Desert.DepthOfField` | `Default.Storm.DepthOfField` |

`Storm` also names `Intensity` (`Default.StormIntensity.Default`) and three transition curves,
`LightingTransition`, `CloudTransition` and `FogTransition` (`Default.Transition.Lighting`,
`Default.Transition.Cloud` and `Default.Transition.Fog`). `<DayCycleScale>` names
`Default.DayNightCycle.DayCycleScale`.

The rest of the element is literal values. Retail, besides `<Sky>` (listed in
[the sky and cloud system](../engine-internals/sky-and-clouds.md)), `<TempHack>` and
`<RealTreeCaps>`:

```xml
<DefaultEnvSettings DefaultStormFactor="0" DefaultHour="11" DefaultMin="30" />
<CurvedHorizon Start="500.0f" End="2200.0f" Height="500.0f" />
<Shadow DynamicShadowRadius="100" SunShadowZOffset="0" SunShadowZOffsetSlopeScale="3"
        SunStaticShadowThresholdAngle="1" />
<Fog Color="202,219,230" Start="0" End="400" FogAmount="0.8" />
<Camera ViewDistance="1024" />
<Clouds Enabled="1" WindSpeedScaling="0.04" AnimationScale="0">
  <Formation Coverage="0.6" FallOffCurve="0.8" NormalStrength="15" />
  <Material DiffuseLightingPower="1" DiffuseColor="204,210,221" SpecularLightingPower="3"
            SpecularColor="106,132,157" SubsurfaceScatteringPower="500"
            SubsurfaceScatteringColor="206,205,159" SubsurfaceScatteringBias="0.2" />
</Clouds>
<FakeTerrain Enabled="1" Height="0.0" Radius="512.0" Tesselation="8" LayerMaterial="" />
```

`world1` and `world2` leave the fake terrain's `LayerMaterial` empty; some multiplayer maps name one,
such as `Savannah_Undergrass`.

The shader's fog colour is a two-colour ramp swept by heading
([what a shader is given](../engine-internals/sky-and-clouds.md#what-a-shader-is-given-and-what-it-is-not)),
and its two ends measured in a running game matched the fog preset's gradients at that hour. The
`<Fog>` element's single `Color` is therefore not what the world fogs to by time of day. What reads
it was not traced.

### The managers file holds them

`<world>.managers.fcb` has one entity named `DataBaseItemManager` — `disEntityId`
`2052664152813473138` in `world1`, `2052667996310079726` in `world2` — holding every preset as a
`Template`:

```xml
<object type="Template">
  <value hash="D97A7AA9" type="String">Default.Clear.Lighting</value>
  <value name="NameId" type="Hash">05E2CDB5</value>
  <value hash="D854F1C7" type="String">{916E4520-3460-4167-B3AE-C1595C62B5C0}</value>
  <value name="Template" type="Hash">AF6CBE1A</value>
  <object type="Template">
    <value hash="CF68E402" type="String">CEnvironmentLighting</value>
    <value name="hid_DTCTH_ClassName" type="Hash">1D9FE8FE</value>
    <!-- members -->
  </object>
</object>
```

The two fields decoded without a name are the preset's name (hash `D97A7AA9`) and its GUID
(`D854F1C7`); the inner template's first field (`CF68E402`) is its class. Names follow
`Default.<set>.<kind>`:

| Class | Retail sets |
| --- | --- |
| `CEnvironmentLighting` | `Clear`, `Jungle`, `Storm`, `Cloudy`, `HoD`, `ScriptedEvent`, `Default` |
| `CEnvironmentFog` | `Clear`, `Jungle`, `Desert`, `Storm`, `HoD`, `Default`, plus `SandStorm.Fog`, `SandStorm.FogLight` and `Defoliant.DefoliantFog` |
| `CEnvironmentCloud` | `Clear`, `Desert`, `Storm`, `Cloudy`, `SandStorm`, `Default` |
| `CEnvironmentSky` | `Clear`, `Storm`, `Default` |
| `CEnvironmentWind` | `Clear`, `Jungle`, `Desert`, `Storm`, `SandStorm`, `HoD` |
| `CEnvironmentAdaptiveBloom` | `Clear`, `Jungle`, `Desert`, `Storm`, `ScriptedEvent`, `Default` |
| `CEnvironmentAtmosphericScattering` | `Clear`, `Jungle`, `Desert`, `Storm`, `HoD`, `Default`, named `.GodRays` |
| `CEnvironmentDepthOfField` | `Clear`, `Jungle`, `Desert`, `Storm`, `HoD`, `ScriptedEvent`, `Default` |
| `CEnvironmentTransition` | `Transition.Lighting`, `Transition.Cloud`, `Transition.Fog`, and two `Transition.Rain` |
| `CEnvironmentWeather` | `StormIntensity.Default` |
| `CDayCycleScale` | `DayNightCycle.DayCycleScale` |

A few more templates carry no name, or only `Default.`.

### Member names

From `RegisterProperties` in `FarCry2_server`:

| Class | Members |
| --- | --- |
| `CEnvironmentLighting` | `curveLightHDRMul`, `gradSunLightColor`, `gradMoonLightColor`, `curveAmbientLightHDRMul`, `gradAmbientLightColor`, `clrGroundColor`, `curveRimLightIntensity` |
| `CEnvironmentFog` | `curveStart`, `curveEnd`, `curveAmount`, `curveStartHeight`, `curveStartHeightValue`, `curveEndHeight`, `curveEndHeightValue`, `gradColorSunSide`, `gradColorSunOppositeSide` |
| `CEnvironmentAdaptiveBloom` | `bEnabled`, `fIntensity`, `fGaussianDeviation`, `fLuminanceThreshold`, `fTimeToAdaptToMoreIntense`, `fTimeToAdaptToLessIntense`, `curveMinimumLuminance`, `curveMaximumLuminance`, `fMinimumLuminanceAdaptationFactor`, `fMaximumLuminanceAdaptationFactor`, `fColorRemapRed`, `fColorRemapGreen`, `fColorRemapBlue`, `fContrast`, `fSaturation`, `bScaleByInverseIntensity` |
| `CEnvironmentAtmosphericScattering` | `bEnabled`, `gradColorTint`, `curveIntensity`, `fZoomFactor`, `fAttenuationStartAngle`, `fAttenuationEndAngle`, `curveCloudMaskIntensity` |
| `CEnvironmentDepthOfField` | `bEnabled`, `fFocusDistance`, `fNearDistance`, `fFarDistance`, `fCircleOfConfusion` |
| `CEnvironmentWind` | `fWindForce` |
| `CEnvironmentTransition` | `curveTransition` |
| `CEnvironmentWeather` | `curveWeatherAmplitude` |
| `CDayCycleScale` | `curveDayCycleScale` |

`CEnvironmentSky` and `CEnvironmentCloud` are in [the sky and cloud system](../engine-internals/sky-and-clouds.md).

## How keys are stored

### Curves

```xml
<object type="curveLightHDRMul">
  <value name="hidNumKnots" type="UInt32">32</value>
  <object type="Knots">
    <object type="Knot">
      <value name="Value" type="Vector4"><x>0.2493</x><y>0.5007</y><z>3.87548</z><w>0.00683966</w></value>
      <value name="Info" type="Vector4"><x>1</x><y>1</y><z>0</z><w>0</w></value>
      <value name="Type" type="UInt32">0</value>
    </object>
```

`Value.x` is the time of day as a fraction of 24 hours, and `Value.y` the value at it. `hidNumKnots`
is the count and has to agree with the knots listed. Retail stores most knots twice in a row; the
duplicate makes a zero-length segment, which evaluation never lands in.

`CCurveObj::GetValue` (`0x096AF4F0`) returns the first knot's value before it and the last knot's
after it. Between two knots, an integer type read from the knot that ends the segment picks the
interpolation, with `t` the position within the segment:

| Type | Between knots |
| --- | --- |
| 1 | Hermite, with both knots' `z` and `w` as tangents |
| 2 | Linear plus `sin(t × Info.x + Info.z) × Info.y`, from the starting knot |
| anything else | Linear |

The lighting knots examined are all type 0, so those curves are straight lines between knots and a
knot can be added anywhere. Which XML field carries the type, `Type` or `Info.w`, was not checked;
both are 0 on those knots.

`curveDayCycleScale` is the exception to the axis: its `x` is the hour, 0 to 24, and its `y` the
fraction of the day.

### Gradients

```xml
<object type="gradMoonLightColor">
  <object type="Knots">
    <object type="Knot">
      <value name="Position" type="Float">0.791272</value>
      <value name="Color" type="UInt32">9131337</value>
    </object>
```

`Position` is the time of day as a fraction of 24 hours. `Color` packs red in the low byte: 9131337
is `0x8B5549`, which is red 73, green 85, blue 139. Channels stop at 255, so brightness past that
comes from the preset's matching multiplier curve, such as `curveLightHDRMul`.

`clrGroundColor` is a plain `Vector4`. Several retail presets store values there that do not read as
colours; `Default.Clear.Lighting`'s is (0, 2.1644E-38, 0, 1.73882E-39).

The sky's `kfgrad…` members are keyframes of whole gradients and have a layout of their own, not
covered here.

## Retail values worth knowing

Times below are the preset's own. Whether they run on the clock or on the day-cycle scale is open.

### Night lighting

| Preset | Moonlight | Full from – until | `curveLightHDRMul` at night | Ambient | `curveAmbientLightHDRMul` at night |
| --- | --- | --- | --- | --- | --- |
| `Clear` | (73, 85, 139) | 18:59 – 04:59 | 0.5 | (65, 100, 255) | 2.0 |
| `Jungle` | (120, 136, 199) | 18:46 – 05:13 | 1.0 | (128, 159, 255) | 2.2 |
| `Storm` | (143, 153, 192) | 18:59 – 04:59 | 0.2 | (122, 122, 137) | 1.0 |
| `Cloudy` | (28, 39, 53) at dusk, (34, 85, 219) at midnight, (26, 36, 49) by dawn | 18:59 – 04:59 | 0.45 | (66, 85, 176) | 1.39 |
| `HoD` | (105, 122, 192) | 18:28 – 05:30 | 1.0 | (160, 173, 211) | 2.0 |
| `ScriptedEvent` | (120, 136, 199) | 18:01 – 06:15 | 0.65 | (41, 64, 97) | 0.35 to 0.44 |
| `Default` | (68, 77, 111) | 20:52 – 03:08 | 0.65 | (51, 52, 87) | 0.65 to 0.74 |

Every one lights the scene from the moon's direction from dusk, whether or not the moon is up; in a
running game the moon rose at about 22:38. With `Clear`'s values the moonlight comes to
(0.14, 0.17, 0.27) against an ambient of (0.51, 0.78, 2.0), about a fifth of it by luma before the
slope of the ground takes its share. The direct light a shadow removes is small next to the ambient
that stays.

### The colour grade

`CEnvironmentAdaptiveBloom`'s scalars are not keyed by time, so each preset grades noon and midnight
alike. In the September 2008 prototype's `posteffect_adaptivebloom.fx`, the final pass clamps the
frame to 0–1, raises each channel to its own power from `ColorRemapData`, bends the result with a
cubic contrast curve from `ContrastData`, then blends toward grey by `Saturation`. How the preset's
`fColorRemap` values become those powers was not traced in retail.

| Preset | Remap R / G / B | Contrast | Saturation | Intensity | Threshold | `curveMaximumLuminance` night → noon |
| --- | --- | --- | --- | --- | --- | --- |
| `Clear` | 0.1 / -0.06 / -0.5 | 0.1 | 0.5 | 0.25 | 0.3 | 0.07 → 0.48 |
| `Desert` | 0.1 / -0.06 / -0.5 | 0.35 | 0.5 | 0.25 | 0.3 | 0.07 → 0.48 |
| `Jungle` | 0.1 / -0.04 / -0.35 | 0.1 | 0.5 | 0.25 | 0.3 | 0.07 → 0.48 |
| `Storm` | 0.02 / -0.03 / -0.01 | 0 | 0.5 | 0.25 | 0.35 | 0.014 → 0.49 |
| `ScriptedEvent` | 0.1 / -0.03 / -0.5 | 0.2 | 0.52 | 0.25 | 0.3 | 0.07 → 0.48 |
| `Default` | 0.06 / -0.03 / -0.32 | 0.6 | 0.45 | 0.4 | 0.6 | 0.4 all day |

All but `Default` hold a minimum luminance of 0.1 and adaptation factors between 0.8 and 4;
`Default` has 0.25, 0 and 3. The times to adapt up and down are 1.5 and 0.5, except `Storm` (2 and 1),
`ScriptedEvent` (1.5 and 1) and `Default` (2 and 3).

:::info[Verified in a running game]
Retail GOG v1.03, world 1, calm weather at sunrise. The three registers were read inside the final
pass's own draw call, which goes through `DrawPrimitive`.
:::

The final pass was handed a saturation of 0.500, powers of (0.910, 1.056, 1.441) and a
`ContrastData` of (−0.176, 0.264, 0.912). That contrast is exactly (−2c, 3c, 1 − c) for c = 0.088,
the cubic `lerp(x, smoothstep(x), c)`, which keeps black and white where they are. No preset holds
0.088, so what reaches the shader is already a blend of presets. The powers sit where 1 − remap
would put a similar blend, which fits but is not proven.

### Clear fog

`Default.Clear.Fog`'s two colours:

| | Night | Dawn | Day | Dusk |
| --- | --- | --- | --- | --- |
| `gradColorSunSide` | (7, 11, 14) | (137, 95, 22) at 05:30, (194, 129, 18) from 05:59 to 06:59 | (85, 170, 255) from 07:30, (64, 159, 255) at noon | (213, 106, 0) from 17:00 to 17:59, (153, 80, 6) at 18:29 |
| `gradColorSunOppositeSide` | (7, 11, 14) | (56, 43, 64) at 05:44, (73, 54, 84) at 06:29 | (85, 170, 255) from 08:59 | (54, 54, 84) at 17:29, (43, 43, 64) at 18:14 |

`curveEnd` runs 200 at night and 500 from noon to about 16:45, and `curveAmount` is 1 all day.

### The day-cycle scale

`Default.DayNightCycle.DayCycleScale`, hour to fraction of the day, duplicates left out:

```
hour      0     2.62   4.69   6.06   7.68   9.62   11.74  14.03
fraction  0     0.058  0.155  0.240  0.266  0.326  0.396  0.432

hour      15.96  16.76  17.86  18.78  20.02  21.29  22.66  24
fraction  0.508  0.594  0.687  0.760  0.835  0.897  0.952  1
```

## Changing them

### As a JackAll layer

Both halves are fragments (see [`.fcb` fragment ids](./vortex.md)):

- `mods\worlds\world1\generated\world1.game.xml\_environment.xml` replaces the whole `<Environment>`
  element, so start from the retail one and change what you need.
- `mods\worlds\world1\generated\world1.managers.fcb\databaseitemmanager.2052664152813473138.xml`
  replaces the whole `DataBaseItemManager` entity: every preset of that world in one file, about a
  megabyte of XML.

`jackall-cli rml decode <world>.game.xml --element Environment` writes the retail element to
`<world>.game.decoded.xml`, and `jackall-cli fcb decode <world>.managers.fcb` writes the XML the entity
is cut from beside the `.fcb`. `world2` takes the same pair under its own names and entity id.

### While the game runs

The environment manager swaps presets by name with a blend; see
[time of day, light and shadow](../engine-internals/time-of-day-and-lighting.md#overrides-blend-over-a-duration).
