---
sidebar_position: 2
---

# `.fcb` — Binary Object-Tree Format

:::info[Verified via reverse engineering]
Traced live via GhidraMCP against the Steam v1.03 build, to independently confirm (and in one case
correct) the `.fcb` format as reverse-engineered by the community (Gibbed's `BinaryResourceFile`) and
ported to first-party C# at `tools/JackAll/src/JackAll.Core/Format/Fcb/FcbDocument.cs`. For where this
format's data lives and how it's used practically, see [Getting
Started](../modding/getting-started.md) and [Data Recipes](../modding/data-recipes.md).
:::

`.fcb` is the engine's generic serialized-object-tree format: a header, a flat pool of objects, and a
recursive tree of typed values. It's used for entity libraries, weapon/vehicle definitions, world-sector
placement data, and — as documented on the [savegame page](./savegame.md) — the entire embedded
`PersistenceDB` dump inside a `.sav` file.

## Confirmed call chain

`World_LoadEntityLibraryWithOverride` (`0x1065b130`, see [archives](./archives-fat-dat.md)) → resolver
vtable+0x4c → `Resource_LoadViaResolver` (`0x102340f0`) → vtable+0x48 → `FUN_102353b0` (reads the whole
file into a malloc'd buffer) → **`Fcb_ReadHeader`** (`0x10235080`) → **`Fcb_AllocateTree`**
(`0x10234fc0`) → **`Fcb_ParseObject`** (`0x10234d60`, recursive) → **`Fcb_ReadTypeHash`**
(`0x10234260`, once per object).

## Header

```
offset  size  field
0       4     magic (u32) — must equal 0x4643626E ("FCbn" LE)
4       2     version (u16) — must equal 2, no other value accepted
6       2     flags (u16) — only bit 0 is read; everything else ignored
8       4     totalObjectCount (u32)
12      4     totalValueCount (u32)
16      —     root object tree starts here
```

`Fcb_ReadHeader` rejects the file (returns failure) if magic or version don't match, or if both counts
are zero — an all-zero tree is explicitly invalid, not "empty but valid."

## `Fcb_ParseObject` — the recursive tree walker

One object is: `childCount`-varint, TypeHash, `valueCount`-varint, that many value entries, then
`childCount` child objects.

- **Varints** (`childCount` and `valueCount`, this object's own counts): a marker byte `< 0xFE` is the
  literal value; `0xFE` or `0xFF` both mean "read the next 4 bytes (LE) as the literal value instead."
  **Neither marker carries backreference meaning at this position** — this differs materially from how
  Gibbed's tooling, and JackAll's original port, treated it (see "Correction" below).
- **Object registration**: right after TypeHash is read, the object's pool address is appended to a
  growing array. Index = its ordinal among everything parsed so far, in file order.
- **Value entries**: nameHash (u32), then a size-varint: `< 0xFE` → that many payload bytes follow;
  `== 0xFF` → an explicit 4-byte size, then that many bytes; **`== 0xFE`** → skip exactly 5 bytes total
  and never dereference — the trailing 4 bytes are a backward byte offset to an *earlier* value's own
  size-varint ("my bytes are the same as that one's"). JackAll's port, needing a self-contained
  in-memory tree, eagerly resolves and copies the shared bytes at parse time instead — a different but
  behaviorally equivalent strategy.
- **Child list**: for each of `childCount` slots, peek the next byte. If exactly `0xFE`: a
  backreference — read a 4-byte index into the object-pointer array, resolve it, store the pointer,
  advance 5 bytes, no recursion. Any other byte recurses into `Fcb_ParseObject`, which consumes that
  same leading byte as its own `childCount` field. This is the one place `0xFE` genuinely means
  backreference, and it's a distinct code position from the object's own `childCount`/`valueCount`
  reads above.
- Each object struct in the pre-allocated pool: 6 dwords (24 bytes) — vtable, a zeroed flag, file
  position, `childCount`, two zeroed trailer fields — followed by `childCount` more dwords holding the
  (possibly backreferenced) child pointers.

## `Fcb_ReadTypeHash` — the flags-gated alternate encoding

If the header's flags bit 0 is 0 (every real sample seen): TypeHash is the raw u32 at the cursor, 4
bytes consumed. If set: the leading u32 becomes a fallback raw hash (used only if the string is too
long), a second u32 gives a string length, and if under 512, that many raw bytes follow as a class-name
string, NUL-terminated and hashed via `GetNameHash` → `CRC32_Hash` to produce the real TypeHash (12
bytes + string length consumed instead of 4). No known shipped `.fcb` uses this path — all 5 real
fixtures checked have flags == 0 — so `FcbDocument.Deserialize` deliberately throws rather than
mishandling a file that needs it.

## `Fcb_AllocateTree` — pool sizing

Allocates `(totalObjectCount * 6 + totalValueCount) * 4` bytes up front. The `*6` term is the 24-byte
fixed object struct; the `totalValueCount` term is consumed only as each object's trailing
child-pointer-slot array (`childCount` dwords per object) — never as storage for primitive value bytes
(values are read from the retained raw file buffer, not copied into this pool). This is the strongest
lead on what `totalValueCount` counts: at the pool-usage level it behaves like "total child-link slots
across the tree," not "total named-field values" the way the community's tooling uses the term.

Cross-checked against 5 real shipped files (`patch_entitylibrary.fcb`,
`patch_entitylibrarypatchoverride.fcb`, `worlds_entitylibrary.fcb`, `dlc1_entitylibrary.fcb`,
`dlc_jungle_entitylibrary.fcb`): `totalObjectCount` matches JackAll's own unique-object count exactly in
all 5 — strong confirmation the backreference handling is correct. `totalValueCount` does not match a
naive "value slots across the unique object graph" tally in any of them (consistently ~3x lower) —
consistent with it counting something narrower than "every named field," though not independently
proven beyond the pool-usage argument. This has no bearing on correctness — nothing in `FcbDocument.cs`
depends on this field's precise meaning; it's written on output purely for structural completeness.

## Correction to JackAll's port

Before this investigation, `FcbDocument.Deserialize` threw if an object's own `valueCount` marker byte
was `0xFE`, treating it the same as the (genuinely different) object-level child-list backreference
marker. The engine never does this — `0xFE` and `0xFF` are equivalent "read 4 more bytes" markers for
an object's own `childCount`/`valueCount` fields, with no backreference meaning at that position. No
real fixture happened to trigger this, so it was a latent, never-triggered bug — fixed regardless, with
a synthetic regression test (`FcbDocumentTests.cs`,
`An_objects_own_value_count_never_means_backreference_even_with_marker_0xFE`).

## Unknowns

- The exact original semantics of `totalValueCount` — would need either a real sample with a nonzero
  count-to-childslot mismatch to falsify the current hypothesis, or the original offline compiler
  (`.fcb` compilation happens in an external build tool, not the shipped game).
- A real `.fcb` sample with flags bit 0 set (the string-hashed TypeHash path) — none seen yet, so
  `Fcb_ReadTypeHash`'s alternate branch is understood from static analysis only.
