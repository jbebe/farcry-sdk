---
sidebar_position: 5
---

# `.xbm` / `.xbg` — Materials & Meshes

:::note[Community-reported]
Source: Discord, Far Cry 2 Multiplayer, `modding` channel, an extended live reverse-engineering
exchange between **Gabor** (unreleased XBM↔XML/XBG↔XML converter, built over "years") and **fdx4061**
(author of an XBM editor and an XBG texture/material extractor), April 2026 — the deepest byte-level
documentation of this format found anywhere in the community. Not yet independently verified by
disassembly; see [intro](../intro.md) for how RE-verified and community-reported claims are
distinguished on this site.
:::

`.xbm` (materials) and `.xbg` (meshes) are structurally the same file format under different
extensions: "they are the same files in a way, just with a different extension... they have the same
structure." An XBM parser can read into an XBG's material data by skipping the XBG-specific mesh
(`LTMD`) content, and vice versa — the section-parsing logic is shared.

## Container shape

**FourCC-style section tags, stored reversed in the byte stream**: `HSEM` (= "MESH" reversed, the
header/section marker), `EDON` (= "NODE"), `DIKS`/`SULC` (submesh/mesh-block data — `SULC` holds one
block per mesh/submesh, each delimited by `FFFF` marker bytes). A classic reversed-FourCC chunk format.

**Section-parsing algorithm** (the actual working algorithm behind both Gabor's and fdx4061's
independent tools): after the header, the file is a fixed, always-present sequence of sections in
constant order; every section begins with a count of the elements it contains, and a count of `0` means
skip immediately to the next section. Confirmed section order for XBM materials:

1. **maps** (texture references)
2. **reflection** (a single f32)
3. **tiling** (2 f32s)
4. **colours/RGB** (3 f32s — diffuse base colour)
5. **illumination/RGBA** (4 f32s)

In practice only one of the colour-RGB or illumination-RGBA blocks is actually populated for a given
material, even though the format always reserves space for both. A section reserved for road-texture-
only data in FC2 is present but "always empty" outside that use, and is used more heavily in Avatar.

