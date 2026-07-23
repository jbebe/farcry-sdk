---
sidebar_position: 2
---

# `.fcb` — Binary Object-Tree Format

:::info[Verified via reverse engineering]
Traced live via GhidraMCP against the Steam v1.03 build, to independently confirm (or correct) the
`.fcb` entity/weapon/vehicle/world-sector binary format as reverse-engineered by the community
(Gibbed's `BinaryResourceFile`, `tools/Gibbed.Dunia`) and ported to first-party C# at
`tools/JackAll/src/JackAll.Core/Format/Fcb/FcbDocument.cs` — verifying against the real parser
instead of only trusting a 2021-era community tool. For where this format's data actually lives and
how it's used practically, see [Getting Started](../modding/getting-started.md) and [Data
Recipes](../modding/data-recipes.md).
:::

## Confirmed call chain

`World_LoadEntityLibraryWithOverride` (`0x1065b130`, already named in [the archives
page](./archives-fat-dat.md) as `FUN_1065b130` — not yet renamed in Ghidra itself, only described
there) → resolver vtable+0x4c → `Resource_LoadViaResolver` (`0x102340f0`) → vtable+0x48 →
`FUN_102353b0` (reads the whole file into a malloc'd buffer) → **`Fcb_ReadHeader`** (`0x10235080`) →
**`Fcb_AllocateTree`** (`0x10234fc0`) → **`Fcb_ParseObject`** (`0x10234d60`, recursive) →
**`Fcb_ReadTypeHash`** (`0x10234260`, called once per object).

`Fcb_MagicConstant` (`0x10246200`) and `Fcb_SupportedVersionConstant` (`0x10246210`) are trivial
one-line constant-returning functions (`0x4643626e` "FCbn" and `2` respectively) — renamed for
readability, not because there's any logic worth documenting in them.

## Header — confirmed byte-for-byte in `Fcb_ReadHeader`

```
offset  size  field
0       4     magic (u32) — must equal Fcb_MagicConstant() = 0x4643626E ("FCbn" LE)
4       2     version (u16) — must equal Fcb_SupportedVersionConstant() = 2, no other value accepted
6       2     flags (u16) — only bit 0 is read (masked `AND AL,1`); everything else is ignored
8       4     totalObjectCount (u32)
12      4     totalValueCount (u32)
16      —     root object tree starts here
```

`Fcb_ReadHeader` treats the file as invalid (returns 0/failure) if magic or version don't match, **or
if both `totalObjectCount` and `totalValueCount` are zero** — an all-zero tree is explicitly rejected,
not treated as "empty but valid."

## `Fcb_ParseObject` — the recursive tree walker

One object is: `childCount`-varint, TypeHash, `valueCount`-varint, that many value entries, then
`childCount` child objects. Confirmed directly in the decompiled function body (not just inferred):

- **The `childCount` and `valueCount` varints (this object's own counts) are decoded identically** —
  a marker byte `< 0xFE` is the literal value; `0xFE` or `0xFF` both mean "read the next 4 bytes (LE)
  as the literal value instead." **Neither marker carries backreference meaning at this position** —
  that's a materially different fact from how the community tooling (Gibbed's `BinaryResourceFile`,
  and JackAll's original port before this investigation) treated it. See "Correction to JackAll's
  port" below.
- **Object registration**: right after TypeHash is read, this object's pool address is appended to a
  growing array via a generic, heavily-shared append helper (`FUN_10075b80`, 90+ unrelated callers
  across the engine — left unrenamed, same precedent as `FUN_1057a030`/`FUN_10769180` in [the engine
  overview](../engine-internals/overview.md)). Index = this object's ordinal among everything parsed
  so far, file-order.
- **Value entries**: nameHash (u32) is read and skipped (not copied anywhere by this function); then a
  size-varint: `< 0xFE` → that many payload bytes follow and are skipped inline; `== 0xFF` → an
  explicit 4-byte size follows, then that many payload bytes; **`== 0xFE` → skip exactly 5 bytes total
  (marker + 4-byte value) and never dereference it** — the trailing 4 bytes are a backward byte offset
  to an *earlier* value's own size-varint (i.e. "my bytes are the same as that one's"), but this
  function doesn't resolve it at all; it just knows this value contributes zero fresh payload bytes
  here. (JackAll's port, needing a self-contained in-memory tree rather than lazy on-demand access
  into a retained file buffer, eagerly follows the offset and copies the shared bytes at parse time —
  a different but behaviorally equivalent strategy, not a bug.)
- **Child list**: for each of `childCount` slots, peek (don't yet consume) the next byte. If it's
  exactly `0xFE`: this slot is a backreference — read a 4-byte index into the object-pointer array
  from `Fcb_RegisterParsedObject`'s helper, resolve it, store that as the slot's pointer, advance 5
  bytes, **no recursion**. Any other byte (`< 0xFE` or `== 0xFF`) recurses into `Fcb_ParseObject`,
  which will itself consume that identical leading byte as *its own* `childCount` field (extending via
  4 more bytes if it was `0xFF`). This is the one place `0xFE` is genuinely special, and it's a
  distinct code position from the "this object's own childCount" read above, even though both are
  reached via the same recursive function.
- Object struct written into the pre-allocated pool: 6 dwords (24 bytes) — vtable, a zeroed flag, this
  object's file position, its childCount, and two zeroed trailer fields — followed immediately by
  `childCount` more dwords holding the (possibly-backreferenced) child pointers.

## `Fcb_ReadTypeHash` — the flags-gated alternate encoding

If the header's flags bit 0 is 0 (every real sample seen): TypeHash is just the raw u32 at the cursor,
4 bytes consumed. If set: the leading u32 becomes a **fallback** raw hash (used only if the following
string is too long), then a second u32 gives a string length; if under 512, that many raw bytes follow
as a class-name string, copied to a stack buffer, NUL-terminated, and hashed via `GetNameHash` →
`CRC32_Hash` to produce the real TypeHash (12 bytes + string length consumed instead of 4). This is a
genuinely new detail beyond what Gibbed's tooling or JackAll's original port document — no known
shipped `.fcb` uses it (all 5 real fixtures checked have flags == 0), so `FcbDocument.Deserialize`
deliberately throws rather than silently mishandling a file that needs it.

## `Fcb_AllocateTree` — pool sizing

Allocates `(totalObjectCount * 6 + totalValueCount) * 4` bytes up front. The `*6` term is exactly the
24-byte fixed object struct above; the `totalValueCount` term, per `Fcb_ParseObject`'s actual pool
writes, is consumed **only** as each object's trailing child-pointer-slot array (`childCount` dwords
per object) — never as storage for primitive value bytes (there is none; see above, values are read
from the retained raw file buffer, not copied into this pool). This is the strongest lead so far on
what the header's `totalValueCount` field really counts, and it's surprising: at the pool-usage level
it behaves like "total child-link slots across the tree," not "total named-field values" in the sense
Gibbed's tooling (and the community at large) uses the term.

**Not conclusively resolved**: whether `totalValueCount` is *literally* a child-slot count by the
original format's own design, or whether it happens to equal that quantity in every sample checked so
far for some other reason. Cross-checked against 5 real shipped files (`patch_entitylibrary.fcb`,
`patch_entitylibrarypatchoverride.fcb`, `worlds_entitylibrary.fcb`, `dlc1_entitylibrary.fcb`,
`dlc_jungle_entitylibrary.fcb`, all in `tools/JackAll/src/JackAll.Core.Tests/Fixtures/Fcb/`):
`totalObjectCount` matches JackAll's own unique-object count (by reference identity, after resolving
backreferences) **exactly** in all 5 files — strong, direct confirmation the object-backreference
handling is correct. `totalValueCount` does not match a naive "value slots across the unique object
graph" tally in any of them (consistently ~3x lower than that tally) — consistent with it counting
something narrower than "every named field," but not independently proven beyond the pool-usage
argument above. This has no bearing on whether JackAll's reader/writer is correct — nothing in
`FcbDocument.cs` depends on this field's precise meaning, it's written on output purely for structural
completeness.

## Correction to JackAll's port made from this investigation

Before this session, `FcbDocument.Deserialize` threw if an object's own `valueCount` field's marker
byte was `0xFE`, treating it the same as the (genuinely different) object-level child-list
backreference marker. Confirmed above: the engine never does this — `0xFE` and `0xFF` are equivalent
"read 4 more bytes" markers for an object's own `childCount`/`valueCount` fields, with no
backreference meaning at that position at all. None of the 5 real fixtures happen to trigger this
(no shipped file was seen encoding a plain count via `0xFE` instead of the equivalent `0xFF`), so this
was a latent, never-triggered bug — fixed regardless, with a synthetic regression test
(`JackAll.Core.Tests/FcbDocumentTests.cs`,
`An_objects_own_value_count_never_means_backreference_even_with_marker_0xFE`) since real data can't
exercise it.

## Not yet traced

- The exact original semantics of `totalValueCount` (see above) — would need either a real sample
  with a nonzero `totalValueCount`-to-childslot mismatch to falsify the current hypothesis, or the
  original offline compiler's source/binary (not present in this DLL — `.fcb` compilation happens in
  an external build tool, not in the shipped game).
- A real `.fcb` sample with flags bit 0 set (the string-hashed TypeHash path) — none seen yet, so
  `Fcb_ReadTypeHash`'s alternate branch is understood from static analysis only, not cross-checked
  against real data the way the rest of this format has been.
