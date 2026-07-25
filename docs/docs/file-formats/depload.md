---
sidebar_position: 9
---

# `depload.dat` — Dependency/"Parents" Chunk

:::info[Verified via reverse engineering]
Originally logged as community-reported (Discord, Far Cry Modding Community, `🔩-tools-talking`,
2026-04-09 — a from-scratch black-box exchange between **fdx4061** and **ArmanIII**, quoted below).
Independently confirmed and extended by disassembly on 2026-07-25, traced live via GhidraMCP against
a separate, more heavily-symbolized FC2 binary (the same "server build" with real mangled C++
symbols already used for the [DLC entity-library merge trace](./archives-fat-dat.md) — distinct from
the retail `Dunia.dll` this site otherwise documents). See [Getting Started](../modding/getting-started.md)
for how RE-verified and community-reported claims are distinguished across this site.
:::

Not covered anywhere else in this project's research prior to the original community pass, and not
supported by FCBConverter — fdx4061 was writing a from-scratch parser specifically because of this
gap: *"I making my own script for it because fcb converter doesn't support fc2 depload.dat."*
ArmanIII's own framing of the difficulty: *"main difference in FC2 is that it uses CRC32 even for
file paths"* / *"I never explored FC2 binary files, so have no idea how exactly depload works in
older dunia."*

## How it's found and loaded (traced from `CXGame::LoadDepLoad`)

`CXGame::LoadDepLoad` (`0x08888d50`) is the real load entry point:

1. Calls `CWorldDescriptorImpl::LoadDep()` (`0x09c2b0c0`) — loads the **current world's** own
   `<world>/generated/<world>_depload.dat` (format string `"%s%s_depload.dat"` at `0x0a158cb0`, and
   its sibling `"%s%s_deploadnewparticles.rml"` right next to it — a second, RML-format dependency
   file for particle effects specifically, out of scope here). If the binary `.dat` isn't found, it
   falls back to a same-named `_depload.xml` (a plain `XmlParser::parse` path — evidently the format
   also has/had a human-editable XML form, though no shipped example was located in this pass).
2. Then, separately, walks every installed DLC via `CDlcService::GetDepLoads()` and loads each one's
   own `depload.dat` the same way, with `isPrimary = false` (see below).

Both the primary and DLC loads bottom out in the same two-line forwarder,
`CResourceManager::LoadDep(IFile*, bool isPrimary)` (`0x09c07f50`) →
**`CResourceDataBase::LoadBinaryFile(IFile*, bool isPrimary)`** (`0x09c594c0`) — this last function is
the actual binary reader, and its read sequence (traced call-by-call from the raw `IFile::Read`
virtual calls) is the ground truth for the layout below. `isPrimary` (`true` for the current world's
own file, `false` for a DLC's) only changes *how loaded records are merged into the in-memory
resource database* (pre-sized in-place vs. read-then-deduplicated-and-merged) — it has no effect on
the file's own byte layout, which is identical either way.

## Confirmed structure

A **file-level header**, then a **parents array**:

```
offset  size  field
0       4     parent count N (u32)
4       8*N   N × parent entries (see below)
```

Each parent entry is exactly the 3 fields the community thread found, read as two `u16` fields
followed by one `u32` — confirmed field-for-field against the three separate `IFile::Read` calls
(`2`, `2`, then `4` bytes) `LoadBinaryFile` issues per entry:

```
offset  size  field
0       2     childIndex — index (not byte offset) into the flattened child arrays below
2       2     childCount — how many consecutive entries starting at childIndex belong to this parent
4       4     parent CRC32 (CPathID hash of the parent resource's path)
```

**Entries are sorted ascending by CRC32, treated as an unsigned 32-bit integer** — confirmed twice
over: the community thread's empirical finding (*"order is mostly done by integer, so sort CRC32 as
int... crc32 is unsigned int32"* — ArmanIII; *"I look at it again and seems that crcs are really
sorted from low to high"* — fdx4061), and now directly in the disassembly, which binary-searches this
array by CRC32 on load (`LoadBinaryFile`'s embedded `while (0 < iVar16) { ... puVar19[1] < local_24 ...}`
loop) — a binary search only works correctly if the array is actually kept sorted, so the engine
itself depends on this invariant, not just tooling built to read the file.

## The "children chunk" — now confirmed against real shipped files

Immediately after the parents array, three more length-prefixed arrays follow, each with its **own
independent `u32` count prefix**:

```
u32       childHashCount    (M_A)
M_A × u32 childHash[]           — CPathID hash (CRC32) of the child (dependency) resource's path

u32       childTypeIndexCount (M_B, == M_A)
M_B × u8  childTypeIndex[]      — one byte per child: an index into the type table below

u32       typeTableCount    (M_C, independent — much smaller than M_A/M_B in practice)
M_C × u32 typeHash[]            — a small deduplicated table of distinct type CRC32s
```

A parent's children are `childHash[childIndex .. childIndex+childCount)` (and the parallel slice of
`childTypeIndex`) — i.e. `childIndex` is a slice start into the two per-child arrays, not a byte
offset, confirmed directly from the load loop: it indexes `childHash` as
`childHash[parent.childIndex + i]` while iterating `i` from `0` to `parent.childCount`. Each child's
actual type hash is `typeHash[childTypeIndex[i]]`.

**Correction from an earlier pass at this format**: this was first written up (from the disassembly
alone, before a real sample was available to check against) as *three* parallel per-child arrays —
`childHash`, a per-child flag byte, and a per-child second CRC32, all the same length. Decoding a real
shipped `entitylibrary_depload.dat` (433 parents, 1314 children) and a real
`worlds/tmpla/generated/tmpla_depload.dat` (9134 parents, 25838 children) immediately falsified that:
in both files the third array's own count-prefix is nowhere near the child count (8 and a comparably
small number respectively), and every observed "flag" byte fell inside `[0, thirdArrayCount)` —
conclusive for "the third array is a small deduplicated type-hash table indexed by the second array,"
not a third per-child field. (`LoadBinaryFile` *also* does its own runtime interning of type hashes
into an in-memory table with a per-child byte index, after the file is read — a second, unrelated
dedup pass over already-parsed data, at a different granularity, likely to merge multiple loaded
depload files' type tables into one shared runtime table. It happens to look structurally similar to
the file's own interned table, which is almost certainly why that first pass conflated the two.)

**Still not yet determined**: the semantic meaning of the resolved `typeHash` itself (e.g. "this
dependency is a texture" vs "a mesh") — only that it's now confirmed to be a per-resource-*type*
value shared by many children, not a per-child one. Would need either a struct definition recovered
from the type that consumes `CResourceDataBase`'s parsed arrays, or empirical correlation against
known resource types across several real `depload.dat` files.

## Concrete gotcha for anyone hand-editing this file

fdx4061 reported **breaking in-game animations** by getting the sort order wrong while merging two
`depload.dat` files by hand. Anyone inserting or removing entries must re-sort the whole parents
array by CRC32 afterward (and keep `childIndex`/`childCount` consistent with wherever the
corresponding slice ends up in the three children arrays), or the file will load but animations will
misbehave — not a hard crash, so the corruption is easy to miss until playtesting.

## Open questions

- Semantic meaning of the resolved per-child `typeHash` (u32) — see above.
- The `_depload.xml` fallback path exists in the loader (`CWorldDescriptorImpl::LoadDep`'s
  `CFileManager::FileOpen` failure branch parses `<name>_depload.xml` via `XmlParser::parse`
  instead) but no real example of this XML form was located in this pass — worth a follow-up if one
  turns up, to confirm it's a straightforward tag-per-field mirror of the binary layout above.
