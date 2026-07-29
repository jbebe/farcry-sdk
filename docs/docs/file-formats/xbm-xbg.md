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
