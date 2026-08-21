---
sidebar_position: 19
---

# `.mab` — Animation Banks

:::info[Verified via reverse engineering]
The layout below is read out of `EvaluateSingleAnimNode` (`0x09bba350`), `GetJointRotationsAtTime`
(`0x09bb7fc0`), `GetQuaternionAtTime` (`0x09bb8a50`), `GetXYZDiff` (`0x09bbc8a0`),
`GetOrientationAtTime` (`0x09b77540`), `GetRootRotation` (`0x09bb9410`),
`CAnimData::GetEventAtTime` (`0x09b9efe0`), `CAnimationResource::ClientProcessRawData`
(`0x09b9e8d0`), `AnimDataHeader::AnimDataHeader` (`0x09b9f150`) and
`CMoveDefParameter::GetAnimData` (`0x09b7d480`) in the symbol-bearing `FarCry2_server` binary,
and checked against all **4,436** shipped files and the **11,261** clips inside them. The keyframe
group layout also comes from `CAnimSparseRotationChunkWalker::sg_magicTable` (`0x0a1f2140`).
:::

A `.mab` is a **bank**, not a single clip. It holds one clip per skeleton taking part in the
animation — the character, then the weapon in their hands, then whatever else moves — each chained
from the one before. `1stge_uppb_reload_+000fw_prak4_i1.mab` carries both the arms that reload and
the AK-47's own magazine and slide.

Each clip stores bone rotations and offsets over time, the trajectory the actor is carried along,
and the events the clip fires. It is **not** a Havok packfile — Havok 5.5.0 ships in the binary but
serves ragdoll and skeleton mapping, not this format.

Bones are addressed by their **`.skeleton` bone id**, so a clip is bound to the rig it was authored
for and carries no bone names of its own. See [`.skeleton`](./skeleton.md).

## Inventory

4,436 files ship, from 640 bytes to 1,032 KB, holding 11,261 clips between them. 854 banks hold a
single clip; most hold two or three, and the longest chain is 35 — the DLC weapon crate, which
animates every gun in it.

