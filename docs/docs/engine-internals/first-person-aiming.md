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
with Aiming Overhaul's sway on retail GOG v1.03.
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

The angular offset is pitch, roll and yaw, up positive for pitch, copied from the effective data's
`+0x2C` with the camera shake at `+0x128` added. `Update` turns it into a quaternion with the same
euler order as `CEntity::SetEuler` (`0x100677C0`, on `this + 0xD0`) and multiplies it onto body part
0's rotation, so it turns the view about its own axes and leaves the body, the arms and the gun
where they are. Once placed, the scene camera, `ModifySceneCamera` (`0x10504CE0`) on `this`, holds
the eye's world position at `+0x08`.

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

The console variable `gfx_widescreenmode` decides which way that angle is applied to a wide screen.
With `x` and `y` the half-extents of the projection at unit distance and `fov` the camera's angle
(`0x103ff2c0` in the GOG build, **RE-verified**):

| Mode | Projection |
|---|---|
| 0 (default) | `x = tan(fov/2)`, `y = x / aspect` — the angle is horizontal |
| 1 | `y = tan(fov/2) · 0.75`, `x = y · aspect` — the angle is a 4:3 screen's horizontal one |
| 2 | `y = tan(fov/2) · 0.5625`, `x = y · aspect` — the angle is a 16:9 screen's horizontal one |

Any other value leaves the projection unchanged. The render profile's `ForceWidescreen` letterboxes a
screen narrower than 16:9 into a centred 16:9 band.

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
flags at `+4` is set. It takes no arguments and returns with a plain `RET`. It writes the look to both
copies and carries the effective roll over unchanged, since the mouse has no roll axis.

:::info[Seen in a running game]
Adding the frame's change of an offset to both copies of the desired look after `UpdateLook` moves
the view and the gun together, and the mouse turns on from wherever the offset left it. A slow
breath-and-tremor offset read as a sway through iron sights and scopes alike. Where the shots went
was not checked. Retail GOG v1.03, from Aiming Overhaul.
:::

