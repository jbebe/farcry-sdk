---
sidebar_position: 7
---

# Texturing a replaced weapon

:::tip[Built, played, and confirmed in game]
Two weapons in this repo have been through this end to end: `mods/doom-super-shotgun`, which
replaces the DLC1 sawed-off, and `mods/vss-vintorez`, which replaces the Dart Rifle. Every number on
this page is measured off one of them or off the retail set, and says which.
:::

[Replacing an existing weapon](./replacing-a-weapon.md) gets a donated mesh into the game wearing
somebody else's materials. This page is the other half: taking that model's own texture set and
getting it onto the weapon, without repainting whatever you borrowed the mesh from.

It is a smaller job than the mesh and it fails in fewer ways, but the ways it does fail are all
silent. Nothing warns you that a material is shared, that an albedo is too dark to light, or that a
specular floor is sitting under every texel on the gun.

## The shader has no albedo slot

This is the fact the whole page hangs off. `Weapon` and `Generic` build colour out of **two shared,
game-wide tiling detail maps blended by a per-model mask**:

```
layer1 = DiffuseTexture1 * lerp(DiffuseColorBase, DiffuseColor1, mask.b * vertexColour.b)
layer2 = DiffuseTexture2 * DiffuseColor2
albedo = lerp(layer1, layer2, mask.g * vertexColour.g)
```

A weapon's own two `.xbt` are its **damage-state masks**, not its colour. So a donated colour map has
nowhere to go until you rewrite the `.xbm` to give it one. See
[`.xbm`/`.xbg`](../file-formats/xbm-xbg.md) for the full parameter set and where the formula was read
from.

The rewrite is four lines:

| Slot | Set to | Why |
| --- | --- | --- |
| `DiffuseTexture1` | your albedo | layer 1 becomes the weapon's own texture instead of a tiling detail map |
| `DiffuseTiling1` | `1,1` | so it lands on the model's UVs instead of repeating |
| `MaskTexture1`, `MaskTextureBroken` | your control map | both, or a degraded weapon falls back to the old mask |
| `DiffuseColorBase`, `DiffuseColor1` + `Clean`/`Broken` | equal to each other | so no weapon condition and no vertex channel can re-tint it |

Your control map then holds **green at 0** so the second tiling layer never blends, and **blue at 1**
so the tint weight is full. With blue pinned, `DiffuseColor1` stops being a lerp end and becomes a
plain per-material multiplier — which is the knob you set the weapon's overall level with.

**A weapon owns exactly two texture paths.** That is enough for one albedo and one control map, and
not enough for a normal map. Both worked examples leave `NormalTexture1` and `SpecularTexture1`
pointed at the shared tiling maps they came with, which is what every retail weapon does. A donated
normal map has no home without minting a new asset path, and whether an asset missing from a world's
`depload` loads at all has never been tested here.

## Step 1 — find a material you are allowed to own

