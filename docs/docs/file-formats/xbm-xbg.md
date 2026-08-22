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
content, and vice versa — the section-parsing logic is shared. (The community account names `LTMD` as
that mesh content; it is in fact an embedded material, and appears in only three shipped meshes — see
[Chunk framing](#chunk-framing-read-out-of-the-engines-own-loader) below.)

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

## Chunk framing, read out of the engine's own loader

:::info[Verified via reverse engineering]
Read out of `LoadGeomResource` (`0x097fd440`) in the symbol-bearing `FarCry2_server` binary, and
checked against all 3,133 shipped `.xbg`. A reader written from it walks every file with **zero
trailing bytes**, and a writer reproduces all 3,133 **byte for byte**.
:::

The community description above works in practice but treats the chunk header as 12 bytes with the
payload immediately after it. The engine reads a **20-byte header**, and the payload is addressed
**backwards from the end of the chunk**:

```
+0x00  u32   tag, stored reversed — 'EDON' in the file is 'NODE' to the engine
+0x04  u32
+0x08  u32   chunk size, including this header
+0x0C  u32   payload size
+0x10  u32   sub-chunk count

payload = chunkStart + chunkSize - payloadSize
```

For every chunk in every shipped file that resolves to `chunkStart + 20` — **except `DNKS`**, which
is the only chunk in the format with a sub-chunk (`subChunkCount` is 1 in all 3,133 files). Its
sub-chunk is `SULC`, framed the same way, and `DNKS`'s own payload sits *after* it:

```
DNKS
  +0x00   20-byte header, subChunkCount = 1
  +0x14   SULC sub-chunk — per skin descriptor: u32 cluster count, then that many
          110-byte clusters (7 x u16 header, then 48 x int16 bone palette)
  ...
  end - payloadSize:  u32 descriptor count, then per descriptor:
                      52-byte meta, u32 name length, name, NUL
```

That backwards-addressed payload is why parsers written from the community description need a
hand-tuned "preamble" constant to find the `DNKS` name table.

The 52-byte meta is a LOD metric, ten floats of bounds, the LOD tier and a reserved word:

```
+0x00  f32      LOD metric
+0x04  f32[3]   bounding sphere centre
+0x10  f32      bounding sphere radius
+0x14  f32[3]   AABB min, over this part's own vertices
+0x20  f32[3]   AABB max
+0x2C  i32      LOD tier, matching the name's _LOD<n> suffix
+0x30  u32      reserved
```

The box matches the part's vertices in **16,885 of 16,885** shipped parts, once the comparison
allows one position-quantisation step — the bounds were fitted before the positions were quantised
to int16. Both are in the part's **own** space, not model space, so the placement node is not applied.
The sphere is a fitted one rather than the box's circumscribed sphere: its radius is the exact
distance to the furthest vertex in 99.3% of parts, but its centre is the box centre in only 5.6%, so
it comes from a minimal-enclosing-sphere fit. `XOBB`/`HPSB` carry the same pair for the whole model,
in model space, and their sphere behaves the same way.

This retires the community reading of these ten as a bare min/max pair, which holds for 18 of 18,533
— it was reading the sphere as the box.

**Alignment padding is a descending byte counter, not zeros.** Nine bytes of padding before the
vertex or index section are written `09 08 07 06 05 04 03 02 01`. A file padded with zeros still
loads, but no longer matches the original byte for byte.

Chunk tags are **not unique within a file** — `objects\lights\torch01.xbg` carries two `LTMD` chunks.
A parser that keys chunks by tag silently drops data.

### Chunk census

Every shipped `.xbg` is version `0x0006002A` and carries these ten chunks exactly once:
`LTMR`, `EDON`, `DIKS`, `DNKS`, `SDOL`, `XOBB`, `HPSB`, `DOL\0`, `PMCP`, `PMCU`. Three more appear
conditionally:

| Chunk | Engine name | Files | Contents |
|---|---|---|---|
| `MB2O` | `O2BM` | 78 | `u32` count, then that many 64-byte inverse-bind matrices, column-major |
| `ADKI` | `IKDA` | 10 | IK data, 52 bytes per entry. Byte-identical in all ten, all destructible props |
| `LTMD` | `DMTL` | 4 chunks in 3 files | an **embedded material**, loaded by `CMaterialResource::LoadFromGLM` |

`LTMD` is therefore not "XBG-specific mesh content an XBM parser skips" as described above — it is a
material inlined into the mesh instead of referenced by path, carrying names like
`Torch01_587110454_0.fakemat`. `LTMR` (`RMTL`) is the ordinary path-reference material list, and it
carries a trailing word after mesh version 41.3, which FC2 (42.6) has.

**An inline `LTMD` is not laid out like an `.xbm`'s.** A standalone `.xbm` opens its `LTMD` payload
with five bytes, then the material name, then the shader name. An embedded one has no such preamble:
it opens with the name the mesh's `LTMR` list references, then the `DNKS` part that name belongs to
(`BAT`, `WOODTORCH01`, `CLOTHTORCH01`, `RAG01`), then the shader name. From there the two run the
same body — counted texture slots, property groups of one, two, three and four floats, integers,
then a trailing word. Reading an embedded chunk with the standalone layout desynchronises on the
first field. Measured on all four shipped chunks, each consuming its payload exactly; the three
meshes carrying one resolve their textures only through this path.

### `DIKS` is the part table, and it names each part's placement node

`DIKS` (`SKID`) entries are **8 bytes**, not 4, and they are not LOD switch distances. The chunk is a
`u32` count followed by one entry per `DNKS` part:

```
+0x00  u32  CRC32 of the full part name, "FRAME_LOD0" — the exact-case DNKS name
+0x04  u32  (placement node index, or 0xFFFF when none) << 16 | this entry's position
```

Measured across all 2,922 shipped meshes and 16,885 entries:

| Claim | Result |
|---|---|
| word 0 hashes to a `DNKS` part name | 16,885 / 16,885 |
| entry count equals part count | 2,922 / 2,922 |
| low 16 bits equal the entry's own position | 16,885 / 16,885 |
| every skinned part carries `0xFFFF` | 1,645 / 1,645 |

So a mesh states each part's placement node outright, and nothing has to match part names against
node names. Matching by name is not merely slower, it is wrong: 291 parts fold to a different node
than `DIKS` names, because those meshes have a node whose *own* name ends in `_LOD0`
(`ColonialChurchBellRinger_01_LOD0` sits beside the root `ColonialChurchBellRinger_01`, and stripping
the suffix from the part name matches the root). Judged by which placement makes a model's LOD0
geometry fill its own `XOBB` bounds, `DIKS` beats name matching in 3 meshes and loses in none;
`urbanmedium00_gazebo02_part02_bk` sits 65% outside its declared bounds under name matching and
within 1% under `DIKS`.

`0xFFFF` does not mean "no such node exists" — it means no node transforms this part. Both skinned
parts and rigid parts already modelled in model space carry it, including ones that do have a
same-named node (`urbanlarge01_cs_door` has nodes `s1` and `s2` and marks both parts `0xFFFF`).

### A LOD's geometry is a plain concatenation

`SDOL` holds one flat vertex block and one flat index block per LOD, and the submesh table says how
they are divided. The division is completely regular, which is what makes an exporter possible:

- A cluster's vertices are `vertex_count` of them starting at the running total for **its own
  buffer**, counted in submesh order.
- Its indices are `face_count * 3` of them starting at the running total for the **whole LOD**, also
  in submesh order, and address the buffer directly.
- Buffers sit end to end in the vertex block, in buffer order.
- Nothing is left over at the end of either block.

Measured across every shipped mesh: 29,296 of 29,296 clusters and 9,746 of 9,746 LODs, with no
exceptions to any of the four. Two consequences worth knowing before writing a reader:

**Every vertex is referenced by a triangle** — 29,296 of 29,296 clusters have no spare vertices, and
no shipped mesh contains a degenerate triangle or a `0xFFFF` strip-restart index. A reader that
compacts a cluster's vertices down to the referenced set is therefore doing nothing but permuting
them, which quietly makes a re-export differ from the original.

**`cluster.stride` always equals its buffer's stride**, so it is a duplicate, not an override.

### `EDON` node record

68 bytes, `memcpy`'d wholesale by the engine, then a length-prefixed NUL-terminated name:

```
+0x00  u32     CRC32 of the exact-case name — 0 on the root, and not read at runtime
+0x04  u32     first child, 0xFFFFFFFF when none — not read at runtime
+0x08  u32     next sibling, 0xFFFFFFFF when none — not read at runtime
+0x0C  u32     parent, 0xFFFFFFFF on the root
+0x10  f32[4]  local rotation, xyzw
+0x20  f32[3]  local translation
+0x2C  f32[3]  local scale
+0x38  i32     skinIndex — -1, or an index into MB2O
+0x3C  f32     1.0 in all 16,738 shipped nodes
+0x40  f32     constant per file in 3,123 of 3,133 files, median 0.97x the XOBB bbox diagonal
+0x44  u32     name length, then the name, then a NUL
```

`CGeomResource::GenerateMatrices` (`0x097fc880`) builds each node's world transform as
`parent_world x TRS(scale, rotation, translation)`, so the scale at `+0x2C` is live — 121 shipped
nodes carry a non-unit value.

## The `.xbm` body, and writing one back

:::info[Verified against the retail corpus]
Measured over all 2,379 shipped `.xbm` files, and round-tripped byte-identically through
`tools/BlenderFC2`'s writer.
:::

The community account that an `.xbm` and an `.xbg` are the same format is exactly right, and stronger
than stated: **an `.xbm` is an `.xbg` carrying an `LTMD` chunk and no geometry**. All 2,379 shipped
materials have the byte-identical chunk layout

```
LTMD EDON DIKS DNKS SDOL XOBB HPSB DOL\0 PMCP PMCU
```

so a mesh writer that treats `LTMD` as opaque already emits a correct `.xbm` container — 2,379 of
2,379 come back byte-identical with no material-specific code at all. Only the `LTMD` payload needs
its own serialiser.

That payload is a run of counted sections:

```
u8[5]           preamble, which no traced code path reads
cstring         material name          e.g. SAWEDOFF_SHOTGUN_METAL_CHROME
cstring         shader name            e.g. Weapon
u32 count, then count x (cstring path, cstring slot)      textures — path first, then its slot
for width in 1, 2, 3, 4:
  u32 count, then count x (cstring key, f32[width])       float properties, grouped by width
u32 count, then count x (cstring key, u32)                integer properties
u32             trailing — 0 in all 2,379
```

`cstring` is the same length-prefixed, NUL-terminated form the `.xbg` chunks use. The float sections
are what split `DiffuseTiling1` (a `float2`) from `DiffuseColor1` (a `float3`) — a key's width is
carried by which group it sits in, not by the key.

**A section may repeat a key, so a reader that stores properties in a map loses data.**
`FATHER_MALIYA_HAIRHELMET` (`worlds/worlds/graphics/_materials/fchappart-m-2008041057227927.xbm`)
lists `OmniSpotLightingDisabled` twice in its integer section, both times 1. It is the only material
in the set that does, and it is enough to break a byte-exact round trip: keep the entries in file
order alongside whatever map the reader exposes.

### Shader and slot census

| Shader | Materials |
| --- | --- |
| `Generic` | 1,626 |
| `Cloth` | 265 |
| `Skin` | 142 |
| `Weapon` | 102 |
| `RealtreeTrunk` | 51 |
| `Unlit` | 49 |
| `Hair` | 37 |
| `Vehicle` | 34 |
| `Leaf` | 25 |
| `Road` | 18 |
| `BigLeaf` | 12 |
| `Water` | 7 |

Texture slots, by how many materials name them: `DiffuseTexture1` 1,964, `SpecularTexture1` 1,889,
`NormalTexture1` 1,656, `MaskTexture1` 1,565, `DiffuseTexture2` 1,503, `RimLightTexture` 397,
`BloodTexture` 393, `FabricTexture` 264, `PrintTexture` 239, `ReflectionTexture` 222, `SkinTexture`
142, `NormalTexture2` 135, `MaskTextureBroken` 102, `BurntDiffuseTexture` 51, `MaskTexture0` 34,
`SpecularID` 25.

### The `Weapon` shader's parameter set

The 102 `Weapon` materials extend `Generic`'s inputs with the weapon-degradation system:

- **`MaskTextureBroken` and `MaskTilingBroken`** — a second mask, alongside `MaskTexture1`.
- **A `Clean`/`Broken` triplet for every colour.** `DiffuseColor1`, `DiffuseColor1Clean`,
  `DiffuseColor1Broken`, and the same for `DiffuseColorBase`, `DiffuseColor2`, `SpecularColor1` and
  `SpecularColorBase`. The unsuffixed key is what the shader reads; the two suffixed ones are the
  ends the weapon's condition interpolates between.
- **`ReflectionTexture` and `ReflectionPower`** on some of them — the sawed-off's black-metal
  material names `graphics\_textures\cubemap\lens_cubemap.xbt` at power 0.9.

The mask channels behave as `Generic`'s do. Measured on the sawed-off's own shipped
`sawed_off_shotgun_state01.xbt`: **green is 0.000 in every texel**, red averages 0.556, blue averages
0.439, alpha is 1. So a shipped weapon never blends its second tiling layer in at all — the whole
look is layer 1 tinted between `DiffuseColorBase` and `DiffuseColor1` by the mask's blue.

`MaskTiling1` is 1,1 on all three of the sawed-off's materials while `DiffuseTiling1` runs 6 to 12,
which is the shape to expect: the mask is per-model and in the model's own UVs, the detail maps tile
over it.

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
  blend weights above; every mesh in the set carries the attribute. It is real data, not padding:
  across 8,550,866 shipped LOD0 vertices, **45.3% carry a non-white RGB**. A writer that emits white
  is asking for both weights at full strength, which is right for a weapon (the sawed-off and the
  AK-47 are white in RGB, varying only in alpha) and wrong as a general default.
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

**A rigid part's vertices are stored around its own pivot, not in model space.** What places it is an
`EDON` node — and [`DIKS`](#diks-is-the-part-table-and-it-names-each-parts-placement-node) names
which one, so this needs no name matching. Take that node's world matrix and transform the part by
it. Skip this and every wheel, door and bumper piles up at the model origin. Roughly half of retail
parts have a non-identity placement, so the error is obvious on vehicles and invisible on a
single-part prop. Sibling parts frequently share one vertex buffer and place it differently, so
placement has to be applied per part rather than per buffer.

**A part `DIKS` gives no node sits in the root's space.** That covers every skinned mesh — their
vertices already sit in the skeleton root's bind space — and rigid parts already modelled in place.
The root is at a character's waist, around z = +1.0, so using the wrong one sinks a character to the
knees through the floor.

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

:::info[Verified via reverse engineering]
Read out of `LoadGeomResource` (`0x097fd440`) and `CGeometryResource::ClientProcessRawData`
(`0x097fb3f0`), and measured across every cluster in the retail set.
:::

This was the open edge of the format. Neither Gabor nor fdx4061 had it as of April 2026 ("I don't
know how bone palettes work... that's why I still can't create a character-type xbg" — fdx4061).
The chain is:

**cluster palette slot → `EDON` node → that node's `skinIndex` → `MB2O` inverse-bind matrix.**

Each 110-byte cluster inside `SULC` ends with 48 `int16` palette slots. A slot holds an **index into
the file's `EDON` node array**, not a bone id from any external file. Every `EDON` node carries an
`i32` at `+0x38`: `-1` when the node is not a skinning bone, otherwise its index into the `MB2O`
array of inverse-bind matrices. Across the 78 skinned files those indices form a clean permutation of
`0 … MB2O count − 1`, and the `MB2O` count equals the file's skinned-node count in every one.

Every palette slot in the retail set resolves to a node whose `skinIndex` is not `-1` — 3,953 of
3,953 non-empty palettes.

Padding rules, measured over all 32,170 clusters:

| | Count |
|---|---|
| Static cluster (`fmtFlags & 0x0010` clear) — all 48 slots `-1` | 28,217 / 28,217 |
| Skinned cluster — at least one bone | 3,953 / 3,953 |
| Skinned palette is a contiguous prefix of indices, then `-1` padding | 3,953 / 3,953 |
| Skinned palette repeats a bone index within the prefix | 3,841 / 3,953 |

**The rule that a skinned palette must never contain `-1` is wrong** — every skinned palette in the
game is `-1`-padded, and duplicate indices inside the prefix are normal. The engine's only use of the
palette at load is the final loop of `LoadGeomResource`, which scans all 48 slots and sets a single
"has any bone" flag if any slot is not `-1`. The constraint a writer must actually hold is narrower:
every slot a vertex's `BLENDINDICES` refers to has to be non-`-1`.

Animation binds by **name**, not by index: `CGraphicComponent::BuildSkeleton` (`0x094ac0b0`) walks
the nodes into `CSkeletonBuilder::BeginAddBone(nameId, matrix)`, where `nameId` is the CRC32 of the
node's exact-case name. An `.xbg` node and a [`.skeleton`](./skeleton.md) bone are the same bone when
those hashes match, which is why a replacement model must keep part and bone names byte-identical.
See [`.mab`](./mab.md) for how a clip then addresses those bones by skeleton bone id.

## A container can be authored, not just edited

:::info[Verified against the retail corpus]
`tools/BlenderFC2/tests/originate.py` rebuilds each shipped mesh from its decoded content and
requires the bytes back: **3,133 of 3,133 byte-identical**.
:::

Editing a container in place preserves whatever was not understood, which is why every exporter so
far has done that — and why none could add a part or an LOD. Originating one instead requires
knowing which fields carry information and which are bookkeeping. Almost all of them are bookkeeping:

| Field | Derived from |
| --- | --- |
| every chunk size, payload size and sub-chunk count | the body written under it |
| every chunk's `word0` | constant `1`, on all ten mandatory chunks and on `SULC` |
| `header_words[1]`, `[2]`, `[4]` | constant `0` |
| `header_words[3]` | the file size, less the 12 bytes that precede it |
| `DOL\0` | the LOD count, then a constant `98` |
| node `nameId`, `first child`, `next sibling`, `skinIndex` | the name, and node order |
| node `+0x3C` | constant `1.0` |
| `DIKS` entries and their order | one per part, in part order, keyed by the part's name |
| part `reserved` | constant `0` |
| part LOD tier | the `_LOD<n>` suffix on the part's own name |
| `cluster.stride`, `cluster.flags` | the buffer the cluster draws from |
| `cluster.face_count`, `vertex_count` | the geometry |
| `VertexBuffer` third word | that buffer's own vertex count |
| `VertexBuffer` fourth word, `submesh.index_offset` | the running layout |
| `submesh` trailing words | see below |

**A submesh's three trailing words are `[last vertex index, byte offset, 0]`** — the index is
`start + vertex_count - 1` within its buffer, and the offset is `buffer.offset + start * stride`,
absolute in the LOD's whole vertex block rather than relative to the buffer.

The two readings differ only when a LOD has a second buffer, and just six LODs across
`1_grassjungle_b.xbg` and `1_grassjungle_c.xbg` do. The relative reading matches 3,131 of 3,133
files, so those two meshes are the entire evidence that distinguishes them.

Two fields carry information nothing else supplies, so a writer has to keep them:

- **`header_words[0]`**, a per-file value that is not a CRC32 of the file name, the stem in any
  casing, or the body.
- **`LTMR`'s trailing word**, zero in 3,114 files and 1 to 3 on nineteen grass meshes. It equals the
  material count on 17 of those 19, which is a coincidence rather than a rule — it is zero on every
  other mesh, and those have materials too.

Everything else a writer needs — node transforms, part and model bounds, the quantisation scales,
vertex format flags, bone palettes, material paths and LOD distances — is content rather than
bookkeeping, and a decoded model holds it already.

## Authoring ceilings

:::info[Verified against the retail corpus]
Measured over all 3,133 shipped `.xbg` files: 32,170 clusters and 10,462 LODs.
:::

Indices are `u16` throughout, and a cluster stores its own counts in `u16` as well, which puts three
hard ceilings on anything written. Retail sits just under all three — the art pipeline was clearly
built against them:

| Ceiling | Limit | Highest shipped |
| --- | --- | --- |
| Triangles in one cluster (`face_count * 3` is a `u16`) | 21,845 | **21,351** — `bargearmsbazard_multi` |
| Vertices addressed by one LOD's buffer | 65,535 | **56,961** — `merc_kit` LOD0 |
| `cluster.vertex_count` | 65,535 | **29,965** — `bridge_end_multi` |

The triangle ceiling is the one a donated mesh hits first, and it is per *cluster*, not per part or
per LOD — a part with three clusters can draw 65,535 triangles between them. The vertex ceiling is
per buffer, and 10,456 of 10,462 LODs use exactly one buffer, so in practice it is per LOD.

**No shipped cluster draws nothing.** 0 of 32,170 have a zero face count, so a submesh left empty is
a shape the engine is never asked to handle. A writer that cannot fill a cluster should give it a
degenerate or sub-millimetre triangle rather than a zero count.

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
