---
sidebar_position: 22
---

# First-person aiming

:::info[Verified via reverse engineering]
Traced in `FarCry2_server` (symbols) and mapped into `Dunia.dll` (Steam v1.03). Addresses without a
binary named are Steam `Dunia.dll`. FCSE's address library maps every one of them to GOG.
:::

:::warning[Not yet tested in a running game]
Whether moving the eye this way moves it relative to the gun, rather than the gun along with it, is
the first thing Weapon Overhaul's sway shows.
:::

## Down the sights

The pawn's effective data holds one flag for it: bit 3 (`0x08`) of the byte at `+4`. `CPawn::
GetEffectiveData` (`0x1007E1C0`) is `*(pawn + 0x10) + 0x2D0`.

`CPawnBeautifier::UpdateEffectiveIronSight` (server `0x09008E80`) copies the flag over from the
desired data only while all of these hold: the pawn's state machine is in the `AllowIronsight` group,
the primary track is equipped, the weapon is not jammed, the pawn is not jumping, it is under 0.5 m
from the ground, and the weapon's `bCanIronsight` is set. When the flag changes, it sends the weapon
the `IRONSIGHT_ON` or `IRONSIGHT_OFF` event.

`CPlayerBiofeedback::UpdateIronsightBreathing` (server `0x08740120`) is sound only: a mixing preset
while down the sights, and the breath sounds after `fTimeInIronsightBeforeBreathing`. Nothing moves.

## The first-person camera

`CCameraPawnComponent` is the camera entity attached to the player's pawn. Its `Update`
(`0x10693C00`) runs, in order:

1. **`UpdateDebugOffset`** (`0x10693A00`) sets the camera entity's local position: the weapon's
   `CameraOffset`, or `CameraOffsetIronsight` down the sights, plus the component's `DebugOffset`.
2. **`UpdateCameraOffset`** (`0x10693490`, `__thiscall(float seconds, CPawn* pawn)`) fills an
   angular offset and a positional offset from the pawn's effective data. That is the recoil kick: a
   new kick is blended back to zero over its time. Camera shake angles are added on top every frame.
3. The scene camera's orientation is the look angle times the angular offset. Its position is the
   entity's position plus the positional offset rotated by that orientation, so the positional
   offset is in the view's own axes: x right, y ahead, z up.

| Field | Steam `Dunia.dll` | server |
|---|---|---|
| `DebugOffset` | `+0xC4` | `+0x9C` |
| angular offset (radians) | `+0xD0` | `+0xA8` |
| positional offset (metres) | `+0xE8` | `+0xC0` |

`Update` is entered through the component's second base, at `this + 4`, and calls both helpers on
`this`. The offsets above are from `this`, which is also what the accessor below returns.

Most branches of `UpdateCameraOffset` rewrite the positional offset every frame. The one that is
blending an angular kick back leaves it alone, so anything added to it has to be taken out again
before the next call.

### From the console

The retail Lua bindings `Game:SetFPCameraOffsetX/Y/Z` (`0x1070C2E0` for X) set one axis of
`DebugOffset`. `Game:SetWeaponCameraOffsetX/Y/Z` set one axis of the equipped weapon's
`CameraOffset`, or of its `CameraOffsetIronsight` while down the sights. Both go through `0x1070C210`, which returns the player's camera component. They
are reachable with the console's `#` escape, for example `#Game:SetFPCameraOffsetX(0.01)` (see
[the developer console](./developer-console.md#--the-lua-escape)).

## A scope's sight picture

`CFCXWeapon::ShowHiResScope(bool)` (`0x106D3B80`) swaps the weapon's `SCOPE_HI` part in and every
other part out. It keeps its current state at `+0x84`, and `bUseHiResScope` at `+0x85` decides whether
it does anything at all.

`CFCXWeapon` derives from `CWeapon` at offset 0, so the equipped weapon is the object to read those
two bytes from. `CInventoryViewPawn::GetEquippedWeapon` (`0x10127DA0`, `__thiscall`) returns it, and
the pawn's inventory view is `*(pawn + 0x10) + 0x4F0`.

What that means for a weapon's mesh is in
[replacing a weapon](../modding/replacing-a-weapon.md#e-scope_hi-is-drawn-instead-of-the-rest-of-the-gun).

## Where a first-person shot goes

`CWeaponFireBulletStrategy::SetupShot1st` (server `0x08F50BC0`) starts the shot at the pawn's body
part 0 and points it along that part's rotation, not the weapon's. It then bends the direction by
`CNoiseMaker::GetNoise` (server `0x09008420`), in degrees. That noise is scaled up while the pawn
moves, by sustained fire and by the player's accuracy bonus.

`GetActualSpreadData` (server `0x08F4D690`) picks which noise: the jump one while in the air, the
iron-sight ones once the ironsight flag is set and the pawn's skill value at `+0x14` has reached
0.75, and the crouched or standing one by stance.

`CNoiseMaker` is a Catmull-Rom curve through Gaussian keys of standard deviation `fAmplitude`, a new
key every `1 / fFrequency` seconds of game time. The retail `BulletSpread_IronSight` is 0.4° at 35
keys a second, so a shot's error is effectively random from one shot to the next rather than a
drift you could see.
