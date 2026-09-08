# The free camera and noclip

How to detach the view, and how to detach the player, in the retail PC build. Neither is reachable
from the console — [the developer console](./developer-console.md) works through why the survey that
looked for them came up empty. Both are RE-verified against Steam and GOG 1.03 and implemented in
`mods/DevTools/src/engine/`.

## The camera manager

One per scene, embedded in it rather than pointed at, so its address is `scene + 0xB8` and the scene
is `localPlayer + 0x04`. Reading it as a pointer is the first mistake to make.

| Offset | Field |
|---|---|
| `+0x08` | focus entity id, low half |
| `+0x0C` | focus entity id, high half |
| `+0x14` | `Locked` |

The focus id is a pair, and the manager's own guard for "nothing focused" is `(low & high) ==
0xFFFFFFFF` — testing either half alone is not the same test.

`Locked` is what a cutscene sets while it owns the camera. **While it is set, activating a camera by
name is a silent no-op**: no error, no return value, nothing. Anything switching cameras has to clear
it, keep the old value, and put it back on the way out.

### Activating a camera

`CCameraManager::SetActiveCameraByName` (Uplay `0x0057CDE0`) is `__thiscall`, taking a plain `char*`
and a notify flag. It matches case-insensitively against **entity-library archetype names loaded from
game data**, not against strings in `Dunia.dll`. Four are useful:

| Name | In the binary's strings? |
|---|---|
| `Cameras.Camera.First` | yes |
| `Cameras.Camera.Editor` | yes |
| `Cameras.Camera.Spectator` | yes |
| `Cameras.Camera.Free` | **no — data only** |

That last row is the whole reason free-fly was written off as editor-only. It works in retail.

The function **reports nothing**. The only way to know whether a switch took is to read the active
camera back through `CCameraManager::GetActiveCamera` (Uplay `0x0057C1B0`) and see that it changed.
An archetype the data set does not carry leaves the previous camera up, which is indistinguishable
from a refusal except by that comparison.

### Focus, and a by-value reference

A newly activated camera is only pointed at the player on the activation that *instantiates* it;
activate it a second time and it keeps whatever it was looking at. Setting focus by hand is vtable
slot `+0x78`, and its calling convention is the trap:

> **`SetFocus` takes the entity-ref holder by value, and drops one reference of its own.**

So the caller has to add a reference before the call that it does not own afterwards. Get this wrong
and the holder is freed underneath the camera on the next level load.

## Entity reference holders

The engine hands out entities through holders, never raw pointers, because an entity can be destroyed
while something still names it.

| Offset | Field |
|---|---|
| `+0x08` | reference count |
| `+0x0C` | the entity, or null once it is gone |

The entity going null is how a level load is detected: anything holding a reference across frames
should test it and let go rather than follow it. Destroying a holder at zero is two calls in order —
`ReleaseEntityRef` (Uplay `0x0029B7F0`) then `FreeEntityRef` (Uplay `0x00228AD0`) — and the refcount
is decremented by hand, not by either of them.

An entity's world transform is at `+0x30`: 4×4 row-major, rows 0–2 the basis (X right, Y forward,
Z up), row 3 the translation. **Local +Y is forward**, so a yaw taken from the basis needs a quarter
turn subtracting: `atan2(m[5], m[4]) - π/2`.

## Noclip

There is no noclip in the shipped game, and nothing to enable. What there is, is a physics component
whose enable slot can be cleared:

- the physics component comes from the entity's class tag, and the engine registers that tag lazily —
  the guard is a flag it tests on every fetch, so a fetch that skips it gets nothing;
- vtable slot `+0xB4` on the component is enable/disable. Disabled removes collision *and* gravity,
  which is the whole mechanism.

With physics off the character controller goes with it, and three things follow that have to be
handled rather than left:

**The body cannot turn and the head bone cannot look.** Yaw has to be integrated by hand off the
input listener's accumulator and written with `CEntity::SetEuler`. Write yaw only and leave the body
upright: pitching it pitches the head bone and the arms with it. Pitch belongs on the camera, and is
better *read* from the render camera each frame than integrated — integrating on a wall clock drifts
against engine frame time.

**The pawn still reports falling, forever.** The fall update's not-falling tail is at `+0x303` from
the head of that function; sending execution straight there, with `pawn + 0x49B` (the flag other
systems query) and `pawn + 0x4A0` (frames the fall has run) cleared, is what stops it. This must be
gated on the player's own state block — the same function runs for every pawn.

**Sprint still reaches the animation layer** even though it moves nothing. The state block is at
`pawn + 0x10`, with requested state at `+0x140` and current at `+0x2D0`, each carrying a flags byte
at `+0x04` whose `0x40` bit is sprint. Both need clearing, every frame.

### Signals worth refusing

Three signals should not be actioned while the player is off the ground, and the dispatcher they all
pass through takes a CRC32:

| Signal | CRC32 |
|---|---|
| `show_pausemenu` | `0x04127107` |
| `quicksave` | `0xEFEF8B90` |
| `quickload` | `0x9F8F5553` |

Returning **true** is the dispatcher's own "handled, nothing done", so refusing is returning true
rather than skipping the call.

## `CCameraFreeComponent`

`CCameraGhostComponent` derives from it without adding fields, so one set of offsets covers both, and
one hook on its `Update` (Uplay `0x00692050`) drives both. That `Update` is `__thiscall` on the
component's **second base**, four bytes in — the component itself is `ecx - 4`.

| Offset | Field |
|---|---|
| `+0xB4` | move forward |
| `+0xB8` | move strafe |
| `+0xBC` | move vertical |
| `+0xC0` | look yaw |
| `+0xC4` | look pitch |
| `+0xC8` | speed, m/s |
| `+0xCC` | speed adjust |

Move axes are unit vectors in the camera's local frame; the speed field scales them, so it should not
be folded in. Look values are rates — the engine integrates them as `angle += value * frameTime *
180°` — and **pitch is negated**, so up is negative. A whole unit of look is half a turn per second,
far too fast for a key.

The one behaviour that surprises: **the engine writes these fields only when an action fires.** A
released key therefore coasts at its last value rather than stopping, so anything feeding them has to
write one pass of zeros on release and then hand back.

The shipped `free_camera` action mapping still fills these fields from a gamepad stick, so a free
camera is partly pad-driven with no extra work. Mouse look has no such binding and has to be sampled
from the pawn input listener — `+0x10` horizontal, `+0x14` vertical — and consumed.

## Where this runs

`CPawnInputListener::Update` is the right frame for all of it: gameplay input is enabled, a pawn is
alive, and both the listener and the pawn are in hand. It carries **no frame delta**, so one has to be
measured, and clamped — a hitch or a level load otherwise arrives as a step nothing could survive.

Not `CCryEngine::Update`: activating a camera by name from there mutates the manager's array while it
is being iterated.