:::danger[A transplanted mesh inherits its donor's material table]
The mesh moved to a new `.xbg` path. **Its material and texture references did not.** If you built
your model inside a donor's pack, your weapon is drawing through that donor's `.xbm` files — and the
donor is still in the game, drawing through the same ones. Rewriting them retextures both.
:::

The VSS is the worked case. Its `.xbg` sits at the Dart Rifle's path and names seven materials, every
one of them the Dragunov's, because that is the pack the mesh was built in.

You cannot fix this by copying a material, because **nothing in the toolchain creates an `.xbm`**.
What you can do is take a material the *replaced* weapon owned. Your mod already made it unused — you
overwrote the mesh that was its only reader.

Build the reference index and ask:

```
jackall-cli mod restore --game "C:\Games\Far Cry 2"
jackall-cli xref build   --game "C:\Games\Far Cry 2"
jackall-cli fc2model export graphics/weapons/special/dart_rifle/dart_rifle.xbg \
    --game "C:\Games\Far Cry 2" -o dartrifle.fc2model
jackall-cli fc2model inspect dartrifle.fc2model
```

Restore first, so the index describes the vanilla install rather than your own patch. The `role`
column then reads `owned` or `shared` per file, and **`owned` outside the model's own folder means
exactly one file uses it** — see [`.fc2model`](../file-formats/fc2model.md). Without the index that
column is a directory-rule guess and marks a pooled material `shared` even when only this model uses
it.

On the Dart Rifle two came back `owned`:

```
material  owned   GRAPHICS\_MATERIALS\FBOIVIN2-M-2007050148031384.xbm   DART_RIFLE_METAL
material  owned   GRAPHICS\_MATERIALS\FBOIVIN2-M-2007050162241150.xbm   the wood/camo one
```

`jackall-cli xref to <path>` confirms it and shows something better: both are already listed in
`world1_depload.dat` and `world2_depload.dat`, sited against `dart_rifle.xbg`. A material at a path
you invented would not be, and that is the untested ground worth staying off.

**If nothing comes back `owned`, stop and say so.** That is a real gap, not something to work around
by inventing a path.

## Step 2 — repoint the mesh, and append rather than overwrite

A pack carries the mesh as `model/mesh.json`, where `materials` is a plain list of paths and every
cluster holds a `material_index` into it. Repointing is an edit to an array of strings.

**Append your material and move only the body's clusters onto it.** Do not overwrite the entry that
is already there. The reason is specific and easy to miss:

```
FRAME_LOD0    cluster 4   mat 6   15,651 tris     the gun body
ACCESSORY02   cluster 1   mat 6    1,628
CLIP          cluster 0   mat 6      318
SLIDE         cluster 0   mat 6      556
SCOPE_HI      cluster 4   mat 6      144          <- the same material
```

`SCOPE_HI` draws through the body's material. It is the zoomed sight picture, calibrated to a camera
you cannot see, and [it is the only thing on screen while
zoomed](./replacing-a-weapon.md#scope_hi-is-drawn-instead-of-the-rest-of-the-gun-not-on-top-of-it).
Overwrite the entry in place and you repaint 144 triangles of the scope along with the gun. Append,
and the scope keeps what it had.

Two other things that table shows:

- **Only one material draws any of the model.** The other six carry a single sub-millimetre triangle
  each, because `cluster.zero-triangles` is a blocking rule and a transplant fills the clusters it
  cannot supply. So "the donor's seven materials" is not seven materials to reproduce — it is one.
- **The cluster index is not stable between LOD tiers.** `FRAME` is cluster 4 at LOD0 and cluster 1
  at LOD4. Select by material index, not by position.

## Step 3 — convert the maps

Modern models ship **metallic-roughness**; older ones ship specular-glossiness. Neither is what the
engine reads. Roughness is the lossy direction — Blender has no `SpecularPower` and Dunia has no
roughness.

Both weapons were converted by a short numpy script run headless in Blender — a hundred-odd lines
that reads the source maps, box-filters them down, and writes two PNGs. The scripts themselves are
hardcoded to their own weapon and are not in the repo; everything they encode is on this page, and
the rest of this section is what it took to get each step right.

### The albedo needs a band, not a curve

A physically based albedo is far too dark for a shader with no metalness input. The Doom source
measured **0.05–0.12 luma** and read as black plastic once lit.

The fix is to fit it into a band with a floor and a ceiling — `0.13` to `0.52`, which is also what
`tools/BlenderFC2` tells a modeler. Not a gamma lift: a gamma compresses the top, so it burns pale
areas to white under the game's sun while barely moving the darkest metal. And do it **in float,
before compression** — reaching a metal level from 0.05 needs about 8×, and DXT1 gives red five bits,
so multiplying through `DiffuseColor1` afterwards bands it badly.

**Anchor the fit on your own source's percentiles rather than hardcoding the curve.** The two worked
examples are three to six times apart:

| | Doom super shotgun | VSS Vintorez |
| --- | --- | --- |
| source luma p1–p99 | ~0.05–0.12 | **0.124–0.534** |
| saturation, mean | red 1.4–3.7× blue | **0.072**, near neutral |
| what it needs | a large lift | almost none |

The VSS's author had already worked inside the target band. Copying the shotgun's `LIFT = 0.4` and
`DESATURATE = 0.45` would have crushed a 4.3:1 range to 1.4:1 and flattened what little colour there
was. Mapping the source's own p1–p99 onto the band instead is one rule that serves both: it is nearly
identity for a well-authored source and a 5× lift for a dark one.

Apply the scale to all three channels off a luma-derived factor, not to each channel independently,
or a linear remap shifts hue.

### Three smaller traps

- **V orientation.** Blender holds images bottom-up and a PNG stores rows top-down. Read through
  Blender and write through a hand-rolled PNG writer that flips, and the two cancel. Get it wrong and
  every texture is upside down, which no numeric gate can see — look for lettering in a render.
- **Size.** Retail ships nothing over 2048 and everything power-of-two. A 4096 source is halved. The
  pair that ships is a **1024² base with an 11-level chain plus a 2048² single-level `_mip0`**.
- **Ambient occlusion.** glTF packs AO in the same map's red channel. It is tempting and it was
  measured and rejected here: a quarter of the VSS's AO sits at zero, which is what a bake leaves
  *outside* the UV islands, and multiplying that in bleeds black across island edges at low mips.
  Check the distribution before you use it.

## Step 4 — get the level right

The number that decides whether the weapon reads correctly is what the shader hands the lighting:

```
effective albedo = DiffuseTexture1 mean  x  lerp(DiffuseColorBase, DiffuseColor1, mask.b)
```

Measured across the cases that exist:

| Weapon | `DiffuseTexture1` | tint | effective |
| --- | --- | ---: | ---: |
| Dragunov, retail | `metalbrushed_d` tiled 5,5 | 0.208 | **0.092** |
| Sawed-off, retail | `metalbrushed_d` tiled 5,5 | 0.405 | **0.180** |
| Doom super shotgun, shipped and accepted | its own albedo | 1.547 | **0.412** |
| VSS Vintorez, shipped | its own albedo | 0.640 | **0.213** |

Retail runs 0.09 to 0.18. Note what the retail rows are doing: their `DiffuseTexture1` is a bright
generic detail map and `DiffuseColor1` brings it **down**. Under this recipe the texture is the
weapon's own and the tint usually brings it **up** — but not always, and that is the trap.

The doom shotgun shipped at **2.29× its own donor** and was accepted in game. That ratio is the only
figure here validated by playing, so it is the one to anchor on: apply it to *your* donor's level
rather than copying doom's absolute 0.412, which belongs to a bright chrome weapon. For the VSS that
gave 0.092 × 2.29 = 0.211, and a neutral tint of 0.64 against a texture mean of 0.332.

Leave the tint neutral unless the source needs correcting. The texture already carries the author's
hue; the shotgun's cool `1.50, 1.55, 1.65` exists to counteract a source that was 3.7× as red as
blue.

Set `DiffuseColorBase` **equal to** `DiffuseColor1` rather than leaving base at white. With the two
equal the tint is a constant no mask channel and no vertex colour can move — which matters because
`VertexColorEnabled` is 1 on most weapon materials, and with a tint below 1 a white base makes
low-vertex-colour areas *brighter* rather than darker.

## Step 5 — get the specular right

:::danger[`SpecularColorBase` is a floor under every texel]
This is the one that cost the most time on the VSS, and the render gates cannot see it. The gun
looked correct in every offline check and came back from the first playtest reading as **one polished
surface** — stock, receiver and suppressor all equally glossy.
:::

Specular is a separate additive Blinn term, and the pair `SpecularColorBase`/`SpecularColor1` has the
same shape as the diffuse pair:

```
specular = lerp(SpecularColorBase, SpecularColor1, mask.r) * pow(saturate(N.H), SpecularPower)
```

:::note[The `mask.r` weight is inferred, not traced]
The additive Blinn term and its three inputs are read out of the engine's own shaders. That **red**
is what weights the pair is inference from the diffuse pair's shape. What supports it is the retail
data — the Dragunov's `SpecularColorBase` of 0.043 against a bimodal red channel only makes sense if
red is the weight — and the fact that acting on it fixed this weapon in game. Treat the numbers below
as measured and the mechanism as very likely.
:::

`SpecularColorBase` is what a texel gets when red is zero. Set it high and no mask can make anything
matte. Measured:

| | VSS, first attempt | Dragunov, retail | VSS, shipped |
| --- | ---: | ---: | ---: |
| `SpecularColorBase` | 0.600 | **0.043** | 0.043 |
| `SpecularColor1` | 2.000 | 2.000 | 2.000 |
| `SpecularPower` | 8 | **30** | 30 |
| control red p5 / p25 | 0.137 / 0.298 | 0.020 / 0.051 | 0.024 / 0.043 |
| control red p75 / p95 | 0.745 / 0.773 | 0.710 / 0.969 | 0.816 / 0.839 |
| contrast, dullest to brightest | ~2:1 | **~14:1** | ~14:1 |

Two things to copy from that table.

**A working weapon's specular control is bimodal.** Over half the Dragunov's surface sits at
0.02–0.09 — effectively matte — and a quarter sits at 0.71–0.97. It is not a mid-grey map. The first
VSS attempt was a flat band sitting on a floor higher than the Dragunov's value over half its gun,
which is exactly what "polished shoe" looks like.

**`SpecularPower` is lobe width, and low is wide.** Retail runs 2 to 20 with a mode of 8; the
Dragunov's 30 is tight enough to keep a highlight on the edge it belongs to. A wide lobe on a weapon
that is mostly tube spreads one highlight across the whole thing.

### Level the metal band, not the mean

Deriving red from a metallic-roughness source is `gloss = 1 - roughness`, gated down where the
surface is not metal. Two details decide whether it works:

- **A low dielectric gate is what separates wood from steel inside one material.** The VSS's metallic
  map is very nearly binary — p25 of 0.000 and p50 of 0.996 — so a gate of `0.06` puts the stock and
  grip in the same near-zero band the Dragunov puts most of its surface in. A gate of 0.35 leaves
  them a third as shiny as the steel, which reads as uniformly polished.
- **Fit the gamma to the metal texels alone.** Levelling the *mean* to a donor's is the intuitive
  move and it is wrong: the Dragunov's mean is low because more of it is matte, not because its metal
  is dimmer. Levelling the VSS's mean to the Dragunov's 0.347 dimmed its metal to p95 0.627. Fitting
  the metal band to 0.80 instead puts both ends where they belong, and lets a weapon that happens to
  be 57% metal keep a mean higher than the donor's without that being wrong.

## Step 6 — ship it

Do the whole thing through a `.fc2model` pack. Edit `materials/<name>.json` and swap the texture PNGs
inside the zip, then set **`origin_sha256`** on each entry you touched — that field's presence is
exactly what marks an entry modified. Set `levels` to the new chain length (12 for a 2048 image) and
the applier does the rest:

```
jackall-cli fc2model extract edited.fc2model -o mylayer
jackall-cli mod build --game "C:\Games\Far Cry 2" --layer mylayer
```

It re-encodes each PNG in the codec the entry records, generates the mip chain, and **splits it
itself** — level 0 to the `_mip0` companion, the rest to the base file. That removes the whole
`xbt extract` / `xbt build` step and its trap, which is that
[every header byte has to come from a real file](../file-formats/xbt.md#header): `Reserved` is a
bitfield the streaming loader consumes and `Hash` is an id nothing derives, so there is no honest
path from a bare `.dds`.

The applier also refuses to write a `shared` entry that was edited, which is the gate that keeps
step 1 honest.

:::note[There used to be a Python route, and it moved]
Older material-editing scripts imported `fc2fmt`, a Python `.xbm`/`.xbg` library that sat under
`tools/BlenderFC2/`. **That code was ported into JackAll** when the add-on was rebuilt on the pack —
the `.xbm` reader and writer live in `JackAll.Tools` now, which is why the add-on carries no format
code of its own. The capability did not go away; the Python entry point did, and the pack is the
route that replaced it.
:::

Then **read it back out of the patch**, not out of your layer:

```
jackall-cli archive extract "C:\Games\Far Cry 2\Data_Win32\patch.fat" --names --filter <weapon> -o check
```

Both textures should come back as a 1024² base with 11 mips and a 2048² companion with one, in the
same codec.

## What the offline gates cannot see

Render the weapon and compare it against the donor — that rule
[governs the mesh half](./replacing-a-weapon.md#the-rule-that-would-have-saved-this-project-the-most-time)
and it still applies. `render_refs.py`'s `_lit` view is the one that matters here, because it uses
EEVEE with the materials the file carries. It will catch an upside-down albedo, a body reading black,
and a seam.

It will not catch the specular, and that is worth knowing precisely. The add-on's material preview
drives Blender's roughness from `SpecularTexture1` and `SpecularColor1`; it models **neither
`SpecularColorBase` nor the control map's red**. Those are the two inputs that decide whether the gun
reads matte or polished, so between a first attempt and a fixed one the render looks essentially
identical. Only the game shows it.

One more, if your build takes its animation from a different weapon than its mesh path: the sight
render derives its eye from whichever aim bank the pack carries, and a pack collects banks by model
name. The VSS therefore renders its scope from the **Dart Rifle's** `sp389` bank while the game plays
the **Dragunov's** `spdra`. The framing is wrong by however far those two eyes differ.

## Known constraints, collected

- **The `Weapon` shader samples no albedo.** Colour is two tiling maps blended by a mask until you
  rewrite the `.xbm`.
- **A weapon owns two texture paths**, its damage-state masks. One albedo, one control map, no normal
  map.
- **A transplanted mesh keeps its donor's material table.** Take a material the replaced weapon owned
  instead, and check it with `xref` rather than the directory rule.
- **Nothing creates an `.xbm`.** If no material is exclusively yours, that is a wall, not a detour.
- **Append the material and move clusters onto it.** `SCOPE_HI` shares the body's material and must
  keep it.
- **Fit the albedo into a band anchored on the source's own percentiles**, in float, before
  compression.
- **Match the effective albedo to your donor's level**, not to another mod's absolute number.
- **`SpecularColorBase` is a floor under every texel.** A chrome weapon's 0.6 makes everything glossy.
- **A working specular control is bimodal**, and its metal band is what to level — not its mean.
- **`SpecularPower` is lobe width and low is wide.**
- **Set `DiffuseColorBase` equal to `DiffuseColor1`** so no condition or vertex channel re-tints.
- **The `_mip0` companion is twice the base**, and the pack applier splits it for you.
- **`origin_sha256` is what marks a pack entry modified.**
- **No offline gate models the specular path.** That one needs the game.
