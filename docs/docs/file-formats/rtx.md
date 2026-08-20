---
sidebar_position: 16
---

# `.rtx` — Realtree Vegetation

:::caution[Partially decoded]
Only the container framing below has been read from real files. The payload — the branch hierarchy,
simulation data and leaf state that the `RTxc*` classes imply — is not decoded.
:::

`.rtx` is the asset format behind Far Cry 2's vegetation: the trees and large plants that sway, burn,
shed branches and lose leaves. It is a simulation asset rather than a static mesh; see
[`RTxcManager`](../engine-internals/architecture.md#rtxcmanager--rtx-is-a-live-vegetation-simulation-not-a-static-mesh)
for the runtime class taxonomy, which names the parts to expect inside.

## Inventory

96 `.rtx` files ship, all under `graphics\vegetation\<biome>\realtrees\`, and they live in
**`worlds.fat`** rather than `common.fat`. Observed sizes run from roughly 100 KB to 145 KB — far
larger than the `.xbg` meshes in the same folders, which is consistent with a simulation asset.

The same folders also carry `.xbg` (54) and `.hkx` (16), so a species can have an ordinary mesh and a
collision hull alongside its realtree asset.

## Container framing

The file opens with a 24-byte header followed by an asset path string:

```
+0x00  u32   size of section A
+0x04  u32   size of section B (0 when absent)
+0x08  u32   0x88 (136) — constant across samples
+0x0C  u32   flag (observed 0 and 1)
+0x10  u32   0
+0x14  u32   an offset into section A, a few hundred bytes short of its end
+0x18  char[] asset path, e.g. graphics\Vegetation\Desert\Realtrees\HY_Aloes_01
```

The two section sizes account for the whole file: `sizeA + sizeB == fileSize - 8`. A file with
`sizeB == 0` is therefore a single section. Both observed values of the flag at `+0x0C` pair with a
different `sizeB`, so the flag plausibly records whether the second section is present.

The embedded path is the asset's own name. In one sample it ends in **`.rta`** — an authoring
extension that does **not** ship in any archive, so it is a reference to the source asset rather than
to another game file.

Beyond the header the payload is dense binary with no readable chunk tags; the only other ASCII found
is a repeated decimal string (`180809718`), which looks like a build or revision stamp.

## Placement

`.rtx` names a species, not a location. Placement is resolved: it lives in the per-sector landmark
files, under a `CCollectionComponent`'s `VegetationZoneData`, and a `.rtx` is referenced there the
same way a mesh is — by the CRC32 of its own path. See
[It lives in the landmark files](../engine-internals/terrain-and-vegetation.md#it-lives-in-the-landmark-files)
and [Resource ids are path hashes](../engine-internals/terrain-and-vegetation.md#resource-ids-are-path-hashes-and-most-of-the-scatter-is-grass).

RealTree is the smaller half of that scatter: 60 distinct `.rtx` resources against roughly 101,000
placed instances in `world1`, about 4% of the total. The other 96% resolves to ordinary `.xbg`.

By instance count that share is small, but by *species* it is nearly everything: sampling six landmark
files from each `world1` cell turns up 88 distinct resources, and the ones that read as vegetation are
almost all `.rtx` — `rt_tree_acacia`, `rt_tree_ficus_a`/`_b`, `rt_tree_camphor`, `rt_saguaro_cactus_line_*`,
`rt_bush_tamarix_*`, `hy_aloes_*`, `hy_banana_big`. The `.xbg` share is dominated by grass and by
`terrain\rocks\desert\ter_desertrock*`. **The trees are the part that has no parser.**

The payload holds no references out to other assets: scanning `rt_tree_acacia.rtx` for the CRC32 of
every one of the 16,645 paths under `graphics\` finds zero matches, and the only readable string is
the `.rta` source path in the header. So the species' textures and materials cannot be recovered from
it without decoding the payload proper.

## Impostor cards

Six meshes under `graphics\vegetation\jungle\realtrees\donotuse\` are camera-facing impostor cards:
`facingbush`, `facing_bush_large`, `facing_bush_palm`, `facingbush_savannah2`,
`facing_bush_large_savannah`, `facing_bush_savannah`. Despite the folder name the scatter does place
them — `facingbush.xbg` and both `facing_bush_large`/`_palm` show up in the `world1` sample above.

Each is 12 vertices: a single quad (four triangles, normals along **−Y**) plus two ground-facing
triangles, spanning roughly 16 m wide and up to 15 m tall.

They are also the only meshes in the game that ask to be turned toward the viewer, and they say so in
their material rather than their name. Exactly **two of the 2,208 shipped `.xbm` materials** carry a
`Billboard` property — `dboivin-m-2008041150042062` and `smaingot-m-2008042358745658` — and between
them they cover those six meshes and nothing else. A renderer that ignores it draws a tree card
edge-on.
