---
title: Vehicle copies in the override library
kind: shared
status: located
systems: [vehicles]
match:
  - "generated/entitylibrarypatchoverride.fcb/vehicle/**.xml"
exclude:
  - "generated/entitylibrarypatchoverride.fcb/vehicle/land/bigtruck{,/*}.xml"
requires: []
verified: diff
---

# Vehicle copies in the override library

A shared piece: the mod copies single-player vehicle archetypes into
`generated/entitylibrarypatchoverride.fcb`, where the base game has none. The override library loads
after the world libraries, so these copies are what the game reads - except where a DLC library,
which loads after it, declares the same archetype. This page holds eight of the copies; the three
big trucks are `truck-engine-sounds`.

## How

Whole new units, each carrying the mod's vehicle balancing against the base game:

- `CVehicle/FOV/fFOVAngle` `90` -> `100` - `vehicle-fov-100`
- part `fHealth` x2 to x4 and `fInitialReliability` `1` -> `2` - `vehicle-durability`
- `fEnginePower` and `fGearBoxTopSpeed` x2 (wheeled only) - `land-vehicle-speed`
- `CVehicle/POV/vectorQ*` and `Leaning/fCameraRotationFactor` rounded (`37.999996` -> `38`), the
  editor's float rounding

| Copy | Read in game |
|---|---|
| `vehicle.Air.Paraglider`, `Paraglider.Paraglider_Lv3`, `Paraglider.Paraglider_Lv5` | yes |
| `vehicle.Land.JeepLiberty`, `vehicle.Land.JeepWrangler` | yes |
| `vehicle.Land.Rover.MK19_Mounted` | yes |
| `vehicle.Land.DLC_Vehicle1_DLC1`, `vehicle.Land.DLC_Vehicle2_DLC1` | no - `downloadcontent/dlc1` declares both again and wins |

The two DLC copies were taken from the DLC library's archetypes (quad and Unimog), not from the
world libraries' placeholders of the same name (Datsun and Land Rover models). The game reads the
DLC library's own, separately edited versions instead (`fFOVAngle` `110`, `fInitialReliability`
`1.5`, power and top speed doubled, part health unchanged).

`JeepLiberty`'s `Parts/Part[3]/Name` is mangled from hash `00FD2EFC` into 8 bytes of BinHex, as in
`noise-player-hash-mangling`.

## Uncertain

- The Jeep Liberty's mangled part name probably no longer matches its part; the effect (that part
  undamageable or missing) is not checked.
