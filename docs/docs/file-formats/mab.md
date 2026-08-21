---
sidebar_position: 19
---

# `.mab` — Animation Banks

:::info[Verified via reverse engineering]
The header, bone bitmasks, section table and quaternion codec below are read out of
`GetQuaternionAtTime` (`0x09bb8a50`), `AnimDataHeader::AnimDataHeader` (`0x09b9f150`),
`CMoveDefParameter::GetAnimData` (`0x09b7d480`) and `CAnimationResource::ClientProcessRawData`
(`0x09b9e8d0`) in the symbol-bearing `FarCry2_server` binary, and checked against all **4,436**
shipped `.mab` files. The per-track packing inside a keyframe group is **not** decoded.
:::

`.mab` holds one animation clip: a set of bone rotations over time, plus the events the clip fires.
It is **not** a Havok packfile — Havok 5.5.0 ships in the binary but serves ragdoll and skeleton
mapping, not this format.

Bones are addressed by their **`.skeleton` bone id**, so a clip is bound to the rig it was authored
for and carries no bone names of its own. See [`.skeleton`](./skeleton.md).

## Inventory

4,436 files ship, from 640 bytes to 1,032 KB. Character clips live under
`graphics\characters\_common\animations\<category>\`, grouped by weapon, vehicle, locomotion state,
communication, and choreographed scene. All of them target
`characters\_common\pelvis_ref.skeleton`.

Filenames encode the clip: `1stge` / `3rdge` for first- and third-person, `fulb` / `uppb` for full
and upper body, then the action, a facing, and the weapon code — for example
`3rdge_uppb_runnoneupperbody_+000fw_prak47_i1.mab`.

## Layout

A 16-byte file header precedes the **body**, which is the `CAnimData` the engine hands out and what
every offset below is relative to. Body base is therefore **file + 0x10**.

### File header

```
+0x00  u16   version — 0x4C in every shipped file
+0x02  u16   1, 2 or 3
+0x04  u32   hash
+0x08  8 bytes
```

### Body

```
+0x00  u32[5]   bitmask of bones holding a CONSTANT rotation
+0x14  u32[5]   bitmask of bones with KEYFRAMED rotation
+0x28  72 bytes  not decoded
+0x70  u32      'AnD\x1a' signature — present in all 4,436 files
+0x74  f32      clip duration in seconds
+0x78  i32[10]  section offset table, relative to the body base; 0 means unused
+0xA0            section data begins here in every shipped file
```

Both bitmasks are indexed by `.skeleton` bone id. Resolving every mask bit in every character clip
against `pelvis_ref.skeleton` yields **303,067 bits, none out of range**.

**A bone's slot inside a section is the popcount of its mask below that bone's id.** The engine
computes exactly this to index the constant-rotation array.

### Section table

Ten entries. The engine dereferences three of them by name; the rest are not identified.

| Index | Body offset | Contents |
|---|---|---|
| 2 | `+0x80` | constant rotations — `u16` count, entries from `+8`, one packed quaternion each |
| 3 | `+0x84` | keyframes — `u16` quantised count, `u16` frame count, `u32` track count, then per-group offsets |
| 7 | `+0x94` | event node chain |

Offsets are **not** stored in ascending order, so a section's extent is found from the next larger
offset, not the next table entry.

Event nodes are FCB binary instantiated through `CDynamicTypeFactory`:

```
+0x00  u32
+0x04  u32   type
+0x08  u16   size
+0x0A  u16   offset to the next node
+0x0C        payload, a CReadOnlyBinaryNode
```

## Quaternion codec

Rotations are packed into 6 bytes as smallest-three: the three smallest components are stored in 16
bits each and the largest is recovered from the norm.

```
a = (word0 & 0x7FFF) * 4.315969e-05 - 0.70710677
b = (word1 & 0x7FFF) * 4.315969e-05 - 0.70710677
c =  word2           * 4.315969e-05 - 0.70710677     word2 is SIGNED and uses its full range
d = sqrt(1 - a² - b² - c²)
```

The two sign bits select where the recovered component `d` goes:

| `word0` bit 15 | `word1` bit 15 | Result, xyzw |
|---|---|---|
| 0 | 0 | `d a b c` |
| 0 | 1 | `a b d c` |
| 1 | 0 | `a d b c` |
| 1 | 1 | `a b c d` |

The scale and bias put each stored component in ±1/√2, which is the range the smallest three of a
unit quaternion occupy. Decoding all 174,820 constant rotations in the character clips gives a worst
`|norm − 1|` of `1.1e-16`.

Unit norm cannot distinguish one component permutation from another, so the table above was
confirmed a second way: a bone a clip holds constant should sit at or near its rest rotation
(`m_ChildToParent`). Scored against the rest pose over 31,383 samples, this mapping gives mean
`|dot| = 0.977`; the nearest alternative gives 0.858 and the rest give 0.04.

## What is still open

- **The per-track packing inside a keyframe group.** The group table and the block boundaries are
  readable; splitting a group into per-bone tracks is not solved, so a clip cannot yet be imported
  as keyframes.
- **Translation tracks.** `m_fAnimatedTranslation` is set on `Pelvis` and `Camera` only, and the
  path that reads their translations has not been traced.
- **Section table entries 0, 1, 4, 5, 6, 8 and 9**, and body `+0x28`–`+0x6F`.

## Tooling

`tools/BlenderFC2/fc2fmt/mab.py` reads and writes the container, decodes both bitmasks and the
constant rotations, and preserves everything else byte for byte;
`tools/BlenderFC2/tests/roundtrip.py mab` re-writes all 4,436 shipped files and requires the bytes
back unchanged.
