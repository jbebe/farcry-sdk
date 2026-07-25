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

## The "children chunk" — now confirmed (previously an open question)

Immediately after the parents array, three more length-prefixed arrays follow — a struct-of-arrays,
each with its **own independent `u32` count prefix** (not a single shared count, even though in every
valid file all three ultimately hold one entry per flattened child):

```
u32       childHashCount  (M_A)
M_A × u32 childHash[]         — CPathID hash (CRC32) of the child (dependency) resource's path

u32       childFlagCount  (M_B)
M_B × u8  childFlag[]         — one byte per child; meaning not yet determined (see below)

u32       childTypeCount  (M_C)
M_C × u32 childTypeHash[]     — a second CRC32 per child; meaning not yet determined (see below)
```

A parent's children are `childHash[childIndex .. childIndex+childCount)` (and the parallel slices of
`childFlag`/`childTypeHash`) — i.e. `childIndex` is a slice start into these three parallel arrays,
not a byte offset, confirmed directly from the load loop: it indexes `childHash` as
`childHash[parent.childIndex + i]` while iterating `i` from `0` to `parent.childCount`.

**Not yet determined**: what `childFlag` (the per-child byte) and `childTypeHash` (the per-child
second CRC32) actually mean semantically. What *is* confirmed is that neither is a small interned
lookup table — both arrays are exactly as long as `childHash`, one entry per child, not per distinct
value. (A superficially similar-looking dedup/interning step does happen in `LoadBinaryFile`, folding
repeated `childTypeHash` values into a compact runtime table with a per-child byte index — but this
happens only to the in-memory structure *after* the file is fully read; it is not reflected in the
file's own bytes, so it doesn't change the layout above.) Plausible guesses — `childTypeHash` as a
resource-type CRC (e.g. "this dependency is an `.xbg`" vs "an `.fcb`") and `childFlag` as some
load-priority or optional/required bit — are unconfirmed; would need either a struct definition
recovered from the type that consumes `CResourceDataBase`'s parsed arrays, or empirical correlation
against known resource types across several real `depload.dat` files.

## Concrete gotcha for anyone hand-editing this file

fdx4061 reported **breaking in-game animations** by getting the sort order wrong while merging two
`depload.dat` files by hand. Anyone inserting or removing entries must re-sort the whole parents
array by CRC32 afterward (and keep `childIndex`/`childCount` consistent with wherever the
corresponding slice ends up in the three children arrays), or the file will load but animations will
misbehave — not a hard crash, so the corruption is easy to miss until playtesting.

## Open questions

- Semantic meaning of `childFlag` (u8) and `childTypeHash` (u32) — see above.
- The `_depload.xml` fallback path exists in the loader (`CWorldDescriptorImpl::LoadDep`'s
  `CFileManager::FileOpen` failure branch parses `<name>_depload.xml` via `XmlParser::parse`
  instead) but no real example of this XML form was located in this pass — worth a follow-up if one
  turns up, to confirm it's a straightforward tag-per-field mirror of the binary layout above.
