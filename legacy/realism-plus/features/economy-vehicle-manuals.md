---
title: Vehicle repair manuals regrouped
kind: component
bundle: gameplay
status: located
systems: [economy, vehicles]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/WeaponBazaar/Item[{jeep wrangler,buggy,datsun,jeep liberty,rover,fishingboat,big truck,swampboat} repair manual]@*"
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/BonusService/Plan[*_vehicle_manual]{,@*,/**}"
exclude: []
requires: []
verified: diff
---

# Vehicle repair manuals regrouped

The two jeeps share one manual, the two boats share another, and the freed manuals now cover the
utility truck and the ATV, which had none. All are sold from act 1.

## How

`engine/gamemodes/gamemodesconfig.xml`, `BonusService` (each plan gives `degradation` and `repairtime`
`-50` percent):

- `Plan[swampboat_vehicle_manual]` and `Plan[jeep_liberty_vehicle_manual]` move their `object` from
  the plan onto their two bonuses and gain two more bonuses for `fishingboat` and `jeep_wrangler`
  respectively.
- `Plan[fishingboat_vehicle_manual]@object` `fishingboat` -> `unimog`;
  `Plan[jeep_wrangler_vehicle_manual]@object` `jeep_wrangler` -> `quad`; the unsold
  `Plan[quad_vehicle_manual]` is removed.

`WeaponBazaar`, the vehicle manuals (keys shuffled like the weapons', `economy-bazaar-reorder`), per plan:

| Manual (plan) | Base | Mod |
|---|---|---|
| swamp boat (+ fishing boat) | 15, act 2 | 20, act 1 |
| Jeep Liberty (+ Wrangler) | 10, act 2 | 15, act 1 |
| fishing boat plan, now utility truck | 10, act 1 | 15, act 1 |
| Jeep Wrangler plan, now ATV | 10, act 1 | 10, act 1 |
| buggy | 15, act 2 | 15, act 1 |
| Rover, Datsun, big truck | unchanged | unchanged |

## Depends on

- The new names ("Swamp Boat and Fishing Boat", "Jeep Liberty and Jeep Wrangler", "Utility Truck",
  "ATV") are `WEAPONBAZAAR_*_REPAIR_MANUAL_NAME` strings in `languages/`, not claimed here; without
  them the shop shows the old vehicle names.
