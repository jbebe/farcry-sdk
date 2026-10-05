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

:::info[Seen in a running game]
With the eye moved 10.6 cm ahead toward an AS50's scope, each shot's recoil left the weapon wholly
undrawn for about a third of a second while its sight picture stayed up. Retail GOG v1.03, from an
FCSE plugin logging the weapon's draws.
:::

### The field of view

`CCameraPawnComponent::UpdateCurrentFOV` (`0x10692E30`, server `0x08951870`,
`__thiscall(CPawn* pawn, float seconds)`) is called from `Update` after the camera offset. It works
out this frame's field of view, in radians, at the component's `+0x108`:

1. It starts from the unaimed field of view at `+0x70`.
2. It blends toward a second eased field of view by that one's weight.
3. It blends that toward the iron sights' field of view by their weight.
4. It adds a Perlin noise term, kept at `+0x11C`.

The skills, `*(pawn + 0x10) + 0xD8` (`0x1007E1F0`, the server's `CPawn::GetSkills`), hold two
`CPawnFOV` transitions, at `+0x08` and `+0x24`. Each has a time, a curve, a field of view and the
weight its curve has reached, eased every frame by `BeautifierMaths::UpdateFOV` (server
`0x09007370`):

| Field | Offset in the pawn's data |
|---|---|
| iron-sight weight | `+0xF0` |
| iron-sight field of view | `+0xF4` |
| second weight | `+0x10C` |
| second field of view | `+0x110` |

`CWeapon::OnEquip` (server `0x08F49D00`) fills the first transition from the weapon's iron-sight
properties: its `fIronsightTransitionTime`, its transition curve, and its `fIronsightFOV` (`+0xE4`
in the weapon's properties). A scoped weapon's zoom is that same transition, so the whole view zooms
in as the scope comes up.

`Update` then hands `+0x108` to the scene camera at `+0x28`, and the unaimed `+0x70` as its typical
field of view at `+0x30`. A debug field of view at `+0x10C` takes both instead while it is above
nought. `Update` reads these through its own `this`, the component's second base, 4 bytes on.

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

The desired data is `*(pawn + 0x10) + 0x140` (`0x1007E1B0`), and the effective data, laid out the
same, is `+0x2D0`. Both hold the look in radians:

| Field | Offset in either |
|---|---|
| pitch, up positive | `+0x38`, again at `+0x44` |
| roll | `+0x3C`, again at `+0x48` |
| yaw, left positive | `+0x40`, again at `+0x4C` |

`UpdateLook` reads the effective look from `+0x54` instead while bit 2 (`0x04`) of the effective
flags at `+4` is set.

:::warning[Not yet tested in a running game]
Adding to the desired look after `UpdateLook` should move the view, the gun's aim and where shots go
together, since all three follow it.
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

The weapon's entity is behind an entity proxy at `+0x08`, at the proxy's `+0x0C`, and the entity's
name, a `char*` at its `+0x14`, is its archetype's: `weapons.Primary.M16`,
`weapons.Special.Dart_Rifle`, and so on. The server's `GetWeaponName` (`0x092F1730`) reads the same chain.

When aiming stops, the iron-sight flag goes a few frames before `+0x84` does, and the sight picture
is drawn until `+0x84` goes.

### The weapon's events

`CFCXWeapon::OnEvent` (`0x106D3D00`, `__thiscall(CBaseEvent* event)`) receives the weapon's
events by name. The name is a `std::string` at the event's `+0x08`: its characters are at `+0x0C`,
inline while the capacity at `+0x20` is below 16, and its length is at `+0x1C`. For the weapon in
hand these arrive:

| Event | When |
|---|---|
| `IronSightOnFinished` | the raise to the sights ends |
| `IronSightOffBegin` | the sights start to go |
| `PullTrigger` | the trigger is pulled, once per pull |
| `WeaponFired` | each round leaves, so more than once for one `PullTrigger` held |
| `checkMode` | after a shot, and as the sights come and go |

The rounds in the clip are a count at `+0x20` of the weapon's implementation, `*(weapon + 0x24)`,
and drop by one with each `WeaponFired`.

:::info[Seen in a running game]
Retail GOG v1.03, from an FCSE plugin: the events above were logged, in that order, while firing
through a scope, with the count beside them.
:::

### The scope's post effect

The radial blur toward the screen's edges while a sniper scope is up is a post effect the weapon
starts itself: its `IronsightFX`, which names `PostFX.Sniper.RadialBlur`, drawn by the
`PostEffect_RadialBlur` shader.

- `CFCXWeapon::OnEvent` (`0x106D3D00`, server `0x088D7700`) asks `CPostFxManager` for it
  (`RequestFX`, `0x100B6D30`) on the event that ends the raise. On the events that lower the scope
  it stops it (`StopFX`, `0x100B6EE0`) and calls `ShowHiResScope(false)`.
- The effect's id is read from the weapon's properties at `+0x128`, through `0x100F4280`. The
  properties are `*(*(weapon + 0x24) + 4)` (`CWeapon::GetProperties`, `0x1012E910`).
- An id of -1 means none: `OnEvent` then neither starts nor stops anything, and the scope comes and
  goes as before.

Weapon Overhaul writes -1 there for the scopes it draws itself, while the weapon is lowered, so the
effect is never left running.

:::info[Seen in a running game]
Retail GOG v1.03, from an FCSE plugin: the names above came back for every weapon taken in hand. Of
21 scope-ups traced, `+0x84` came on in the frame the iron sights' field-of-view weight reached one.
:::

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
