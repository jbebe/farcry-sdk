---
title: Wider look-around on swamp boats
kind: component
bundle: gameplay
status: located
systems: [vehicles, player]
match:
  - "generated/entitylibrarypatchoverride.fcb/vehicle/sea/swampboat{,/**}.xml#**/vehicleMaxLookAngle/*"
exclude: []
requires: []
verified: diff
---

# Wider look-around on swamp boats

Driving a swamp boat, the player can turn their head 150 degrees to either side instead of 90.

## How

`CVehicle/vehicleMaxLookAngle/z` (the horizontal limit) `90` -> `150` on `Sea.SwampBoat` and its
`M2_Mounted`, `M249_Mounted` and `MK19_Mounted` variants, copies the mod adds to
`generated/entitylibrarypatchoverride.fcb`. Other vehicles allow about 170 already; Boggalog's guide
notes the swamp boats as the one reduced case
([vehicles: max look angle](../../../docs/docs/modding/guide/vehicles.md#max-look-angle)), and the
engine clamps the driver's free look by this value
([free camera](../../../docs/docs/engine-internals/free-camera-and-noclip.md)).
