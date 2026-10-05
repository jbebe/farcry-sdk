---
title: Silent, deadlier machetes
kind: component
bundle: weapons
claims: []
status: located
systems: [weapons, ai]
match:
  - "generated/entitylibrarypatchoverride.fcb/weaponproperties/handtohand/**#**"
exclude:
  - "**#**/{fIronsightFOV,disEntityId}"
requires: []
verified: diff
---

# Silent, deadlier machetes

Machete attacks make no noise for the AI to hear, hit a little harder and further, and a single
blow can put an enemy into his wounded state.

## How

The mod's copies in `generated/entitylibrarypatchoverride.fcb`:

| Field | Change | `Machete` | `Machete_HomeMade` | `Machete_Modern` | `Machete_Primitive` |
|---|---|---|---|---|---|
| `CommonProperties/bIsSilent` | `False` -> `True` | yes | yes | yes | yes |
| `CommonProperties/bSingleHitHealthFailure` | `False` -> `True` | yes | yes | yes | yes |
| `CommonProperties/bEmitLight` | `True` -> `False` | yes | yes | yes | |
| `CommonProperties/fRange` | `100` -> `120` | yes | | yes | |
| `FireStrategyProperties/fCutEfficiency` | `10` -> `20` | yes | | yes | |
| `FireStrategyProperties/Stim_ImpactDamage/nLevel` | `24` -> `25` | yes | yes | | yes |
| `Stim_ImpactDamage/fPhysImpulse` | `25` -> `30` | yes | | | |

`HandToHand.Machete.Multi` gets the same `nLevel` `25`.

## Depends on

Nothing. Realism Plus raises the damage much further (`35`,
[`weapons-machete-damage`](../../realism-plus/features/weapons-machete-damage.md)) and silences the
kill through the state machine instead; Scubrah's Patch silences it through `hmr.gosm.xml`
([`machete-stealth-kills`](../../scubrahs-patch/features/machete-stealth-kills.md)). The machetes'
iron-sight value `1.308` -> `1.3` is on `weapons-ironsight-fov`.

## Uncertain

- `bIsSilent` (the AI does not hear the attack), `bSingleHitHealthFailure` (one hit can trigger the
  health-failure, wounded state) and `bEmitLight` are read from their names; not traced.
- The readme's "reverted default machete" (2-15-21) leaves nothing to find: the player's pack still
  starts with `weapons.HandToHand.Machete`, as in the base game.
