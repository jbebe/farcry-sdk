---
sidebar_position: 25
---

# Bullet tracers

:::info[Verified via reverse engineering]
Traced in `FarCry2_server` (symbols) and mapped into `Dunia.dll` (Steam v1.03). Addresses without a
binary named are Steam `Dunia.dll`. Not yet seen in a running game.
:::

## The weapon's settings

Each weapon's `WeaponProperties` carries a `BulletTracer` object, read by
`CWeaponFireBulletProperties` (server `RegisterProperties` `0x08F9F6F0`, constructor `0x100FA620`).

| Field | Offset in the properties | Retail |
|---|---|---|
| `texTexture` | `+0x4D8` | `graphics\gfx\weapons\bullettracer_d.xbt`, or none on launchers |
| `texTextureDistortion` | `+0x4DC` | none on every weapon |
| `fSpeed` | `+0x4E0` | 400 |
| `fLength` | `+0x4E4` | 10 |
| `fLengthDistortion` | `+0x4E8` | 50 |
| `fWidth` | `+0x4EC` | 0.02; 0.03 on the M249, PKM and sniper rifles; 0.04 on the M2 |
| `fOffset` | `+0x4F0` | 0.5, 0 on the Dragunov, M1903 and Dart Rifle, 9 on the AS50 |
| `iFrequency` | `+0x4F4` | 1 to 5: every round on the sniper rifles, every 3rd on most guns |

The values are the same in every retail entity library. `CWeaponFireBulletStrategy::CacheFXData`
(`0x1010FAF0`) loads the two textures into the fire strategy at `+0x80` and `+0x88`.

## Which shots leave one

`CWeaponFireBulletStrategy::ApplyDelayBullet` (`0x10115680`, server `0x08F5A3E0`) is the only
caller of the tracer manager. With the strategy in `EDI`, its tracer block (`0x10116510`) goes on
only while all of these hold:

1. **The shooter is not drawn in first person.** Bit `0x80` of the byte at `+4` of the shooter's
   effective data, the one [the first-person camera sets](./first-person-aiming.md#the-first-person-body),
   is tested at `0x10116515`. So the player's own shots never leave a tracer; other people's do.
2. A countdown at the strategy's `+0x168` is not zero, and either texture is set.
3. The countdown, taken down by one, reaches zero. It then starts again from `iFrequency`, which
   `SetProperties` (`0x101109B0`) also loads it with. A tracer is every `iFrequency`th round of that
   weapon, with no chance in it, and `iFrequency` 0 means none.
4. The weapon has a muzzle bone, its index at the strategy's `+0x54`.
5. The shot's end is more than `fOffset` from the muzzle.

The tracer starts `fOffset` along the muzzle bone's Y axis and ends where the shot hit, or at the end
of its ray. The weapon is at the strategy's `+0x40`, and its properties at `+0x50`.

## Drawing

`CBulletTracerManager` keeps them, through the global at `0x10FE3C24`:

| Function | Address |
|---|---|
| `AddTrace(start, end, speed, length, lengthDistortion, width, &texture, &textureDistortion)` | `0x10102530` |
| `Update` | `0x101022D0` |
| `AdvanceTrace` | `0x10101540` |
| `BuildTrace` | `0x10101960` |
| `FreeTrace` | `0x10102170` |

A trace is a 0x78-byte record of its own, in a list that grows as needed, with no limit. Its head
moves at `fSpeed` and stops at the end once less than `fLength` is left; it is freed when its time,
the distance over the speed, runs out.

`BuildTrace` draws each as two quads turned to face the camera, `fWidth` wide:

- **The streak**, `fLength` long behind the head, in `texTexture`. Its blend is additive (source and
  destination both one), and its vertex colour white. The texture's U runs along the streak and V
  across it; the retail art is bright at U 1, which suggests that end leads.
- **A heat haze**, up to `fLengthDistortion` behind the head, in `texTextureDistortion`, drawn as a
  distortion. No retail weapon has the texture, so it is never drawn.

There is no light. `PackColor` (`0x1045B810`) clamps each channel to one, so the vertex colour can
only tint or darken the texture. The streak goes into world pass `0x22`, which runs before the
bloom; that order is read from `CSceneRenderer_ExecuteViewPasses` (`0x10342360`), not measured.

The retail `bullettracer_d.xbt` is a 32×8 DXT1 lit on 2 of its 8 rows, a dim grey about 23% bright
with one near-white texel at the leading end. Each smaller mip level averages those two rows with the
dark ones, so a far tracer is dimmer still.
