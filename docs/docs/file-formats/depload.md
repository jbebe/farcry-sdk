---
sidebar_position: 9
---

# `depload.dat` — Dependency/"Parents" Chunk

:::note[Community-reported]
Source: Discord, Far Cry Modding Community, `🔩-tools-talking`, 2026-04-09 — a live, from-scratch
reverse-engineering exchange between **fdx4061** (writing a standalone parser, since FCBConverter
doesn't support this file for FC2) and **ArmanIII** (FCBConverter's author, who hadn't personally
explored FC2 binaries but corroborated/assisted). Not yet independently verified by disassembly
against `Dunia.dll` — see [Getting Started](../modding/getting-started.md) for how RE-verified and
community-reported claims are distinguished across this site.
:::

Not covered anywhere else in this project's research (community or RE) prior to this pass, and not
supported by FCBConverter — fdx4061 is writing a from-scratch parser specifically because of this
gap: *"I making my own script for it because fcb converter doesn't support fc2 depload.dat."*
ArmanIII's own framing of the difficulty: *"main difference in FC2 is that it uses CRC32 even for
file paths"* / *"I never explored FC2 binary files, so have no idea how exactly depload works in
older dunia."*

## Confirmed structure (derived empirically, in-thread)

A **parents array**, starting at file offset `4`, with 3 fields per element:

```
offset  size  field
0       2     offset to the children chunk
2       2     child count
4       4     parent CRC32
```

(*"Parents array contain 3 parts for each element: 2b offset for children chunk, 2b childs count and
4b parent crc. Starts from offset 4."* — ArmanIII)

**Entries are sorted ascending by CRC32, treated as an unsigned 32-bit integer** (*"order is mostly
done by integer, so sort CRC32 as int... crc32 is unsigned int32"* — ArmanIII; *"I look at it again
and seems that crcs are really sorted from low to high, thank you"* — fdx4061, confirmed empirically
against real data).

## Concrete gotcha for anyone hand-editing this file

fdx4061 reported **breaking in-game animations** by getting the sort order wrong while merging two
`depload.dat` files by hand. Anyone inserting or removing entries must re-sort the whole parents
array by CRC32 afterward, or the file will load but animations will misbehave — not a hard crash, so
the corruption is easy to miss until playtesting.

## Open questions

- The children chunk's own byte layout (what the 2-byte offset in each parent entry actually points
  to) wasn't discussed in this exchange — only the parents array itself.
- Not yet cross-checked against `Dunia.dll` by disassembly — everything above is community-derived
  from black-box testing, not decompilation.
