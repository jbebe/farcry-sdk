# Cheat flags, vitals and the economy

What the `cheat_*` console commands actually reach, and what they do not. RE-verified against Steam
and GOG 1.03; implemented in `mods/DevTools/src/options/`.

## The game profile's cheat flags

Three `int32` fields on the game profile, and the whole of what the `-GameProfile_*` launch arguments
and the matching `cheat_*` console commands set:

| Offset | Flag | Console command |
|---|---|---|
| `+0x94` | GodMode | `cheat_GodMode` |
| `+0x98` | UnlimitedAmmo | `cheat_UnlimitedAmmo` |
| `+0xA0` | AllWeaponsUnlock | `cheat_AllWeaponsUnlock` |

Consumers compare them unsigned, so anything other than 1 and 0 risks reading as set.

**Setting one once is not enough.** A profile reload writes the file's values back over them, and a
multiplayer spectator transition clears GodMode outright, so anything that wants a flag held has to
re-apply it. The profile pointer itself is readable out of the `MOV EAX, [imm32]` that opens
`CWeaponBazaar::IsWeaponUnlocked`.

## What GodMode does not cover

Two things, and both are why the console command alone reads as unreliable in play.

### The health-failure threshold

GodMode suppresses damage but never *restores* health, so a player already below the failure
threshold when it is switched on stays one scratch from going down. The campaign player's vitals live
on `CFCXCountersComponentPlayerSP`:

| Offset | Field |
|---|---|
| `+0x44` | the `CCounter` health lives on |
| `+0x6C` | health-failure threshold |
| `+0x88` | forced failure |
| `+0xE8` | rescue state |
| `+0x140` | revive invulnerability |

The threshold is `80.0` from the constructor but **raised at runtime**, so it has to be read every
time rather than assumed.

Health sits at `+0x10` on the counter, and the way to raise it is vtable slot `+0x20`,
`CCounter::SetToMax` — a direct write to `+0x10` skips the HUD and event chain that hangs off it.

The last three rows are the states where the engine parks the player below the threshold *on
purpose*: a scripted forced failure, a buddy rescue, and revive invulnerability. Anything topping
health up has to skip all three, or scripted failures and rescues stop resolving.

### Vehicles

GodMode reaches no vehicle damage path at all. Two entry points on `CVehiclePhysComponent` carry it:

| Function | Uplay RVA |
|---|---|
| `ApplyHealthDamage` | `0x00067170` |
| `ApplyStimToParts` | `0x00071450` |

Both have gate bytes that would suppress damage — but those bytes are **recomputed every physics
step**, so clearing them does not hold and this is one of the few places a patch is the wrong tool.
Passing `0.0f` damage through `ApplyHealthDamage` is better than skipping the call, because the health
ratio at `+0x90` is still recomputed. `ApplyStimToParts` is safe to skip outright: both callers ignore
its return, and the collision-immunity timer it sets only throttles damage already suppressed.

The pawn keeps **no pointer to the vehicle it is in**, so the link has to be walked each frame:
`CVehicle::GetCurrentVehicle` (Uplay `0x000E7330`) from the seat fact, its ref block at `+0x08`, the
entity at `+0x0C`, then the physics component. The entity's async job must be flushed first (Uplay
`0x004DD4F0`), which means main thread only.

`GetVehiclePhysics` (Uplay `0x000649F0`) shares its bytes with 69 other component getters, and telling them apart by pattern needs the call displacement left unwildcarded — which
differs between builds. The address library knows the entry, so this is one case where the table is
strictly better than a pattern.

Drowning and scripted destruction are separate paths again, and stay lethal.

## Unlimited ammo also makes healing free

Magazines and consumables come out of **one shared item decrementer** (Uplay `0x00145100`), which
returns early without decrementing anything while UnlimitedAmmo is set. So `cheat_UnlimitedAmmo`
silently makes syringes free too, and with them the malaria and health economy the campaign is built
around.

Nothing in the arguments distinguishes a syringe from a magazine — the only discriminator is **which
call site called**. Three sites spend a syringe, and all three end on the same thirteen bytes:

```
3B C1 75 04 33 C9 EB 02 8B 08 6A 01 E8
```

They are told apart by the instructions *ahead* of that tail, which differ in the stack slots they
read:

| | Prefix | Return address |
|---|---|---|
| A | `8B 44 24 18 8B 50 04 8B 00 8D 0C 90 8B 44 24 10` | match `+0x21` |
| B | `8B 44 24 1C 8B 50 04 8B 00 8D 0C 90 8B 44 24 14` | match `+0x21` |
| C | `8B 48 04 8B 10 8B 44 24 10 8D 0C 8A` | match `+0x1D` |

Each pattern ends on the `E8` opcode, so the return address is the match length plus four. All three
are unique across both shipped builds.

## The two act gates

`AllWeaponsUnlock` bypasses the per-weapon unlock list and nothing else, which is why the bazaar still
only stocks the current act. Two act-tag comparisons in `CWeaponBazaar::IsWeaponUnlocked` are the
rest:

```
80 7E 58 01   cmp byte ptr [esi+58h], 1     ; act tag, at pattern +3
75 09         jnz short +9
...
80 7E 58 02   cmp byte ptr [esi+58h], 2     ; at pattern +18
```

The tag is only ever 0, 1 or 2, so writing `0xFF` over each immediate makes both jumps unconditional.
One byte per site, branch displacements untouched.

## The economy

`CEconomyComponent` holds the wallet at `+0x10`. Three `int32` properties carry a diamond count into a
save, and they are distinguished by CRC-32 of the property name **and** field offset, because the two
HUD mirrors carry the same hash on a different object:

| Property | CRC-32 | Offset |
|---|---|---|
| `DiamondCount` (wallet) | `0x333DBF78` | `0x10` |
| `DiamondCount` (HUD) | `0x333DBF78` | `0x2BC` |
| `LastDiamondCount` (HUD) | `0x3A8909F7` | `0x2C8` |

A property descriptor is `+0x04` name, `+0x08` name hash, `+0x0C` field offset, `+0x10` flags.
`CConstIntProperty::Serialise` is shared by every `int32` property, so anything hooking it has to
filter on the descriptor. It is **not** an entry the address library knows, and neither is the game's
signal dispatcher — both stay on patterns.

Locating the component takes four sightings, because no single one covers every session: its
constructor, `AddDiamonds`, `RemoveDiamonds`, and the bazaar page snapshotting the wallet as it
opens. `RemoveDiamonds` is worth hooking before its own clamp and store, and anything mirroring its
arithmetic has to clamp to the balance the same way it does.

The component dies with the player while an external clock keeps running, so **every** write to a
sighted component needs a liveness test: a live local player, and the object still carrying the vtable
it was first seen with.
