---
sidebar_position: 18
---

# `.skeleton` — Rigs

:::info[Verified via reverse engineering]
The layout below is read out of `CSkeletonResource::SerializeSkeleton` (`0x09ba0840`) and the
`SerializeBone` (`0x09b9ff80`) and `SerializeAnimHandle` (`0x09b9fe70`) it calls, in the
symbol-bearing `FarCry2_server` binary. A reader written from it consumes all **81** shipped
`.skeleton` files exactly, and a writer reproduces all 81 **byte for byte**.
:::

`.skeleton` is the rig an animated `.xbg` binds to: a bone hierarchy with local transforms,
constraints, and the sockets weapons attach to. The engine identifies bones by the CRC32 of their
name, which is how an `.xbg` node and a `.skeleton` bone are matched — see
[`.xbm` / `.xbg`](./xbm-xbg.md).

## Inventory

81 files ship, all named `<root bone>_ref.skeleton` and sitting beside the model they rig. Sizes run
from 232 bytes (`characters\_common\singlebone_ref.skeleton`) to 12,428 bytes
(`characters\_common\pelvis_ref.skeleton`).

Every human in the game shares `characters\_common\pelvis_ref.skeleton`. Animals have their own
(`hips_ref` or `pelvis_ref` per species), as do vehicles, animated doors, and 41 weapons.

## Layout

All little-endian. `StringID` is `u32 CRC32` + `u32 length` + that many characters, **not**
NUL-terminated.

Objects are introduced by a tag/version pair, written by the engine's versioned-object transfer:

```
u32  0x3ADE68B1   object tag
u32  version      7 for a skeleton and its bones, 3 for an anim handle
```

### Header

```
+0x00  u32   'LKS\0'
+0x04  u32   file version — 18 in every shipped file
+0x08  u32   0x3ADE68B1
+0x0C  u32   object version — 7
+0x10  u16   bone count
+0x12  u16   common bone count
```

### Bone

Repeated `bone count` times.

```
u32       0x3ADE68B1
u32       object version (7)
f32[4]    m_ChildToParent      local rotation, xyzw
f32[3]    m_LocalOffset        local translation
f32       m_flLength
u16       m_nId                equal to the array index in every shipped file
u16       m_nParentId          0xFFFF on the root
u16       m_nFirstChildId      0xFFFF when none
u16       m_nNextSiblingId     0xFFFF when none
u8        m_eOriConst          orientation constraint kind, payload below
u8        m_ePosConst          position constraint kind, payload below
StringID  m_name
u8        m_fAnimatedTranslation
u8        m_BodyPart
f32       m_COMWeight
```

Constraint payloads follow their kind byte immediately:

| `m_eOriConst` | Payload |
|---|---|
| 0 | none |
| 1 | `i32` look-at bone, `f32[3]` offset |
| 2 | `i32` bone 1, `f32` weight 1, `i32` bone 2, `f32` weight 2 |
| 3 | `i32` dependent bone, `f32` weight |
| 4 | `i32` damped bone, `f32` weight |

| `m_ePosConst` | Payload |
|---|---|
| 0 | none |
| 1–3 | `i32` scale-to bone, `f32[3]` offset |

Versions below 7 write a longer bone carrying derived transforms (`WorldToLocal`, `LocalToWorld`,
`ParentToChild`, `Center`), per-axis rotation limits and a mirror id. No shipped file uses it.

### Remainder

```
u16[common bone count]   m_rgCommonBoneIds        0xFFFF marks an empty slot
u16                      anim handle count
                         anim handles, below
f32                      m_flScaledFactor
u16                      translation bone count
u16[...]                 m_rgTranslationBoneIds
3 x (u32 5, u32[5])      LOD bone bitmasks
```

The LOD bitmasks are stored **zeroed** in every shipped file; `CSkeleton::FillLODBitmask` regenerates
all three after load.

### Anim handle

Repeated `anim handle count` times. These are attachment sockets, not bones, and their ids continue
past the last bone id.

