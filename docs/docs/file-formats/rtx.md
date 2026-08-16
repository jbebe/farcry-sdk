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

`.rtx` names a species, not a location. How a level decides where its vegetation stands is a separate
and currently unresolved question — retail campaign levels carry no authored placement data, only a
palette of collection definitions. See
[Retail campaign levels carry no authored collection data](../engine-internals/terrain-and-vegetation.md#retail-campaign-levels-carry-no-authored-collection-data).