The player's sprint press is handled in `CPawnInputListener`'s signal handler: on `lock_sprint` (or
the pad's `sprint`), the call at `0x101447C1` fetches the desired data and the next instruction ORs
`0x40` into its flags. EAX holds the signal there and ESI the listener. Jumping from that call to
`0x101447CA`, the handler's return, refuses the press.

:::info[Seen in a running game]
Refusing the sprint press that way while the effective iron-sight bit is set keeps the player down
the sights with sprint held. Retail GOG v1.03, from Aiming Overhaul.
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

## The gun near walls

The engine never moves the gun out of a wall. It draws it in a depth range of its own, so a gun
pushed into a wall is drawn whole in front of it.

`CPhysicsSystem::CastRay` (`0x1048DD30`, `__thiscall` on `*(void**)0x11613D60`, `RET 0x18`) takes
the start, the vector to the end, a hits vector `{hits, count, capacity}`, a `CPhysQueryFilter`, an
object to skip and flags. The editor's `ComputeDropDistance` (`0x1083DCA0`) calls it so, with flags
5: the closest hit only, and nothing cast outside the world. Each hit is `0x30` bytes, its world
position first. The filter is built by `0x1048EC40` from a layer mask and a group; a hits vector
whose capacity's bit 0 is clear is freed with the engine's free (`0x10228AD0`).

`CWeaponState::CanFire(float)` (`0x1012EC50`, `RET 4`) decides every shot, the player's and the
AI's. The weapon is at the state's `+0x00`, and `CEquipmentBase::GetWielder` (`0x10156BA0`) on it
gives the pawn holding it.

A gun can be swung down without the view moving: lower the desired look by an angle (see
[the look](#the-look)) and raise the camera's angular pitch at `+0xD0` by the same angle. The arms
and the gun follow the look; the view is the look turned back up by the offset.

:::info[Seen in a running game]
Aiming Overhaul casts layer mask `0x43B` from the eye along the view, swings the gun down that way
as the room ahead shrinks, clears the desired iron-sight bit and refuses `CanFire` for the player's
weapon while it is down. The gun swings out of walls with the view holding still, and the player
neither aims nor fires until it comes back up. Retail GOG v1.03.
:::

`CastRay` was called from the camera update every frame for this, and up to a few dozen times a
second more for leaning, without trouble.

## Leaning

The player cannot lean. The cover stances the animation graph has a channel for (31, `CoverStance`:
none, leaning left, leaning right, leaning up, blind firing) are fed from `*(pawn + 0x10) + 0x3F0`,
the effective data's `+0x120`, by the beautifier's animation update (`0x100AC190`), but the player's
graph has no clips for them.

`CCameraPawnComponent::Update` does not read the look's roll. It takes the camera's rotation from
body part 0 (`0x1010D850`): that part's skeleton node, through the skeleton's vtable `+0x78`, times
the pawn entity's own orientation. That orientation is the entity's 4×4 at `+0x30`, which
`CEntity::GetOrientation` (`0x104DDA00`) turns into a quaternion.

### Turning the entity

`CEntity::SetEuler` (`0x104E01C0`) takes pitch, roll and yaw and rebuilds the basis as yaw about Z,
after roll about the entity's own Y, after pitch about X, keeping its position.
`CEntity::GetEuler` (`0x104DF830`, through `0x1006AEE0`) decomposes a basis back into the same three
the same way. A pawn faces along its Y row, with X to its right, so a roll leans it sideways and
`atan2(m[5], m[4]) - π/2` reads its heading whatever the roll. Rolling about X instead tips it
forward or back.

Every orientation write ends in `CEntity::SetMatrix` (`0x104DF440`, `__thiscall(const float*
matrix, void* source)`, `RET 8`). It stores the matrix at `+0x30`, calls `vt+0x4C` on each of the
entity's components but `source`, and moves the entities bound to it. It reads the matrix with
aligned SSE loads, so a matrix handed to it must be 16-byte aligned: an unaligned stack copy faults
as a read of `0xFFFFFFFF`. A component is found by `CEntity::GetComponent` (`0x104E2CC0`, the
CRC-32 of its class name); the player's physics is `CCharacterPhysComponent`.

### When the body is read

Within `CEngine::Update`, the pre-physics tick list runs first, then the physics step, then the
physics post-step queue (`0x1058D8F0`, `__thiscall(float seconds)`, `RET 4`), which calls each
registered physics component's `vt+0x90`, then the post-physics tick list, which ends with the
camera.

- The pawn beautifier component's update (`0x100EA050`, `__thiscall(seconds, mask)` on the
  component's updatable at `+0x10`) runs in the **pre-physics** list. It applies the animation's
  root motion (`0x100E9B80`, which `GetEuler`s the body, adds its turn and `SetEuler`s it back),
  then calls its beautifiers' `vt+0x80`.
- In the **post-physics** list something writes the player's matrix through `SetMatrix` once a
  frame, standing it upright, before the camera reads it.
- The weapon entity's own matrix is written through `SetMatrix` only a handful of times in thirty
  frames. The gun is not drawn, and does not fire, from it.

:::info[Seen in a running game]
Retail GOG v1.03, from an FCSE plugin logging the player's matrix and these calls twice a second
while leaning: the beautifier's update ran once a frame for the player, the post-physics write once
a frame, and the weapon's `SetMatrix` 0 to 5 times in 30 frames.
:::

### The body's skeleton

The pawn's body is `*(pawn + 0x10) + 4`: its entity proxy at `+0x04`, its `CGraphicComponent` at
`+0x08`, and the skeleton node of each body part from `+0x10` (`0x1007E1E0` returns it). The
component's `vt+0x78` returns its skeleton, embedded at the component's `+0xE0`. `vt+0x7C` returns it
too but also marks it changed, which has the bones posed again this frame. The human rig hangs
`Camera`, the view's bone, under `Head`, `Neck`, `Spine2`, `Spine1` and `Spine`, and both arms off
`Spine2` (see [the skeleton format](../file-formats/skeleton.md#what-the-human-rig-contains)).

The skeleton keeps its bones at `+0x20` and their count at `+0x2C`; bit 0 of `+0x30` marks them to
be posed. A bone is `0xA0` bytes:

| Offset | Field |
|---|---|
| `+0x00` | CRC-32 of the bone's name, case kept |
| `+0x10` | its animated 4×4, against its parent |
| `+0x50` | its posed 4×4, in the body's own axes: three axis rows, then its place |
| `+0x90` | parent index, `-1` for the root |
| `+0x94` | held: set by hand, and left alone when posing |

Posing (`0x103322C0`, `__fastcall` on the skeleton) runs only while bit 0 is set, and rebuilds every
bone not held from its animated matrix and its parent's posed one. It is the synchronous path, for a
skeleton's first build and the engine's own procedural bones (`0x100C2FA0`, for one), which read a
posed matrix (`0x10331F80`), write it back held (`0x10331F10`), and pose again.

Every frame the posing is a job instead. The graphic component queues two, PS3-style, while anything
has asked for the skeleton in the last 60 frames: each object declares its buffers as plain pointers
(counts at the declaration's `+0x00`–`+0x0C`, the pointers from `+0x14`) and names a kernel taking
the job's context, whose first field is that declaration.

- `SkeletonObjectJob` (the object at the component's `+0x40`, kernel `0x1064EC50`) is a copy of
  posing, run whether or not bit 0 is set. It declares the bones, then the skeleton.
- The skin job (the object at `+0x1C8`, kernel `0x1061F3A0`) is declared only while bit 0 is set. It
  builds each kit part's matrices from the posed ones, through the part's node-to-bone map.

Then the component's sync (`0x1051B2A0`) clears bit 0, moves the entities bound to it, and places its
rigid parts from their bones.

:::info[Measured in a running game]
On the player's first-person body (107 bones at run time) every bone but the first is held: the
animation writes each posed matrix itself, before the skeleton's job, so the job's posing leaves
them all alone. A posed matrix changed right after the job was still unchanged when the skin job
read it and when the camera did, 60 frames in 60. Retail GOG v1.03, from Aiming Overhaul.
:::

The camera turns with the `Camera` bone's posed matrix, and a shot's first-person origin
(`0x10112600`) is that bone's place in the world (`0x1010D780`).

An entity bound to a bone is placed from that bone's posed matrix (`0x105530F0`, `__thiscall` on
the parent's binding with the child's binding and the parent entity's matrix, `RET 8`): the child
binding's `+0x1C` is the bone's index, and the parent's matrix times the bone's is written to the
child's `+0x40`, which the entity is then placed from. The parent's matrix there is turned like the
entity but, where the parent binding's `+0xC5` is set, has its place left out. The parent binding lists its children as entity proxies at `+0x2C`, with their
count at `+0x30`, and an entity's binding is at its `+0x78`.

:::info[Measured in a running game]
On the player: the first-person camera (`cameras.Camera.First`) hangs from `Camera`, the weapon in
hand from `R Hand`, slung long guns from `Spine2`, a holstered pistol and the machete from a thigh,
and the gadgets and grenades from `Pelvis`. Down the sights the AK47 moves to `Camera`, which lines
its sights up on the eye; the Desert Eagle stays on `R Hand`. Retail GOG v1.03, from Aiming
Overhaul.
:::

The first-person body is drawn lined up on its `Camera` bone. Turn every bone but `Camera` about a
point after the skeleton's job, and the arms and the gun turn on screen. Turn `Camera` with them,
and the arms and the gun stay where they were in the world while the view turns.

### Leaning by the bones

Aiming Overhaul leans the player by bending the body's posed bones after the skeleton's job: `Spine`
and every bone below it in the tree but `Camera`, about `Spine`'s place, round the body's forward
axis, and sliding them to the side along its X. Every bone of the player's body is held, so it
bends those too, once per write of the animation, and marks the skeleton changed through `vt+0x7C`
each frame the bend moves. The view is rolled by the same angle at `+0xD4`, and the eye moved at
`+0xE8` and `+0xF0` by how far the bend and the slide move `Camera`, in the rolled view's axes.
Whatever else hangs from `Camera`, a long gun down the sights, has its copy of the bone's matrix
taken back into the body's axes, bent the same way, and turned back; the camera's own is left
alone. The slide and the bent long gun are seen in game, the long gun while turning not yet.

:::info[Seen in a running game]
The view, the arms and the gun lean together. Retail GOG v1.03, from Aiming Overhaul. The muzzle
flash, a scope's sight picture and leaning while looking up or down were not checked.
:::

### What a lean was tried as

Before that, Aiming Overhaul tried six ways of leaning the player, each seen in game on retail GOG
v1.03. None leans the view, the arms and the gun, the muzzle and a scope's sight picture together.

1. **The pawn entity rolled from the camera update** with `SetEuler`. The view rolls about its own
   axis and nothing else: the eye does not move, and the arms and the gun stay level with the world,
   so they tilt on screen. They were posed earlier in the frame, and the camera reads the entity's
   orientation lazily for body part 0.
2. **The camera's own roll and eye.** The roll at `+0xD4` and the eye at `+0xE8` (turned into the
   rolled axes, since the positional offset is turned by the full camera rotation) lean the view
   cleanly. The gun is placed through `c0`–`c3`, the view's rotation times the projection, with the
   projection alone at `c8`–`c11` (see [the weapon's passes](./presentation-and-input.md)): giving
   each of its draws `P · A · P⁻¹ · C` in place of `C`, with `A` the view's roll and the eye's move in
   camera space, draws the arms and the gun exactly where they sit unleaned, so they lean with the
   view. A shot can be moved with the eye in the bullet strategy's first-person setup (`0x10112600`,
   `__thiscall(pawn, origin, direction, ·)`, `RET 0x10`), which writes the origin from body part 0.
   But iron sights come up and a scope's sight picture never shows, and the muzzle flash stays where
   the gun is in the world. The `SCOPE_HI` part, a few centimetres ahead of the unleaned eye, is
   probably culled from the narrow magnified view; that is not confirmed.
3. **The pawn entity rolled right after the post-step**, with `SetPosition` and `SetEuler`, which
   tell every component. The muzzle flash then follows the lean, but the body's heading turns on its
   own for as long as the lean is held, by up to a quarter turn, and the camera with it; and the
   post-physics write has stood it upright again by the time the camera reads it.
4. **The pawn rolled about its X row** from the post-step, on every write. The body tips forward: the
   view rises and turns.
5. **The pawn rolled after the pre-physics beautifier and stood upright after the post-step.** The
   roll is gone before anything is drawn: the beautifier runs before the physics.
6. **The pawn rolled after the post-step without the physics told** (`source` its
   `CCharacterPhysComponent`), re-rolled after each of the engine's writes, both for the post-physics
   list only and for the whole frame. The matrix holds the roll when the camera reads it, every
   frame, yet the arms and the gun do not move at all, and the body does not turn.

From 3 and 6 the drawn body seems to follow the physics' own copy of the body, not the entity's
matrix, and telling the physics of a tilt sets it turning the body. A physics component told of the
lean only between the post-step and the next pre-physics tick list was the next step, untried.

:::info[Seen in a running game]
Weapon entities leaned in the world on every `SetMatrix` move nothing visible, as the writes above
predict. Moving the drawn gun by its draws alone, as in 2, keeps it in the player's hands only while
the arms are moved with it: rewriting the arms' draws (40-byte skinned vertices) and not the weapon's
(32-byte) leaves the gun behind.
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

### Where the scope is drawn from

:::info[Verified via reverse engineering]
Read out of `ShowHiResScope`, `CGraphicComponentObject::SetIsVisible` (`0x10519010`), the geometry
loader (`0x103D2E50`, its `SDOL` chunk at `0x103D2700`) and `CGeometryResource::ClientProcessRawData`
(`0x1035F080`, server `0x097FB3F0`). For all six scoped rifles, the counts this gives from the retail
`.xbg` files are the ones Aiming Overhaul measured off their draws in a running game. The walk below
has not yet been run in game.
:::

`ShowHiResScope` takes the weapon entity's `CGraphicComponent` (`CEntity::GetComponent`,
`0x104E2CC0`, by the CRC-32 of the class name) and walks its parts: an array at `+0x30`, `+0x34` of
them. A part keeps the hash of its name at `+0x0C`, which is how `SCOPE_HI` is found, whether it is
shown at `+0x30`, and at `+0x34` a helper holding the part's `CGeometryResource` at `+0x10`. Every
part of a weapon is drawn from the one resource, its `.xbg`.

The part's `+0x2C` is not its `hidNodeNameLOD0`, though `SGraphicComponentObjectDescriptor::
RegisterProperties` (`0x10519510`) registers that field at `+0x2C` of the descriptor: in a running
game it held a heap pointer. When the engine builds a part from the mesh's file descriptor, it names
the node of its nearest detail after the part with `_LOD0` added (`0x1051DE56`).

The resource keeps the mesh as loaded at `+0x38`. In it:

| Offset | |
|---|---|
| `+0x04`, `+0x08` | how many details there are, and the details, `0x24` bytes each, nearest first |
| `+0x60` | the file's `DIKS` table, 8 bytes an entry: the CRC-32 of a node's name, such as `SCOPE_HI_LOD0`, then its part and node as two `uint16` |

A detail:

| Offset | |
|---|---|
| `+0x00`, `+0x04` | its vertex blocks, 16 bytes each: format, stride, vertex count, byte offset |
| `+0x0C`, `+0x10` | its draws, `0x1C` bytes each: vertex block, `DIKS` entry, cluster, first index, last vertex, byte offset |
| `+0x18` | the distance it is drawn from |
| `+0x1C`, `+0x20` | the resources holding its vertices and its indices |

The loader makes both resources. The vertex one keeps the device's buffer at `+0x0C`, and that
buffer keeps the `IDirect3DVertexBuffer9` at `+0x08`: it is what the engine hands `SetStreamSource`
(`0x10430180`). A draw's first index is the `startIndex` of its `DrawIndexedPrimitive`.

So the gun in first person is drawn from its nearest detail's vertex buffer, and the sight picture
by the draws whose `DIKS` entry is `SCOPE_HI_LOD0`. The gun's depth pass joins draws that follow one
another in the index buffer: the Dragunov's single 2,088-triangle draw there is three of its scope's,
1,008 + 936 + 144 triangles, and starts at the first one's index.

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

In data the field is the weapon's `IronsightFX`, a hash naming an effect in the post-effect
database: `ironsightfx` on every retail weapon, `FFFFFFFF` for none. Aiming Overhaul's weapon data
sets none on the scopes it draws itself.

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