Character clips live under `graphics\characters\_common\animations\<category>\`, grouped by weapon,
vehicle, locomotion state, communication, and choreographed scene. The first clip in each targets
`characters\_common\pelvis_ref.skeleton`.

Filenames encode the clip: `1stge` / `3rdge` for first- and third-person, `fulb` / `uppb` for full
and upper body, then the action, a facing, and the weapon code — for example
`3rdge_uppb_runnoneupperbody_+000fw_prak47_i1.mab`.

## Layout

A 16-byte file header precedes the first clip. A clip is the `CAnimData` the engine hands out, and
every offset in it is relative to its own base — so the first clip's base is **file + 0x10**, and a
chained clip's base is wherever section 8 points.

### File header

```
+0x00  u16   version — 0x4C in every shipped file
+0x02  u16   1, 2 or 3
+0x04  u32   hash
+0x08  8 bytes
```

### Clip

```
+0x00  u32[5]   bitmask of bones holding a CONSTANT rotation
+0x14  u32[5]   bitmask of bones with KEYFRAMED rotation
+0x28  u32[5]   bitmask of bones holding a CONSTANT translation
+0x3C  u32[5]   bitmask of bones with an ANIMATED translation
+0x50  f32[4]   the orientation the trajectory is expressed relative to
+0x60  f32[4]   the rotation one full playthrough of the clip adds
+0x70  u32      'AnD\x1a' signature — present in all 4,436 files
+0x74  f32      clip duration in seconds
+0x78  i32[9]   section offset table, relative to the clip base; 0 means unused
+0x9C  ptr      the engine writes the clip's own address here; zero on disk
+0xA0            section data begins here in every shipped clip
```

Rotation and translation are masked **independently**: a bone can be keyed for one and constant for
the other. Resolving every rotation-mask bit in every character clip against `pelvis_ref.skeleton`
yields **303,067 bits, none out of range**, and every one of the 19,458 constant translations and
5,692 animated translation tracks lands on `Pelvis` or `Camera` — the exact two bones that skeleton
marks `m_fAnimatedTranslation`.

**A bone's slot inside a section is the popcount of its mask below that bone's id.** The engine
computes exactly this, and across all 11,261 clips it always equals the bone's ordinal in the mask.

The two quaternions at `+0x50` and `+0x60` serve looping: `GetXYZDiff` reads the first to decide
which way the trajectory delta points, and `EvaluateSingleAnimNode` multiplies the second in once per
completed loop.

### Section table

Nine entries. Offsets are **not** stored in ascending order — the trajectory rotation comes first in
the file — so a section's extent is found from the next larger offset, not the next table entry.

| Index | Clip offset | Contents | Clips |
|---|---|---|---|
| 0 | `+0x78` | trajectory translation, one track | 4,436 |
| 1 | `+0x7C` | trajectory rotation, one track | 4,436 |
| 2 | `+0x80` | constant rotations | 11,261 |
| 3 | `+0x84` | keyframed rotations — see [The keyframe block](#the-keyframe-block) | 11,261 |
| 4 | `+0x88` | constant translations | 11,261 |
| 5 | `+0x8C` | animated translations | 11,261 |
| 6 | `+0x90` | tag table | 3,582 |
| 7 | `+0x94` | event chain | 2,669 |
| 8 | `+0x98` | the next skeleton's clip | 11,261 |

Sections 0 and 1 exist on the **first clip only**: the trajectory belongs to the bank, not to each
participant. Section 8's offset is always set but points at the end of the data when the chain ends.

Every array section opens with the same eight bytes:

```
+0x00  u16   entry count — the popcount of the mask that sizes the section
+0x02  u16   last frame index
+0x04  u16   frames per second; the engine picks a frame with time * this
+0x06  u16   0 in every shipped clip
+0x08        entries
```

30 fps covers most clips; 15, 31, 32, 33 and 34 also occur.

### Rotations and translations

- **Constant** sections hold one entry per bone in their mask, in ascending bone-id order: a
  6-byte packed quaternion for section 2, three `f32` for section 4. The `last frame` field is
  ignored.
- **Animated translations** (section 5) are stored **frame-major and dense** — all tracks for frame
  0, then all tracks for frame 1, and so on, `12 × trackCount` bytes per frame, with the engine
  interpolating linearly between adjacent frames. There is no sparse encoding here; only rotations
  get one.
- **Trajectory** sections 0 and 1 use the same frame-major layout with one track, `12` and `6` bytes
  per frame. `GetXYZDiff` reads section 0 at two times and returns the difference, which is how a
  clip moves the actor through the world; `GetOrientationAtTime` slerps section 1 for the heading,
  falling back to bone 0's own rotation when section 1 is absent.

Sizing every one of these sections from its header and mask, the entries always end inside the
section, with only alignment padding to spare.

### Tag table and event chain

Section 6 is `u32 count` followed by fixed 172-byte records. Only part of a record is traced:

```
+0x00  u8    kind — 0, 1, 6, 7, 8 and 9 occur
+0x02  s16   id
+0x0C  i32   offset to an embedded clip, relative to the record; 0 when there is none
+0x10  f32   start time
+0x14  f32   end time
+0x18  i32   matched against the anim node's own parameter
+0xA8  ptr   the engine writes the record's address here; zero on disk
```

`EvaluateSingleAnimNode` scans this table for a record whose kind and id match the node and whose
time range contains the current time, and can take joint rotations or the whole trajectory from the
clip embedded at `+0x0C` instead of from the clip that owns the table. The remaining 140 bytes are
not decoded.

Section 7 is the event chain proper — what `CAnimData::GetEventAtTime`, `GetTimeOfEvent`,
`GetNearestEvent` and `TriggerGameEvents` read. Nodes are FCB binary instantiated through
`CDynamicTypeFactory`:

```
+0x00  f32   time
+0x04  u32   type; 2 means the payload is a CReadOnlyBinaryNode to instantiate
+0x08  u16   size
+0x0A  u16   offset to the next node, 0 at the end
+0x0C        payload
```

:::warning[The community labels are shifted by one]
The third-party Blender importer calls table entry 6 `Events`. The engine reads events from entry
**7**; entry 6 is the tag table above. Its other labels (`UnkSec1`..`UnkSec5`, `Offsets`) are the
five sections named in this table.
:::

An exporter quirk worth knowing when writing a parser that walks the file linearly rather than
through the table: **the tag array is always emitted, even when empty**, and then the table slot is
left at zero. That leaves 16 zero bytes after the animated translations in the 7,679 clips with no
tags, and nothing points at them.

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

## The keyframe block

Rotations — and only rotations — are stored sparsely, in groups of eight frames. After the shared
eight-byte header comes one offset per group, each relative to the block's own start:

```
+0x08  i32[groupCount]  group offsets; groupCount is (last frame >> 3) + 1
```

Each group holds three runs, every one of them ordered by ascending bone id:

```
trackCount x 6 bytes   the rotation at the group's first frame, one per track
trackCount bytes       a presence byte per track, padded up to an even count
                       then, per track in the same order:
