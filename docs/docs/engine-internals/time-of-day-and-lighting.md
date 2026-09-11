---
sidebar_position: 16
---

# Time of Day, Light and Shadow

:::info[Verified via reverse engineering]
Traced live via GhidraMCP against **`FarCry2_server`** (the Linux dedicated-server ELF, unstripped
symbols). Addresses and object offsets on this page are that binary's. Anything measured in a running game says so where it appears.
:::

The environment manager turns a world's presets and the clock into this frame's sun, moon, fog, light
and shadow. Which presets a world uses, how their keys are stored and what retail ships in them is
data, covered in [environment presets](../modding/environment-presets.md). This page is what the
engine does with them.

## One update, in this order

`CDynamicEnvironmentManager::Update` (`0x09162730`) runs once a frame. After copying the
curved-horizon variables into the renderer's config and binding the world's templates:

1. `UpdateVars` applies the `env_*` settings when they have changed: `env_Hour`, `env_Minutes`,
   `env_Seconds`, `env_StormHour`, `env_TimeScale`, `env_WindForce`, `env_WindDir` and
   `env_DelayShadowMovement` (see the [developer console](./developer-console.md)).
   Step 4 overwrites both wind settings.
2. Every override's blend advances (below).
3. The clock advances by the frame's game time multiplied by the time scale, unless it is held
   (below). Passing midnight counts a day and sends `COneDayCompletedEvent`.
