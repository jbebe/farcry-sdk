---
title: Vehicle repair manual names
kind: component
bundle: gameplay
status: located
systems: [vehicles, economy, ui]
match:
  - "languages/*/oasisstrings.fragment.xml#Items/{swampboat,fishingboat,jeep_liberty,jeep_wrangler}"
  - "languages/*/oasisstrings.fragment.xml#WeaponBazaar/WEAPONBAZAAR_{SWAMPBOAT,FISHINGBOAT,JEEP_LIBERTY,JEEP_WRANGLER}_REPAIR_MANUAL_NAME"
exclude: []
requires: [economy-vehicle-manuals]
verified: diff
---

# Vehicle repair manual names

The shop's vehicle repair manuals are renamed to say which vehicles each one now covers.

## How

`languages/*/oasisstrings.fragment.xml`, all ten languages, both the `Items` name and the
`WeaponBazaar` `…_REPAIR_MANUAL_NAME`:

| Key | Base | Mod |
|---|---|---|
| `swampboat` | Swamp Boat | Swamp Boat and Fishing Boat |
| `fishingboat` | Fishing Boat | Utility Truck |
| `jeep_liberty` | Jeep Liberty™ | Jeep Liberty and Jeep Wrangler |
| `jeep_wrangler` | Jeep Wrangler™ | ATV |

(Translated in each language: "Hydroglisseur et Bateau de pêche", "Camion utilitaire", "Quad"...)

## Depends on

- `economy-vehicle-manuals`, the manual regrouping in `engine/gamemodes/gamemodesconfig.xml`
  (repair-manual items pointing at other names, bonuses and prices). Picked alone these labels
  would name the wrong vehicles.

## Uncertain

- Which shop item ends up showing which label after the reshuffle is not traced item by item.
