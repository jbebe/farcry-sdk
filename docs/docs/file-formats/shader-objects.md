---
sidebar_position: 21
---

# `shadersobj` — Compiled shaders

:::info[Verified via reverse engineering]
Container and index layouts decoded from the shipped `shadersobj` export and confirmed by
round-tripping every one of the 1,698 Direct3D 9 objects byte-identically
(`ShaderObjectTests` in JackAll). Path construction traced in `Dunia.dll` (`0x10446180`) via
GhidraMCP. See [intro](../intro.md) for how RE-verified and community-reported claims are
distinguished on this site.
:::

`shadersobj.dat` (archive slot `0x24`) holds every compiled shader in the game, under two parallel
trees: `engine\shaders\obj\` for the Direct3D 9 backend and `engine\shaders\obj10\` for Direct3D 10.
Only the D3D9 tree is described here; the D3D10 tree is plain DXBC in a different wrapper.

Nothing in the tree is named after a shader. A file is addressed by a 32-bit object hash and lives in
one of 128 buckets:

```
engine\shaders\obj\h5d\shadernumber_3ffcc3dd.pso
```

The bucket is the hash's **low seven bits** — `0x3ffcc3dd & 0x7F = 0x5d` — which is why the folders
run `h00`..`h7f` rather than `h00`..`hff`. Three extensions appear: `.pso` (pixel shader), `.vso`
(vertex shader) and `.rs` (render state).

## Object container

A `.pso`/`.vso` is a parameter binding table in front of stock Direct3D 9 bytecode.

| Offset | Size | Field |
| --- | --- | --- |
| 0 | 4 | Magic `8D 06 08 02` |
| 4 | 2 | Offset of the bytecode, always `12 + 8 × count` |
| 6 | 2 | `0x7F7F` |
| 8 | 1 | Kind, `4` in every shipped object |
| 9 | 1 | Parameter count |
| 10 | 2 | `0x7F7F` |
| 12 | 8 × count | Parameter table |
| *bytecode offset* | to EOF | `vs_3_0` / `ps_3_0` token stream |

Each parameter row is:

| Offset | Size | Field |
| --- | --- | --- |
| 0 | 4 | CRC32 of the parameter's name |
| 4 | 2 | Packed register: `binding >> 6` is the register number |
| 6 | 1 | Registers occupied; `0` when the shader does not use the parameter |
| 7 | 1 | Registers per element, `4` for a matrix |

The low six bits of the packed register sort parameters into kinds the corpus only partly explains:
`12` is always a sampler and `8` always a constant, while `0`, `9`, `10`, `16` and `20` also occur.
JackAll preserves the field verbatim rather than reinterpreting it.

### Names survive as CRC32

The bytecode ships **without its CTAB**, so the binding table is the only record of which parameter
feeds which register, and a name is only ever present as its hash. The hash is plain CRC-32 over the
name in its **exact case** — `TEXKILL`, `CelestialBodySampler` — not the lowercasing that archive
paths go through.

Names are recoverable by hashing a candidate dictionary and matching. Against the identifiers in the
September 2008 prototype's HLSL sources, 44,507 of 44,610 bindings across the whole D3D9 tree resolve,
or 99.8%.

The globals every shader inherits are bound at fixed registers, listed in the prototype's
`globalparameterproviders.inc.fx` and registered at runtime by `CViewportShaderParameterProvider`
(`Dunia.dll:0x103788f0`). `ViewProjectionMatrix` is always `c4`, `FogColorVector` `c48`,
`BloomAdaptationFactor` `c58`, `SunOcclusionFactor` `c63`. A shader's own parameters start at `c71`.
`SkyColor`, `GroundColor` and `LightColor` are among those own parameters rather than the globals, so
their register changes from one object to the next.

### Naming a draw in a running game

:::info[Verified in a running game]
Retail GOG v1.03, logged from an FCSE plugin hooking the device's draw calls.
:::

The bytecode a bound shader returns through `GetFunction` is the bytecode stored in its object here,
byte for byte. A draw seen at the device is therefore named by matching that bytecode's size and
CRC-32 against the export. Not every draw matches: some vertex shaders bound in the sky pass match no
object in the export.

### Finding an object by what it binds

A permutation whose index key is unknown can still be found by a parameter it binds, since every
binding row carries the name's CRC-32. Searches over the D3D9 `obj` tree:

| Parameter | Objects | Register |
| --- | --- | --- |
| `DepthVPSampler` | 74, bytecode 196 to 5,332 bytes | `s0` in every one |
| `Saturation`, `ColorRemapData`, `ContrastData` | 7 | `c71` to `c73` in the 244, 316 and 388 byte objects; `c73` to `c75` in the 440, 496, 512 and 568 byte ones |
| `WindSimParamsX` and `MeshDecompression`, without `WorldMatrix` | 39 `.vso`, the grass material | — |
| `LeavesMorphFact`, `LeavesEquations`, `DistanceFactors` or `LevelLOD`, without `TrunkStencil` or `TrunkUVDecompression` | 97 `.vso`, tree leaves | — |

The grass material places its instances from vertex data, so its per-instance wind and rotation
never appear as constants; the combination above is what is left to find it by. The tree trunk binds
`DistanceFactors` and `LevelLOD` too, which is why the trunk's own constants rule it out.

### Grass lit by the sun is built at runtime

:::info[Verified via reverse engineering]
Measured on GOG in savannah and jungle, by the bytecode of every grass draw, over three launches
that gave the same CRCs.
:::

The 39 grass `.vso` draw grass only into depth: the linear depth pass, and its alpha-tested near
grass. Grass lit in the HDR scene pass comes through vertex shaders that match no object, which the
engine builds for itself:

| Vertex shader | Pixel shader | Draws |
| --- | --- | --- |
| `736F4429` | `D26F57AE`, `B65F81B2` with alpha | lit by the sun, one shadow map slice |
| `50B69F13` | `FA083E8F` | lit by the sun, shadow cascades |
| `0701D375` | `D26F57AE` | the same for bent clumps, placed through a rotation from the instance data |
| `A08FE8BF`, `7601537E` | `ABB7093C` | lit without a shadow |
| `05D4DC7E`, `94B54206` | `5D532300` | an additive light pass, depth test equal |

Every grass vertex shader, shipped or built, turns each clump by `GrassCylindricalBillboardMatrix` at
`c32` to `c34`, reading only `xyz`: one camera heading shared by every clump, so each faces the eye.
The savannah meshes are flat sheets whose blades all face within about 22° of one direction, which
is why the turn is needed.

The lit pair splits the work so that the vertex shader holds all the light. The pixel shader draws
`texture × (TEXCOORD1.rgb + TEXCOORD2.rgb × shadow)`, fogged by the two `.w` values. Per vertex,
`736F4429` computes:

- ambient: `(instance colour × 2.5 + the clump's sky occlusion) × SkyColor × ½`;
- sun: `saturate(ground normal · sun) × LightColor`, plus a glint `pow(…, 7)` on a pattern built from
  position and wind rather than a normal;
- both × instance colour × vertex colour × 4, and × `1 +` a wave from the wind's lean.

Its registers match the prototype's global table (`ViewProjectionMatrix` `c4`, `CameraPosition`
`c45`, `FogColorVector` `c48`, `FogValues` `c51`, `FogHeightValues` `c52`, `WindSimParamsX` and
`WindSimParamsY` `c65` and `c66`) and then `ShadowProjectionMatrix` `c71` to `c73`,
`MeshDecompression` `c74`, `LightColor` `c75`, `LightDirectionWS` `c76`, the shadow's fades `c77`
to `c79`, `SkyColor` `c80` and `DiffuseTiling1` `c81`. The shadowless pair moves `LightColor`,
`LightDirectionWS` and `SkyColor` to `c72` to `c74`. A replacement that repeats `736F4429`'s
placement instruction for instruction lands on the depth the depth pass wrote; Sky Overhaul's
`grass.fx` does.

