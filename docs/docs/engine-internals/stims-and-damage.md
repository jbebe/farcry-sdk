---
sidebar_position: 29
---

# Stims and damage

:::info[Verified via reverse engineering]
Code traced in `FarCry2_server`; every address here is the server's. `CStimsEmitterComponent`,
`fRateOfEmission`, `selUsableBy` and `bPlayerOnly` are in GOG `Dunia.dll` too (found as strings; the
functions were not matched). Data read from world 1's `entitylibrary_full.fcb` and the 250 sectors of
`w1_b_2`, decoded with `jackall-cli fcb decode`. Claims marked *(inferred)* were not read in code or
data. Nothing here was seen in game.
:::

Everything that hurts in Far Cry 2 is a stim: a `CEntityEventStims` with a type, a level, a radius
and a falloff. This page covers how a stim finds its receivers, the one component that sends stims on
a timer, and how the player and breakable props turn a stim into damage. It was written for
[a static snake](../design/attacking-animals.md#the-snake), so it stops where that needs it to.

## The stim

`CEntityEventStims` is 0x7C bytes (constructor `0x09447FF0`, vtable `0x0A3B53C8`). The fields that
matter:

| Offset | Field | |
|---|---|---|
| `+0x18` | position | where the stim comes from |
| `+0x24` | sender | the entity that sends it |
| `+0x28` | instigator | who is to blame: the shooter. [`CanDoDamage`](#the-player) reads it |
| `selType`, `nLevel`, `fRadius`, `bFalloff`, `nFalloffMinLevel`, `sDetail` | the data fields | `sDetail` names the kind, such as `explosion` |

Types, in archetype order: 0 None, 1 Bang, 2 Snap, 3 Pierce, 4 Crush, 5 Cut, 6 Health, 7 Burn, 8 Water,
9 Panic, 10 Fear, 11 Morale, 12 Dirt, 13 Reliability, 14 Interest. `DamageHealth` (`0x09446F50`) is
true for 3, 4, 5 and 7.

## How a stim reaches entities

`CEntityEventStims::Send(TEntityHandle ignore)` (`0x09449BA0`), for a stim with a radius:

- **Bang, Snap, Panic, Fear, Morale and Interest** go to `CAIEngine::OnEvent`, as things the AI
  senses.
- **Every other type** asks physics for every entity in the box of ±`fRadius` around the stim's
  position (`GetEntitiesAroundAabb`, collision layer mask `0x7FF`), keeps those whose
  `GetDistanceToEntity` is at most `fRadius`, and calls each one's `OnEvent`. A second pass over
  `CAABBPartitionManager::GetEntitiesInRadius` reaches objects that are not physics entities
  *(inferred: vegetation)*.

The only entity skipped is the `ignore` handle. The no-argument `Send()` (`0x0944B650`, vtable slot
`+0x24`) passes an invalid handle, so it skips nothing, **the sender included**. Where `bFalloff`
lowers the level with distance was not found in `Send`; it is applied on the receiving side
*(inferred)*.

## Stim emitters

`CStimsEmitterComponent` sends its stims over and over, with no brain, script or trigger.

| Field | |
|---|---|
| `bEnable` | whether it runs; saved |
| `fRateOfEmission` | **seconds between pulses**, despite the name; 1 by default |
| `Stims` | the stims, each with its own type, level, radius and falloff |
| `fTimeElapsed` | the running timer; saved |

- **`Update`** (`0x093535A0`): while enabled it adds the frame time to `fTimeElapsed`. Once that is
  more than `fRateOfEmission`, it stamps each stim with its own entity as sender and its own position,
  sends it with the no-argument `Send()`, and resets the timer. It sets no instigator.
- **`OnEvent`** (`0x09353180`): an event named `Enable` sets `bEnable`, one named `Disable` clears it,
  and either resets the timer. `Init` (`0x093536B0`) also registers both names as registered events.
- **It stims its own entity.** Nothing skips the sender, so an emitter on an entity with a physics
  body sends its stims to that body as well.

**Retail use: campfires.** The eight `StimEmitters.Burn.*` archetypes are bare emitters, with
`CStimsEmitterComponent` and `CPersistComponent` level 0. `BurnEmitter` sends Burn level 15 within
1 m with falloff to level 1, once a second, and carries a second, empty stim. 203 sector files place
one. In the campfire at (1,332.2, 3,222.2) in `worldsector4020`, `Lighting.MediumCampFire_0` is a
prefab of seven entities:

- the burn emitter (`BurnEmitter_Small`), placed with `bEnable False`;
- the log pile, `OA_Burnable.Burnable.CampfireLogs`, a breakable prop. Its `OnStateChange` sends the
  emitter `CEntityEvent` `Disable`, switches the light off with `DeactivateLight`, stops the particles
  and plays `0x0044F70A` on the sound point;
- the light-the-fire and put-it-out duty points, whose `STPSpecialActionTriggered` send the emitter
  `Enable` and `Disable`;
- a light, a particle emitter and a sound point.

The emitter sits inside the logs, and the logs' live state has `bBurnStim False` with Pierce and
Crush on, so its own fire cannot break them.

The sound point is an entity with only an empty `CSoundComponent` (`sndptSoundPoint` `FFFFFFFF`) and
a `CEventComponent`. `CSoundComponent::OnEvent` (`0x095821F0`) plays any `CSoundEvent` it receives on
its own entity, so any entity carrying that empty component can be a link's sound target.

## The player

`CFCXCountersComponentPlayer::HandleDamageStim` (`0x087FDF20`) takes any `CEntityEventStims`, from a
bullet or from an emitter alike, and calls `NotifyAttackerHUD`. Its gate is
`CFCXCountersComponent::CanDoDamage` (`0x087E7250`), which refuses a stim:

- whose instigator is the receiver itself, unless its `sDetail` is an explosion, fire spreading or a
  bullet impact;
- while the receiver is invincible, or dead without having fallen to death;
- that the game rules block as friendly fire;
- of a type the receiver is immune to.

`tables.StimEffectTables.PlayerDefault` sends Pierce, Crush, Cut and Burn through one curve,
`Curves.StimEffectCurves.OldDamageHealth`, and Health through `OldRestoreHealth`.
`ObjectDefault`, the table of 1,365 archetypes, uses the same damage curve for the same four types.
The curve's knots:

| Level | 9 | 18 | 24 | 28 | 30 | 32 |
|---|---|---|---|---|---|---|
| Health lost | 47 | 100 | 202 | 399 | 1,000 | 1,604 |

The player's maximum is a `Curves.PlayerSicknessCurves.HealthMax_*` curve per difficulty. Its first
pair of knots runs from 1,800 to 1,300 on Casual, 1,400 to 1,025 on Experienced, 1,075 to 825 on
Hardcore and 825 to 700 on Infamous. What the curve's input is was not checked. In game the player had
495 or 825 on Hardcore and 780 or 1,300 on Casual at different times.

**Hit locations are off for the player.** `ApplyLocationMultiplier` (Steam `0x107645E0`) scales a hit by
the body part only when a flag on the receiver's counters component (`+0x48`) is set, and the player's is
not. In game a head hit on the player did what a torso hit does.

**The last bar is cushioned.** `CFCXCountersComponentPlayerSP::ApplyDamageMultiplier` quarters a hit
while the player's health is below one health bar. In game, below about 120 health a hit that took 42
elsewhere took 13 or 14.

### Point-blank damage

`CWeaponFireBulletStrategy::SendImpactStims` adds to the level of a bullet the **player** fires by the
range it lands at. The bands are the weapon's `RangeMultipliers`, one list per difficulty; the AK-47's:

| Difficulty | 0-2 m | 2-5 m | 5-10 m |
|---|---|---|---|
| Casual | +7 | +3 | +2 |
| Experienced | +3 | +2 | +1 |
| Hardcore | +2 | – | – |
| Infamous | – | – | – |

The other primaries and pistols follow the same shape. A soldier's bullet skips the bands: its level is
scaled by `GetAIDamageMultiplier` at the range, after a second roll against
`GetAIPrimaryBulletChanceOfSuccess` at the range. What those two read was not traced. This is **traced
in the server build** only.

## Soldiers

A soldier's hits go through `CFCXCountersComponentAI::DamageHealth` (Steam `0x1069C9C0`). It scales a hit by
the body part with `DefaultCountersService/HitLocations`: head 6, torso 1, arms 1, legs, hands and
feet 0.5.

**His health is his army's, not his own.** `CFCXCountersComponentAI::ApplyArmyPillar` (server
`0x087EDED0`) sets his health and his weapon's reliability from a value kept per army, blue or red,
unless his archetype's `fAgentHealth` is 0 or more. The values come from `CounterHealthRatingsTable`
and `CounterReliabilityRatingsTable`, rated high, medium, low and failure per act:

| Act | Health high / medium / low / failure | Reliability high / medium / low / failure |
|---|---|---|
| 1 | 250 / 150 / 130 / 50 | 240 / 120 / 80 / 20 |
| 2 | 250 / 130 / 100 / 50 | 180 / 130 / 100 / 20 |
| 3 | 250 / 160 / 120 / 50 | 220 / 160 / 120 / 20 |

In game every merc had 250, the high rating; which rating each army holds was not traced.

**Going down.** A hit that takes him under the failure value (50) downs him instead of killing him
only if `CanHealthFailureOccur` (server `0x087EE270`, Steam `0x1069B8D0`, `RET 8`) allows it; otherwise
the hit kills him outright, whatever health it would have left. The body part plays no part in the
decision, though the stim it is given carries it:

- never in a vehicle;
- always for a machete hit;
- otherwise only for a hit by the player, while fewer soldiers of both armies are down than the
  player's reputation level allows, on a roll against that level's chance times the weapon's
  modifier.

The levels are `Infamy/Levels` in `gamemodesconfig.xml`. Since reputation rises with the story, more
soldiers go down as it goes on:

| Reputation | Chance | Down at once |
|---|---|---|
| 0-15 | 15 % | 2 |
| 16-35 | 25 % | 3 |
| 36-65 | 35 % | 5 |
| 66-95 | 50 % | 8 |
| 96-130 | 65 % | 11 |
| 131+ | 80 % | 14 |

The cap is `CFCXCountersService::GetMaxNPCsInFailure` (Steam `0x107645C0`). This is **traced in the
server build** and matched in `Dunia.dll`.

A weapon can override that rule for the player's hits. Its `selHitLocation_Torso_Severity` and
`selHitLocation_Limb_Severity` (0 normal damage, 1-3 health failure levels, 4 kill) pick the hit's
path in `DamageHealth`. At 0 the rule above applies, and it has no cap on overkill: a hit far past his
health still downs him at 49. At 1-3 the same decision runs on every hit, so a hit that would not have
taken him under 50 can down him too, and a refused one deals its damage or kills as before. At 4, and
on any head hit, he dies. In world 1's library:

| Weapon | Torso | Limbs |
|---|---|---|
| AS50 | 4 | 3 |
| Dragunov | 3 | 2 |
| Desert Eagle | 2 | 1 |
| Dart Rifle, LPO-50 | 4 | 4 |
| M1903 | 1 | 0 |

Every other weapon leaves both at 0.

A downed soldier runs `CBrainMercThresholdHealthVictim`: he calls for help, limps to cover, waits to be
carried or healed by a comrade, and while the target is visible and close enough aims at it and
shoots (`RepelTarget`). Which weapon he shoots with there was not checked. In the range a comrade
lifted a downed soldier onto his shoulders, carried him a moment and dropped him on the ground.

What a downed soldier then soaks is on the [todo page](/farcry-sdk/todos).

## Breakable props

A `CCompoundPhysComponent` object is a state machine over named parts. 683 of world 1's 954 compound
archetypes have two states. Each state node carries:

- `fHealth`;
- a `Stim` block, `bPierceStim`, `bCrushStim` and `bBurnStim`, saying which types hurt it. There is no
  flag for Cut;
- `OnDamage` and `OnEvent` transitions, `selNextState`: 0 Destroy, 1 Stay, 2 Next, 3 Specify;
- optionally an `Explosion` with its own stims, sent on entering the state.

The node's mesh parts and `.hkx` bodies share the state's names (`StatePrim01` whole, `StatePrim02`
and `03` the broken pieces, `HIGH_StatePrim01` the detailed collision), and the archetype's
`hidDescriptor` bakes the `.xbg` object indices, bounding boxes and state-to-body map.

**Damage.** `CCompoundPhysComponent::DamageParts` (`0x09524CD0`) returns at once when
`Damage.bPlayerOnly` is set and the stim's instigator is not the local player. Otherwise the node's
`ApplyDamage` (single body `0x0954A290`) asks the counters service how much the stim takes, and the
stim's own virtual, handed the node's `Stim` flags, decides whether it counts. That virtual was not
followed, so whether a Cut stim leaves a node alone is *(inferred from the flag set)*.

**Events.** `BroadcastEvent` (`0x09523790`) raises:

| Id | Name | Delivered |
|---|---|---|
| 0 | `OnStateChange` | to the entity's own `OnEvent` |
| 1 | `OnPartBreakOff` | to the event links (`CEventComponent::BroadcastEvent`) |
| 2 | `OnDamageLastState` | to the event links |
| 3 | `OnEventLastState` | to the event links |
| 4 | `OnEventDamageStateChange` | to the entity, its bound children and the event links |
| 5 | `OnDestroy` | to the entity's own `OnEvent` |
| 6 | `OnDamage` | to the event links |
| 7 | `OnPostStateChange` | to the entity's own `OnEvent` |

`OnStateChange` still reaches links: retail campfires and street lights link on it. A street bulb in
`worldsector3860` (`Lights.LightsStreetColonial01_Bulb_34`) sends its time-of-day trigger
`CTriggerEnableEvent` `SetDisabled` with `bEnable False` when it is shot out.

## Scripts

The Lua global `SendPierceStimWithOrigin(target, origin, level)` (`0x086DC880`) builds a bullet hit,
a `CPhysBulletHitStim` of type Pierce at the given level, placed at the target's position with
`origin` as the shooter. It hands it straight to the target's `OnEvent`, with no radius, and then
sends the target a `CPawnEventFakeBullet`. The Domino node `SendPierceStim` calls it, or
`SendPierceStimNoOrigin` without an origin, with level 15 when none is given.

## Mines

The IED mine (`weapons.Explosives.IED_Base.IED_Mine`) is a `CExplosive` with no trigger. It explodes
on a Burn stim (`StimsToExplode`) or from the detonator, then sends Crush 29 within 15 m, Bang 10
within 150 m and two short-range Burns. No retail sector places one. So the mine deals no damage by
proximity; of what was read, the stim emitters are what hurts whoever stands in a spot. Fire regions
(`CFireRegionComponent`) were not read.

## Unknowns

- Whether an emitter's stim reaches the player. The physics query should include the player's
  character body *(inferred)*.
- What `NotifyAttackerHUD` shows for a stim with no instigator.
- Where falloff is applied, and whether `GetDistanceToEntity` measures to the entity's position or its
  bounds.
- Whether a compound node ignores Cut.
