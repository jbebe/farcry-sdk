---
sidebar_position: 22
---

# First-person aiming

:::info[Verified via reverse engineering]
Traced in `FarCry2_server` (symbols) and mapped into `Dunia.dll` (Steam v1.03). Addresses without a
binary named are Steam `Dunia.dll`. FCSE's address library maps every one of them to GOG.
:::

:::info[Verified in a running game]
Moving the eye through the camera's positional offset moves it relative to the gun, not the gun
along with it: down a G3's iron sights, the rear sight moves further than the front one. Measured
with Weapon Overhaul's sway on retail GOG v1.03.
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

## The look

The camera's look angle and the direction a first-person shot leaves in both come from part 0 of
the pawn's body: `CPawnBody::GetBodyPartRot` (server `0x08FD2B80`) is the entity's orientation times
one skeleton node's rotation.

`CPawnInputListener::UpdateLook` (`0x10143C40`, server `0x08FDE5C0`, `__thiscall`) builds the look
anew every frame. It runs from the listener's `Update` unless the pawn is in a vehicle, and sets the
desired look to the effective look plus the mouse axes times the frame time. The listener's pawn is
at `+0x20`.

The desired data is `*(pawn + 0x10) + 0x140` (`0x1007E1B0`). It holds the look in radians:

| Field | Offset in the desired data |
|---|---|
| pitch, up positive | `+0x38`, again at `+0x44` |
| roll | `+0x3C`, again at `+0x48` |
| yaw, left positive | `+0x40`, again at `+0x4C` |

:::warning[Not yet tested in a running game]
Adding to the desired look after `UpdateLook` should move the view, the gun's aim and where shots go
together, since all three follow it. Weapon Overhaul's scope sway is the first thing to do it.
:::

## The first-person body

Whether the player is drawn as the first-person body is bit 7 (`0x80`) of the same byte in the
pawn's **desired** data, `*(pawn + 0x10) + 0x144`, whose `0x40` bit is sprint. Two things read it:

- `CPawnBeautifierComponent::BuildTag` (`0x100E9230`) picks the FirstPerson or ThirdPerson pose.
- `CPawn::UpdateMaterialMask` (`0x10080FC0`) sets the full-body flag at `*(pawn + 0x10) + 0x49A`,
  and shows or hides the kit parts tagged `FirstPerson`.

Three cameras write it:

- `CCameraPawnComponent`'s activation (`0x10693910`) sets it.
- `CCameraThirdComponent`'s update (`0x10695BC0`) clears it and asks for the full body, while that
  camera is active.
- The free camera's update sets it every frame from the camera's own flag at `+0xCD`, which
  `camera_toggle_first_person` flips.

:::info[Seen in a running game]
Clearing the bit does not give the player a body worth showing. Seen from `Cameras.Camera.Third` in
a vehicle, the player is still headless and still in the pose made for the first-person camera. From
the free camera, the body disappears beyond about 10 m. The model and its driving animations are
first-person only.
:::

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