### How a shadow read spreads its samples

Every one of the 272 retail pixel shaders that binds `ShadowMapSize` reads the shadow map 8 times
around the point and averages, with the samples `ShadowMapSize.zw`, one texel, apart times a
factor. The 158 that pick their cascade per pixel carry the factor in one `def` (encoded
`0x05000051`): `0.5, 1/6, 1/3, 1`, the near, middle and far slice's spread in its own texels,
chosen by a `dp4` against the one-hot slice. Of the 114 others, 87 bind `CascadedShadowTexelScale`
and take the factor from its `x`, set per draw; the remaining 27 were not examined. No other
register is read with `ShadowMapSize.zw`, so scaling it widens every shadow alike; scaling the `def`
widens the farther slices alone.

A row's registers-occupied field is nonzero for many viewport globals in every shader searched, the
water reflection constants in grass among them, so a global's row alone does not show that a
shader reads it. The material's own parameters are the ones to search by.

The seven are the final pass of the prototype's `posteffect_adaptivebloom.fx`. The four larger ones
also bind `BloomParams` at `c71`, `LuminanceRange` at `c72` and `LuminanceAdaptationRange` at `c76`
with the adapted luminance at `s2`; the 244-byte one binds no bloom at all. No retail object binds the
prototype's `PowLUTSampler` variant.