4. `UpdateWeather`, the storm factor, zone blending and wind. The force starts from `env_WindForce`,
   but while the flag at `CGameControllerManager` `+4` is clear it is replaced by the zone-blended
   wind preset's. It is then lerped toward the storm's wind and, again only while that flag is
   clear, scaled by a random fluctuation, clamped to 0–250 (the setting's help text says 0–600) and
   lerped toward a wind override's `fWindForce`. The direction is replaced by
   `EvaluateWindDirection`'s (`0x09158380`), which reads `env_WindDir` only as the start of its next
   random drift. In a running game, `env_WindForce` has no visible effect and `SetWindOverride` does.
5. `CSky::Update` places the sun and the moon.
6. Fog: the zone-blended preset, lerped toward the storm's fog by the storm transition curve, then
   toward a fog override. The result goes to `C3DEngine`'s fog setters and to `CSky::UpdateFog`.
7. `UpdateAdaptiveBloom` and `UpdateAtmosphericScattering`, then depth of field with the storm's and
   an override's blended in.
8. `UpdateSky`: the sky gradients, the storm sky, and the clouds' formation, material and light.
9. `UpdateLight` chooses the scene light, `GenerateSunOcclusion` runs, and `CSky::UpdateShadow` aims
   the shadows if that light casts them.
10. Network time, rain probability and rain.

## Overrides blend over a duration

Each override is a preset pointer followed by five floats: a start factor, a target factor, a
duration, the time elapsed and the current factor. `Update` adds the frame's game time to the elapsed
time, clamps it to the duration and moves the current factor between start and target in proportion.
A duration of zero goes straight to the target. Wherever that kind of preset is evaluated, the
override's preset is evaluated at the same time of day and lerped over the result by the current
factor.

| Override | Preset | Current factor | Read in |
| --- | --- | --- | --- |
| Fog | `+0x14C` | `+0x160` | `Update` |
| Clouds | `+0x184` | `+0x198` | `UpdateSky` |
| Wind | `+0x1A0` | `+0x1B4` | `Update` |
| Depth of field | `+0x1DC` | `+0x1F0` | `Update` |

Three more blocks of the same shape end at `+0x17C`, `+0x1CC` and `+0x20C`. What reads them is not
traced.

The Lua override methods on `CDynamicEnvironmentManager` ([Lua API surface](./lua-api-surface.md))
and the Domino boxes `OverrideEnvironmentFog`, `OverrideEnvironmentCloud` and
`OverrideEnvironmentWind` each take a preset by name and a transition duration, which is what these
blocks hold; the call path between them is not traced. Retail content uses the boxes during play.
Act 2's Border Storm graph (`a2sm05_reprisal1.a2sm05_borderstorm.lua`) drives the shared box
`Common_CustomBoxes.SetEnvironmentEffects` with `TransitionDuration = 7`, through its
`SandStorm_Light` input, which overrides the clouds with `Default.Storm.Cloud`, the wind with
`Default.SandStorm.Wind` and the fog with `Default.SandStorm.FogLight`. The box's other input,
`Sandstorm`, uses `Default.SandStorm.Cloud`, `Default.SandStorm.Fog` and `Default.SandStorm.Wind`.

## The clock

The time scale sits at `+0x25C`, copied from `env_TimeScale`. The clock does not advance while
`+0x30` is set, while `+0x3C4` is set, or while `IsHideShadowMovementCondition` (`0x09157260`) holds.

That condition is `DelayShadowMovement`, which the game's own settings describe as the delay "before
shadow starts when camera does not move". While both flags at `+4` and `+5` of the player's input
listener are clear, a timer at `+0x3BC` gathers scaled frame time, and the clock is held until the
timer reaches the delay at `+0x3C0`. When either flag is set the timer resets and the clock runs. A
delay of zero never holds. `SetDelayShadowMovement` (`0x09156C60`) is called from `SetTimeOfDay`, from
starting and stopping a time demo, and from `UpdateVars`.

## The sun, the moon and the day-cycle scale

`CSky::Update` (`0x09173C60`) converts the clock's seconds from midnight to hours and passes them
through the world's `CDayCycleScale` curve, which returns the fraction of the day
([retail values](../modding/environment-presets.md#the-day-cycle-scale)). That fraction times 2π is
the day angle. The sun and moon rotations are built from it and two angles in `CSky` at `+0x5C` and
`+0x60`, and the results land in the scene state:

| `CSceneSky` offset | Value |
| --- | --- |
| `+0x6C` | Storm factor |
| `+0x94` | Day angle |
| `+0x138` | Sun direction |
| `+0x184` | Moon direction |
| `+0x1A8` | Night factor: `(sun.z - a) / (b - a)` clamped to 0–1, with `a` and `b` the `CSky` members at `+0x84` and `+0x88` |
| `+0x1AC` | Time-of-day coordinate: `(π - acos(sun.z)) / 2π`, or one minus that while the sun's `x` is negative |

:::note[These offsets are the server's, not Dunia.dll's]
In `Dunia.dll` (Steam v1.03) the scene state has the sun direction at `+0x148`, the direction the
clouds are lit from as the moon at `+0x194`, the night factor at `+0x1B8` and the time-of-day
coordinate at `+0x1BC` ([the sky and cloud system](./sky-and-clouds.md)): each of those four is the
server offset plus `0x10`. The storm factor breaks the pattern, `+0x6C` here against `+0x78` there, so
no other offset on this page carries over by adding `0x10`.
:::

`GetNormalizedTimeOfDay` (`0x09156C80`) returns `+0x2F0` of the manager, and the lighting, fog, cloud
and sky presets are all evaluated at it. Its writer is not found, so whether presets follow the raw
clock or the day-cycle scale is open.

## The scene light is the sun or the moon, whichever is brighter

`GetFinalLighting` fills a `CLightingSetup` from the lighting presets:

| Offset | Member |
| --- | --- |
| `+4` | `fLightHDRMul` |
| `+16` | `clrSunLightColor` |
| `+32` | `clrMoonLightColor` |
| `+48` | `fAmbientLightHDRMul` |
| `+64` | `clrAmbientLightColor` |
| `+80` | `clrGroundColor` |
| `+96` | `fRimLightIntensity` |

`CDynamicEnvironmentManager::UpdateLight` (`0x0915F110`) then:

- writes the ambient colour times `fAmbientLightHDRMul` to `CSceneSky` `+0x300`, the ground colour
  to `+0x30C`, and the rim light intensity, clamped at zero, to `+0x134`;
- multiplies **both** the sun's and the moon's colour by the one `fLightHDRMul`;
- compares their luma, weighted (0.3, 0.59, 0.11). If the moon's is greater, the scene's
  `CSceneLight` takes the moon's direction and colour; otherwise it takes the sun's, including when
  both are black;
- sets the light's `fShadowFactor` (`+0x4C`) to 1 and its cast-shadow flag (`+0x48`) on, every frame;
- returns whether the sun won, which `Update` keeps as `IsSunLight`.

So moonlit shadows are in the stock engine. Whether they can be seen is decided by the lighting
preset, the moon's colour against the ambient; retail's values are in
[environment presets](../modding/environment-presets.md#night-lighting).

## Where the shadows point

`CSky::UpdateShadow` (`0x091738A0`) takes `IsSunLight` and runs after `UpdateLight` while the light
casts shadows:

1. It starts from the day angle at `+0x94`. For the moon it adds the arc cosine of the value at
   `+0x178`, wrapped into one turn. What `+0x178` holds is not traced.
2. Angles between π/2 and 3π/2 are mirrored to `π - angle`, so dawn and dusk are handled alike, and
   mirrored back at the end. For the sun, an angle past 3π/2 counts as negative.
3. Below `gfx_SunShadow_InertiaStartAngle` the angle stops following the body and eases toward
   `gfx_SunShadow_InertiaEndAngle`, both in degrees:
   `end + (start - end) / ((start - angle) / (start - end) + 1)`. A low sun or moon never lays
   shadows flatter than the end angle.
4. The eased angle becomes a rotation, combined with `CSky`'s own rotations. The negated direction is
   written to `+0x318`–`+0x320`, and the angle to `+0x324` and back to `+0x94`.

## The clouds are lit from a stretched clock

`UpdateSky` (`0x091601A0`) evaluates the cloud material from the cloud preset, the storm's cloud
preset blended in by the cloud transition curve, and a cloud override. It then evaluates the lighting
presets a second time, at `(t - 0.5) × scale + 0.5` clamped to 0–1, where `t` is the normalised time
and `scale` is the material's `fSunLightColorSamplingScale` (`CCloudMaterial` `+0x34`). Formation,
material and that lighting go to `CSky::SetClouds`. A scale above 1 squeezes the day's lighting into a
shorter span around noon, for the clouds only.

## Measured, not explained

:::info[Verified in a running game]
Retail GOG v1.03, with `mods/sky-overhaul` logging the scene state.
:::

- `SetScriptedStormFactorOverride(1.0, 3)` through the developer console does not lift the storm
  factor the sky reads above 0.10–0.20.
- No rain appears under any weather tried.

## Open

- The writer of `+0x2F0`, and so whether presets follow the day-cycle scale.
- What `+0x178` holds, which offsets the moon's shadow angle.
- What reads the three untraced override blocks.
- How `CEnvironmentAdaptiveBloom`'s `fColorRemap` values become the final pass's per-channel powers
  ([environment presets](../modding/environment-presets.md#the-colour-grade)).
