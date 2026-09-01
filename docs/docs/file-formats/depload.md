---
sidebar_position: 3
---

# `depload.dat` — Dependency Chunk

:::info[Verified via reverse engineering, corrects an earlier community write-up]
Originally documented community-side (Discord, Far Cry Modding Community, 2026-04-09 — a from-scratch
black-box exchange between **fdx4061** and **ArmanIII**; not supported by FCBConverter, hence the
from-scratch approach: *"fcb converter doesn't support fc2 depload.dat"*). Independently confirmed and
extended by disassembly, traced live via GhidraMCP against the same more heavily-symbolized
`FarCry2_server` binary used for the [DLC entity-library merge](./archives-fat-dat.md). See
[intro](../intro.md) for how RE-verified and community-reported claims are distinguished on this site.
:::

`depload.dat` records, per world, which resources a resource depends on (its "parents"/"children") —
a **prefetch manifest**, used by the engine to warm what a resource will need and, evidently, to tie
animation data to what plays it.

:::note[It does not gate loading]
An asset absent from every `depload` still resolves and renders when something asks for it — verified
with a texture staged into `patch.dat` at a path present in no shipped archive, no hashlist and no
`depload` list, reached through a material that *is* listed. Absence costs streaming warmth, not
availability. Untested for a mesh or a sound at an unlisted path.
:::

## How it's loaded

`CXGame::LoadDepLoad` (`0x08888d50`) is the entry point:

1. Calls `CWorldDescriptorImpl::LoadDep()` (`0x09c2b0c0`), which loads the current world's own
   `<world>/generated/<world>_depload.dat` (format string `"%s%s_depload.dat"`), and its sibling
   `"%s%s_deploadnewparticles.rml"` — a second, RML-format dependency file for particle effects. RML
   is a separate container and a solved one: `jackall-cli rml decode` / `rml encode` round-trip
   `oasisstrings.rml` byte-identically at 946 KB. Its *contents* are out of scope here. If the binary
   `.dat` isn't found, it falls back to a same-named `_depload.xml` (a plain `XmlParser::parse` path —
   `worlds/tmpla/` ships source XML twins beside its binaries, so confirm whether the fallback
   consumes that same shape before relying on it).
2. Then walks every installed DLC via `CDlcService::GetDepLoads()` and loads each one's own
   `depload.dat` the same way, with `isPrimary = false`.

Both paths bottom out in `CResourceManager::LoadDep(IFile*, bool isPrimary)` (`0x09c07f50`) →
**`CResourceDataBase::LoadBinaryFile(IFile*, bool isPrimary)`** (`0x09c594c0`), the actual binary
reader — its `IFile::Read` call sequence is the ground truth for the layout below. `isPrimary` only
changes how loaded records merge into the in-memory resource database; it has no effect on the file's
own byte layout.

## Structure

A file-level header, then a parents array:

```
offset  size  field
0       4     parent count N (u32)
4       8*N   N × parent entries
```

Each parent entry — confirmed field-for-field against the three separate `IFile::Read` calls
(`2`, `2`, then `4` bytes) `LoadBinaryFile` issues per entry:

```
offset  size  field
0       2     childIndex — index (not byte offset) into the flattened child arrays below
2       2     childCount — how many consecutive entries starting at childIndex belong to this parent
4       4     parent CRC32 (CPathID hash of the parent resource's path)
```

**Entries are sorted ascending by CRC32, treated as unsigned 32-bit.** Confirmed both empirically
(community finding) and now directly in the disassembly, which binary-searches this array by CRC32 on
load — a binary search only works if the array is actually kept sorted, so the engine itself depends on
this invariant, not just tooling built to read the file.

## The children chunk

Immediately after the parents array, three more length-prefixed arrays follow, each with its own
independent u32 count:

```
u32       childHashCount    (M_A)
M_A × u32 childHash[]           — CPathID hash (CRC32) of the child (dependency) resource's path

u32       childTypeIndexCount (M_B, == M_A)
M_B × u8  childTypeIndex[]      — one byte per child: an index into the type table below

u32       typeTableCount    (M_C, independent — much smaller than M_A/M_B in practice)
M_C × u32 typeHash[]            — a small deduplicated table of distinct type CRC32s
```

A parent's children are `childHash[childIndex .. childIndex+childCount)` (and the parallel slice of
`childTypeIndex`) — `childIndex` is a slice start into the two per-child arrays, confirmed directly from
the load loop. Each child's actual type hash is `typeHash[childTypeIndex[i]]`.

**Correction from an earlier pass at this format**: this was first written up, from disassembly alone
before a real sample was available, as *three* parallel per-child arrays (hash, flag byte, second
CRC32), all the same length. Decoding two real shipped files (`entitylibrary_depload.dat`: 433 parents,
1,314 children; `worlds/tmpla/generated/tmpla_depload.dat`: 9,134 parents, 25,838 children) falsified
that: the third array's count-prefix is nowhere near the child count in either file, and every observed
"flag" byte fell inside `[0, thirdArrayCount)` — conclusive for "the third array is a small
deduplicated type-hash table indexed by the second array," not a third per-child field.
(`LoadBinaryFile` also does its own runtime interning of type hashes into an in-memory table with a
per-child byte index after the file is read — a second, unrelated dedup pass over already-parsed data,
structurally similar to the file's own table, which is almost certainly why the first pass conflated
the two.)

The semantic meaning of the resolved `typeHash` itself (e.g. "this dependency is a texture" vs. "a
mesh") is confirmed to be a per-resource-*type* value shared by many children, but not resolved further
— would need either a struct definition recovered from whatever consumes `CResourceDataBase`'s parsed
arrays, or empirical correlation against known resource types across several real files.

## Hand-editing gotcha

fdx4061 reported breaking in-game animations by getting the sort order wrong while merging two
`depload.dat` files by hand. Anyone inserting or removing entries must re-sort the whole parents array
by CRC32 afterward (and keep `childIndex`/`childCount` consistent with wherever the corresponding slice
ends up in the children arrays), or the file loads but animations misbehave — not a hard crash, so the
corruption is easy to miss until playtesting.

## Unknowns

- Semantic meaning of the resolved per-child `typeHash` (see above).
- The `_depload.xml` fallback path exists in the loader but no real example was located — worth
  confirming it's a straightforward tag-per-field mirror of the binary layout above, if one turns up.