popcount(b & 0x7F) x 6 bytes   the rotations for the subframes that byte names
```

**Bit `i` of a presence byte means a key at subframe `i + 1`.** Bit 7 is the group's own first
frame, which is always present and already stored in the first run — the engine forces that bit on
with `| 0x80` before counting, so its value in the file is irrelevant.

The engine never counts these bits directly. It indexes `sg_magicTable`, a 256-byte table whose low
three bits are `popcount(x) - 1`; the walk advances a track's key pointer by
`sg_magicTable[presence | 0x80] & 7` quaternions. The upper bits pick a slerp scale for interpolating
between two keys, which a tool converting to its own keyframes does not need.

Walking every shipped clip this way, the groups tile their block exactly: **3,880 clips** with
keyframes, **63,579 groups** and **14,930,196 keys**, with no group over- or under-running the next.
The only slack is 2 to 14 zero bytes after the final group, which is the block being padded to a
16-byte boundary.

The header cross-checks itself: `duration * rate` never exceeds `last frame + 1` in any shipped clip,
which is required because the engine indexes the group table with `time * rate`.

## Reading a weapon's animation

The clip a bank holds for a given rig is the first one in the chain whose bone ids all fit that
skeleton. A character clip addresses ids up to 118, so an 8-bone weapon rig skips past it and lands
on its own clip; the character rig matches the first clip and stops there. Of the 468 banks filed
under a weapon, 467 chain a clip that fits that weapon's `_ref.skeleton`.

Loading `1stge_uppb_reload_+000fw_prak4_i1.mab` twice — once against `pelvis_ref.skeleton` and once
against `ak47_ref.skeleton` — poses 53 character bones from the first clip and all 8 AK-47 bones
from the second, with `AK47`, `CLIP`, `SLIDE` and `ACCESSORY` carrying the translations, which is
exactly the four ids `ak47_ref.skeleton` marks translation-animated.

## What is still open

- **Writing clips.** Reading is complete enough to round-trip; nothing yet builds a keyframe block
  from scratch.
- **140 of the 172 bytes in a tag record**, and the FCB payload schema of an event node.

## Tooling

`tools/BlenderFC2/fc2fmt/mab.py` reads and writes the container, walks the clip chain, and decodes
the four bitmasks, both constant arrays, the sparse rotation keyframes, the dense translation tracks
and the trajectory — 97% of the bytes in the retail set, with the tag and event blocks the remainder.
`tools/BlenderFC2/tests/roundtrip.py mab` re-writes all 4,436 shipped files and requires the bytes
back unchanged; `tests/invariants.py` sizes every array section in all 11,261 clips from its own
header and mask and requires the entries to end inside it; `tests/mabcheck.py` decodes every key in
the character clips and checks each rotation is unit length, arrives in frame order, and that no
translation lands on a bone the skeleton holds fixed.

`tools/BlenderFC2/addon/import_mab.py` turns a clip into a Blender Action. Because a clip stores a
bone's transform relative to its parent — replacing the rest transform rather than adding to it — the
pose bone carries `rest⁻¹ · clip`, and the armature has to be built with each bone oriented like its
`.xbg` node rather than aimed at its children. A pose bone's location is measured in its own rest
frame, so the offset is rotated into it before being keyed. `tests/blender_anim.py` checks the result
the other way round: it evaluates the posed rig and reads each bone's rotation and offset relative to
its parent back out, requiring what the file stores (worst `4.4e-07` on rotation and `2.4e-07` metres
on offset across four clips, character and weapon).
