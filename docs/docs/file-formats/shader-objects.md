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

:::note[Open]
How the engine folds a permutation's `#define`s into the key is not known. Option-bearing
permutations can be enumerated and their objects read, but not addressed by name. Every scheme tried
against a known set of keys (chaining or concatenating the option names in bit order or
alphabetically, XOR or sum of their CRC32s, CRC32 over the 64-bit option mask) reproduces the
no-option key and nothing else.
:::

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