```
u32       0x3ADE68B1
u32       object version (3)
u16       m_nId
StringID  m_name
StringID  m_parentBoneName
f32[4]    m_ChildToParent
f32[3]    m_LocalOffset
f32[4]    m_ParentToChild
f32[3]    m_LocalOffsetInverted
f32[4]    m_ParentToChild            written a second time
```

## What the human rig contains

`characters\_common\pelvis_ref.skeleton`: **119 bones**, ids equal to array order, 31 common bones.

- **Translation is animated on two bones only** — `Pelvis` (0) and `Camera` (17). Every other bone
  contributes rotation alone.
- **Arm twist bones** (`L/R U/M/D Forearm twist`, `L/R U/D UpperArm twist`) use orientation
  constraint 3, dependent on another bone.
- **Knees and elbows** (`L/R Knee`, `L/R Elbow`) use orientation constraint 2, blending two bones.
- **30 anim handles are the weapon sockets**, ids 119–148. Long guns hang off `Spine2`, sidearms off
  a thigh:

| Handle | Parent bone |
|---|---|
| `ak47`, `fn_fal`, `g3ka4`, `spas12`, `ithaca`, `usas12`, `pkm`, `as50`, `carl_gustaf`, `mortar`, `dart_rifle`, `mp5_sd3` | `Spine2` |
| `deserteagle`, `makarov`, `star_model_p`, `mac10`, `uzi`, `6p9` | `R Thigh` / `L Thigh` |
| `diamondcanister` | `Spine2` |
| `machette_states123` | `L Thigh` |

A weapon rig is much smaller. `weapons\primary\ak47\ak47_ref.skeleton` is 8 bones — `AK47`,
`FRAME`, `CLIP`, `SLIDE`, `FX_FIRE`, `FX_CASING`, `ACCESSORY`, `Weapon_Break` — with no anim handles
and translation animated on ids 0, 2, 3 and 6.

## The bone tree here is not the one in the `.xbg`

A character's `.xbg` carries its own node tree, and for most bones the two agree. On `pelvis_ref`
four do not — the mid-joint helpers:

| Bone | Parent in the `.xbg` | Parent here |
|---|---|---|
| `L Knee` | `Pelvis` | `L Thigh` |
| `R Knee` | `Pelvis` | `R Thigh` |
| `L Elbow` | `Pelvis` | `L UpperArm` |
| `R Elbow` | `Pelvis` | `R UpperArm` |

The engine animates on **this** tree, so a tool that builds its rig from the `.xbg` and then plays a
clip on it leaves each helper hanging off the pelvis. The helper stays by the hip while the leg
swings, and because it deforms the mesh like any other bone, the skin between it and the thigh
stretches into spikes at the knee. Measured on a sprint clip, the worst edge stretch around those
bones falls from **10.5x to 2.5x** once the rig is put on the skeleton's tree — below the 5.3x the
rest of the character reaches in the same clip.

`Pelvis` also differs, harmlessly: the `.xbg` puts a `Root` node above it and the skeleton has none.

## Sixteen bones are solved, not animated

Every bone with `m_eOriConst != 0` is derived by the engine, and no clip keys any of them. On
`pelvis_ref` that is the four helpers above, at `m_eOriConst 2` blending the bones above and below
them at 0.5/0.5, and twelve arm twist bones at `m_eOriConst 3` distributing a joint's roll at
weights 1.0, 0.75, 0.5 and 0.25 along each chain.

The fields are read straight out of `SerializeBone`, but **where the engine evaluates them has not
been traced** — neither `GetJointRotationsAtTime` nor `GenerateOrientationForJoint` touches them, and
the latter falls back to `m_ChildToParent` for any bone a clip does not key. Reading them as
world-space blends and applying that to a rig was tried and measurably made the deformation worse, so
the semantics should be considered open.

## Tooling

`tools/BlenderFC2/fc2fmt/skeleton.py` reads and writes the format; `tools/BlenderFC2/tests/roundtrip.py
skeleton` re-writes all 81 shipped files and requires the bytes back unchanged.