**Field types**: everything in these sections is `f32`, **except the very last section, which is
`u32`**. This was a point of disagreement between the two authors: treating these values as clamped
0–255 RGB integers (as one early editor's UI did) silently loses functionality the engine's real values
support — driving a texture's colour value far above normal makes it visibly glow, useful for
tiling/glow effects, but only if the values aren't clamped to a byte range.

**String padding**: every string in these files is followed by a 1-byte zero-padding, consistently.

**XBG-only alignment**: the start of the actual 3D mesh data inside an XBG must be 16-byte aligned
(offset divisible by 16), or the game breaks on load. Does not apply to XBM (material) files, which
carry no 3D data — some converters apply the rule to XBM anyway without it mattering either way.

## The shading model, read out of the engine's own shaders

:::info[Verified via reverse engineering]
Disassembled from the shipped shader objects. Everything in this section is the engine's own code,
not inference from the asset files.
:::

`Data_Win32/shadersobj.fat` carries the compiled shaders under two trees:

| Tree | Build | Names |
|---|---|---|
| `engine/shaders/obj/*.pso`, `.vso` | Direct3D 9 | stripped |
| `engine/shaders/obj10/*.pso`, `.vso` | Direct3D 10 (DXBC) | **intact** |

The D3D9 tree is the one that looks like a dead end: the bytecode begins 78 bytes into a proprietary
wrapper and carries no `CTAB`, so every constant is a bare register number. The `obj10` tree is plain
`DXBC` from byte 0 with its `RDEF` reflection chunk intact, which means constant-buffer names, field
offsets, sampler names and the template name all survive. Any DXBC disassembler reads it directly:

```
fxc /nologo /dumpbin shadernumber_06480e00.pso
```

Filenames are permutation ids, not paths, so the practical way to find a template is to grep the tree
for a constant name (`DiffuseColorBase`, `MaskTexture1`) and disassemble the hits.

### The `Generic` pixel shader

`Generic` covers about two thirds of the retail material set. Two diffuse layers and a mask, where
the mask decides both how much of layer 2 shows and how far layer 1's tint travels:

```
uvD1   = uv0 * DiffuseTilingAndGroup1.xy + uv1 * DiffuseTilingAndGroup1.zw
uvD2   = uv0 * DiffuseTilingAndGroup2.xy + uv1 * DiffuseTilingAndGroup2.zw
uvM    = uv0 * MaskTilingAndGroup1.xy    + uv1 * MaskTilingAndGroup1.zw

d1     = tex2D(DiffuseTexture1, uvD1)
clip(d1.a - AlphaValues.x)                  // alpha test, layer 1's alpha only
mask   = tex2D(MaskTexture1, uvM)

layer1 = d1.rgb * lerp(DiffuseColorBase, DiffuseColor1, mask.b * saturate(vertexColour.b))
layer2 = tex2D(DiffuseTexture2, uvD2).rgb * DiffuseColor2

albedo = lerp(layer1, layer2, mask.g * saturate(vertexColour.g))
alpha  = d1.a
```

The two weights are worth stating separately, because neither is guessable from the asset files and
both are easy to get half-right:

- **Layer-1 tint weight** is `MaskTexture1.b × vertexColour.b`. Reading the vertex channel alone
  paints the full `DiffuseColor1` over the whole surface — on a material whose `DiffuseColor1` is a
  strong colour that turns a weathered wall into a flat repaint.
- **Layer-2 blend weight** is `MaskTexture1.g × vertexColour.g`.

A material naming no mask samples white, which is why an unmasked surface takes its tint at full
strength. `DiffuseColorBase` is *not* dead data even though the vertex channel is 1.0 on roughly
three quarters of retail vertices — the mask is what brings it in.

Sampler bindings are `t0` `MaskTexture1`, `t1` `DiffuseTexture1`, `t2` `DiffuseTexture2`. The
`Generic` constant buffer's 16-byte slots are `[1]` `DiffuseColorBase`, `[2]` `DiffuseColor1`,
`[3]` `DiffuseColor2`, `[7]` `DiffuseTilingAndGroup1`, `[8]` `DiffuseTilingAndGroup2`,
`[11]` `MaskTilingAndGroup1`.

### Tiling and the "group" half

Each `…TilingAndGroup` constant is `float4(tilingU, tilingV, groupU, groupV)`. The `.xy` half scales
UV set 0 and the `.zw` half scales UV set 1, so a texture bound to the second set carries its tiling
in `zw` and zeroes in `xy`. The `.xbm` stores the two halves apart: `DiffuseTiling1`,
`DiffuseTiling2` and `MaskTiling1` as `float2`, plus `UVGroupMapChannel0..3` mapping a group index to
a UV channel.

`UVGroupMapChannel0` is 0 on every retail material and layer 1 always sits on group 0, so layer 1
always reads UV set 0. Which group the mask and layer 2 use is **not recorded in the `.xbm`** — only
the group-to-channel table is — and it could not be recovered by correlating tiling values against
the table, so that mapping is still open.

Tiling is a real number, not a formality: 1,227 of 2,235 retail materials tile at something other
than 1, up to 20×.

### The lighting around the albedo

The same shader family shows what the engine does with the albedo once it has it. The terrain pixel
shader (the mesh templates share the structure) computes:

```
sun     = _LightColor * shadow                           // shadow: the baked self-shadow map
ambient = lerp(hemiGround, _SkyColor * hemiAO, N·up * 0.5 + 0.5)
colour  = albedo * (ambient + saturate(N·L) * sun)
        + specularColour * sun * pow(saturate(N·H), shininess)
```

Three structural facts, each visible if a renderer gets it wrong:

- **The ambient is a directional hemisphere, not a scalar** — a ground colour below, a sky colour
  above, blended on how far the normal points up.
- **Ambient and sun add before the albedo multiplies**, so a fully lit surface runs brighter than
  its texture and a shadowed one keeps colour from the sky term.
- **Specular is a separate additive Blinn term.** The `.xbm` carries its inputs as
  `SpecularColorBase`/`SpecularColor1` (the same pair shape as the diffuse tints) and
  `SpecularPower`; measured across the 2,208 retail materials, 2,129 carry a non-zero power (2–20,
  mode 8 — broad lobes), 466 author colours past 1 up to 2.0, and 1,743 name a `SpecularTexture1`
  that scales the highlight per texel. A renderer without that map has to tame the authored colours
  or a highlight covers half the surface.
- **The baked terrain shadow scales the sun rather than replacing `N·L`** — see
  [`.sdat`](sdat.md#sd_shadowxbt-carries-the-suns-angle--and-the-engine-multiplies-it-in-anyway).

## Vertex layout, measured across the retail set

:::info[Verified against the retail corpus]
Measured over all 2,922 `.xbg` files in `worlds.fat`, and cross-checked against the vertex shader
that consumes them.
:::

- **Two UV sets.** 2,905 of 2,922 meshes (99%) carry UV set 1 — bit `0x0800`, part of the common
  `0x0BCA` static layout. None carry a third. The second set is what the tiling "group" half reads.
- **UV decompression is the `PMCU` chunk.** The vertex shader runs both texcoords through one
  `_MeshDecompression.zw` pair as `raw * scale + translate`, which is exactly `PMCU`'s translate and
  scale. UVs stay in D3D space, where V=0 is the texture's top row.
- **Vertex colour is a mask, stored BGRA.** The vertex shader emits it as `colour.zyxw`, i.e. it
  swizzles the buffer's BGRA into RGBA before the pixel shader sees it. Green and blue are the two
  blend weights above; every mesh in the set carries the attribute.
- **Triangles wind clockwise** around their authored normal — the D3D convention. Measured at 99–100%
  per file across characters, vehicles, props and buildings. Renderers that assume OpenGL's
  counter-clockwise default will treat every outward-facing triangle as back-facing, which silently
  breaks backface culling and any lighting that keys off facing.
- Vertex-format flags seen in retail: `0x0BCA` (static, 32-byte stride), `0x0BDA` (skinned, 40-byte),
  `0x008A` (16-byte).

## Parts, damage states and wardrobes

:::info[Verified against the retail corpus]
:::

The `DNKS` chunk names each drawable block, and that name — not its index — is the part's identity.
Names carry a `_LOD<n>` suffix which is the LOD tier, and the same part appears once per tier.

**A rigid part's vertices are stored around its own pivot, not in model space.** What places it is
the `EDON` bone that *shares its name*: take that bone's world matrix and transform the part by it.
Skip this and every wheel, door and bumper piles up at the model origin. Roughly half of retail parts
have a non-identity placement, so the error is obvious on vehicles and invisible on a single-part
prop. Sibling parts frequently share one vertex buffer and place it differently, so placement has to
be applied per part rather than per buffer.

**Skinned meshes are the exception**: their vertices already sit in the skeleton root's bind space,
so they take the root bone rather than a same-named one. That root is at the character's waist,
around z = +1.0 — using the wrong one sinks a character to the knees through the floor.

**A file holds every state a part can be in, and the engine draws one.** Roughly 1,162 parts across
the set carry a `STATE<n>` tag: a vehicle body intact and wrecked, a door closed/ajar/open, a road
sign whole and snapped. The lowest number is the intact, closed, unbroken one. The comparison has to
be made **per part**, not per file — 616 part groups in the retail set have no state 1 at all, so a
file-wide minimum deletes them.

**Character files are wardrobes.** `characters/mercenaries/merc_kit.xbg` holds 111 parts — every
head, hat, shirt, vest, trouser, boot and ethnicity-specific arm and chest a mercenary can be built
from, 77,193 triangles in total. A single NPC wears about a dozen of them, roughly 7,700 triangles.
Which dozen is on the entity, not in the mesh — see
[`hidMeshName`](../engine-internals/entity-instancing.md#hidmeshname-picks-parts-out-of-a-wardrobe).

## Character/creature bone palettes

Neither author understood the `SULC` submesh blocks' bone-palette data for character XBGs as of April
2026 ("I don't know how bone palettes work... that's why I still can't create a character-type xbg" —
fdx4061). By June 2026, **Quiet_Joker** (author of the `Dunia-Engine-XBG-Blender-Importer`, see
[Sources](../modding/sources.md)) worked out the actual mechanism:

An `.xbg` model's bones are a local, pruned subset of a full master "source skeleton" file
(`.mab`/`.skeleton`) — one shared per model *category* (e.g. all NPCs), containing every bone ever
defined for that category at development time, from which each individual model's bones were derived.
**Animation does not use the xbg's own local bone order** — because bones get pruned per-model, that
local order isn't stable across models — so the engine looks up the master skeleton file as a reference
for which bones move, then applies that to the local xbg model at animation time. **Three files are
required together for a custom/replaced model to animate correctly**: a `.mab` motion/animation-bank
file, an `.xbg` that references it, and the category's master `.skeleton` reference file. This
bone-inheritance pattern is a known technique from professional animation tooling (Maya), not a
Dunia-specific oddity.

## Import/export tooling

**`Dunia-Engine-XBG-Blender-Importer`** (Quiet_Joker) is the current working answer to custom mesh
import, v3.0 released 2026-07-04. Originally built for *Avatar: The Game*, ported from a Blender
2.49b-era script lineage; FC2 support works "more or less" because "Avatar shares the same stuff as far
cry 2." Confirmed working: static object import, character import (with some broken clothing/UV-tiling
material loading), and a real export/injection workflow — import with "Separate Primitives" on, edit or
replace geometry in Blender, select only the objects to write out, then export (the script writes
whatever is currently selected). Confirmed broken as of its release: weapon XBG import (reproduced on
the AK-47 and a 1911), HKX (Havok collision mesh) export, and export reliability generally reported by
at least one other user. FC2 `.xbg` files live in `Data_Win32\worlds\worlds.fat`, not the more obvious
`common` archive. Treat as pre-alpha but actively developed.

A second, separate XBG-injection path was mentioned in passing but not verified or linked further: "the
only way of importing xbgs back into the game for fc was to use the unreal engine tool made by
id-daemon" — id-daemon is independently credited elsewhere for FCBConverter (see [Getting
Started](../modding/getting-started.md)).

A modder porting FC2 models into FC3 (Ganic, several models ported 2023–2024) hit the same wall from
the opposite side: raw geometry porting works, but there's no clean `.xbm` (material) converter, and
rebuilding an `.xbg` from scratch fails on material indices/bone weighting — the same unsolved edge of
this format the FC2-side investigation above was working from.

A cross-game skeleton-reading tool (`SkeleTree`, fdx4061, preserved in
`research/reference-files/tool-archives/`) works across Avatar (2009), Far Cry 2, and Far Cry 3 — direct
evidence these three titles share a compatible skeleton/rig format at the binary level, beyond the
broader Dunia lineage.

`.spk` filenames are themselves hashes (e.g. `004492b8.spk`), consistent with the hash-based naming
established for FCB/archive content — see [`.spk`](./spk.md).
