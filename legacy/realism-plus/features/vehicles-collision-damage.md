---
title: Vehicles take less damage per crash
kind: component
bundle: gameplay
status: located
systems: [vehicles]
match:
  - "{generated/entitylibrarypatchoverride.fcb,downloadcontent/dlc1/generated/entitylibrary.fcb}/vehicle/**#**/nMaxStimCollisionLevel"
exclude:
  # the dead placeholder copies of the DLC vehicles
  - "generated/entitylibrarypatchoverride.fcb/vehicle/land/dlc_vehicle{1,2}_dlc1{,/**}.xml#**"
requires: []
verified: diff
---

# Vehicles take less damage per crash

A single collision can do less damage to a vehicle, which goes with the higher speeds of
`vehicles-faster-land`.

## How

`nMaxStimCollisionLevel` of the physics component, the cap on the damage one collision can deal
([Boggalog's guide: collision damage](../../../docs/docs/modding/guide/vehicles.md#collision-damage)),
20 values:

- `CVehicleWheeledPhysComponent`: `28` -> `23` on the Land Rover and its three gun variants, the
  Datsun, the Jeep Liberty, the big truck and nitrous truck, the quad and the Unimog; `29` -> `23` on
  the Jeep Wrangler; `28` -> `17` on the buggy. The two multiplayer Unimog variants used by patrols
  (`Multi_M249_Mounted`, `Multi_MK19_Mounted`, in the DLC library) go from `2` to `23`.
- `CVehicleFloatingPhysComponent`: `29` -> `25` on the fishing boat and its M249 variant, `28` -> `24`
  on the swamp boat and its three gun variants.

## Uncertain

- The guide reads the value as a cap on damage received; whether it also caps the damage a vehicle
  deals to what it hits is not traced. The multiplayer Unimogs' vanilla `2` would make patrol
  Unimogs nearly crash-proof, so for them the change is a large drop in toughness, not a rise.