## Render states

A `.rs` is **plain text**, one `Key=Value` per line, and matches the `technique` block of the shader's
source one for one:

```
AlphaBlendEnable=true
BlendOp=Add
ZWriteEnable=false
CullMode=None
```

Only states the source names are present. Anything absent is inherited from whatever the renderer set
last, so a shader that never mentions `ZEnable` does not control its own depth test. Editing one of
these needs no compiler.

## Index tables

Three tables sit at the root of each tree — `index.pso`, `index.vso`, `index.rs` — mapping a shader
permutation to the object it loads.

| Offset | Size | Field |
| --- | --- | --- |
| 0 | 4 | File size |
| 4 | 4 | `"DAEH"` |
| 8 | 4 | Version, `10` |
| 12 | 4 | File size − 4 |
| 16 | 4 | File size − 24 |
| 20 | 4 | Zero |
| 24 | 4 | Entry count |
| 28 | 8 × count | Entries, sorted ascending by key |

Each entry is a 32-bit permutation key and a 32-bit object hash. A hash of zero is a real answer
meaning the permutation binds no object of that kind — a shader that overrides no render state, say.

The D3D9 tables carry 147,140 permutations for `.pso`/`.vso` and 147,200 for `.rs`, against 1,698
objects actually present in the export: one object serves many permutations, and permutations that
differ only in a define with no effect on the PC build (`TEXKILL`) share one file.

**The permutation compiled with no options keys on the CRC32 of the shader's source name.** That is
confirmed for 30 shaders, and it is how a named shader is located at all. Shaders whose option domain
has no empty case — `cloudlayer`, which requires `LAYER1` or `LAYER2` — correctly have no such entry.

**An option-bearing permutation keys on the CRC32 of a string built from its options**
(`BuildPermutationKey`, `0x104267a0` in the GOG build, **RE-verified**):

```
<shader name>, then for each option the permutation sets, in ascending CRC32 of the option's name:
    "_" + the option's index in the shader's define list
    + "-" + (value - 1) + "of" + max      only for an option that has a maximum value
```

The index counts the shader's `<define>`s in declaration order — the order of the second name list in
`fastinitdata_d3d9.bin`. A boolean option has no maximum and adds only `_<index>`. So
`celestialbody_3_4_2_5` is `celestialbody` with `TEXKILL`, `ADDITIVE`, `TOD_COLOR` and `FAKEHDR` set,
and `burnterrain_0-0of31` to `burnterrain_0-31of31` are the 32 values of a ranged option. Checked
against the retail `.pso`/`.vso` index: all 26 `celestialbody` permutations and all 32 `burnterrain`
ones are found **(seen in data)**. How the define list is built for a shader whose defines come through
`xi:include` or carry a `platform=` attribute (`aaaleaf`, for one) is not checked.

## What `fastinitdata_d3d9.bin` holds

`common\engine\shaders\fastinitdata_d3d9.bin` is a big-endian table naming every shader and the
options it was compiled with. Strings are 2-byte length-prefixed ASCII. Each option record carries a
32-bit CRC32 of its own name and a small integer that is its bit position in the 64-bit option mask
the engine passes around — for `celestialbody`: `TEXKILL` 31, `FAKEHDR` 33, `ADDITIVE` 34,
`TIME_OF_DAY_COLOR` 35, `VISIBILITY_TEST` 36, `TIME_OF_DAY_MAPPING` 37.

## Tooling

```
jackall-cli shader index engine/shaders/obj --shader celestialbody
jackall-cli shader extract shadernumber_3ffcc3dd.pso -n names.txt
jackall-cli shader build  shadernumber_3ffcc3dd.bin
```

`extract` splits an object into its bytecode (`.bin`, which `fxc /dumpbin` disassembles) and its
binding table (`.xml`); `build` reassembles them, dropping the constant table `fxc` emits so the
result matches the shipped convention. See
[replacing a shader](../modding/replacing-a-shader.md) for the whole loop.
