---
title: Wider soldier and buddy loadouts, endless grenades
kind: component
bundle: gameplay
status: located
systems: [ai, weapons, buddies]
match:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/WeaponsService/Properties/InventoryPacks/**"
exclude:
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/WeaponsService/Properties/InventoryPacks/Pack[warlord]/**"
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/WeaponsService/Properties/InventoryPacks/Pack[player]/**"
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/WeaponsService/Properties/InventoryPacks/Pack[{warlord_sniper,warlord_shotgun,buddy_sniper}]"
requires: []
verified: diff
---

# Wider soldier and buddy loadouts, endless grenades

Soldiers and buddies draw from a wider set of guns, more evenly across the game. Every one of them
also carries a machete, a flare gun and 99 each of grenades and Molotovs.

## How

`engine/gamemodes/gamemodesconfig.xml`, `WeaponsService` → `InventoryPacks`. For each progression
level (0 to 27) a pack lists the weapons an archetype using it can draw. Changes per pack:

| Pack | Primary or special pool | Sidearm pool | Added |
|---|---|---|---|
| `assault` | the vanilla rifles and machine guns rebalanced to about 11 each, plus `MP5` | `MAC10` and `Uzi` mixed into `DesertEagle`/`Star45`/`Makarov` | machete |
| `buddy` | as `assault` | unchanged | flare gun |
| `shotgun` | `Ithaca`/`SPAS12` 15 each, plus `Usas12` | as `assault` | machete |
| `buddy_shotgun` | as `shotgun` | unchanged | flare gun |
| `sniper`, `RocketLauncher`, `Mortar` | `RocketLauncher` evens out to 14 `Carl_Gustaf` and 14 `RPG7`, dropping `RPG7_Merc`; the others are unchanged | as `assault` | machete, flare gun |

Every pack's grenade `Gadget` goes from `count` 3 to 99, `CarlGustav`'s included. Eight packs gain
a second `Gadget`, `gadgets.Grenades.Molotov` `count="99"`. In `buddy`, 21 entries move one level
earlier and four change probability.

## Depends on

Nothing. These packs arm the campaign's soldiers and buddies and, with
[`ai-editor-mode-services`](ai-editor-mode-services.md), the ones placed in an editor map.
[`ai-armed-faction-leaders`](ai-armed-faction-leaders.md) is the same treatment for `warlord`.

## Uncertain

- Whether a soldier actually throws more than a few of the 99 grenades depends on the brain, and
  has not been checked.
- Which gadget the existing `Gadget[0]` entries hold was not read for each pack. The `count` change
  is the same in all of them.
