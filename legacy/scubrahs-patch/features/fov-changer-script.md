---
title: External FOV changer script
kind: component
bundle: gameplay
claims:
  - "Added external FOV changer script (in Far Cry 2 folder)"
status: located
systems: [player, vehicles]
match:
  - "install/far cry 2 fov changer.vbs"
exclude: []
requires: []
existing: mods/UFCP — src/options/fov.cpp (the same two fields of view, set at run time)
verified: diff
---

# External FOV changer script

A script beside the game that asks for a field of view (75-110) and writes it into the installed
`patch.dat`, for both the on-foot camera and every vehicle.

## How

`Far Cry 2 FOV Changer.vbs` (loose, installed in the game folder) runs under Windows Script Host:

1. Asks for a whole number from 75 to 110 and looks it up in its own table of the matching 32-bit
   floats.
2. Reads `Data_Win32\patch.dat` whole as text.
3. Camera: after each occurrence of the archetype name `cameras.Camera.First`, finds the next
   `BA 21 F7 BE 04` (a field hash and a 4-byte length; the camera's `fFOV`) and replaces the 4
   bytes after it.
4. Vehicles: finds every `80 54 74 49 04` (the hash of `fFOVAngle` and its length) and replaces the
   following float with the new value, for every distinct old value it meets.
5. Writes `patch.dat` back in place.

It only works because the mod's `patch.dat` stores the entity-library archives uncompressed, as its
header comment says, and because the mod's own `cameras.Camera.First` copy (`default-fov-95`) puts
a camera FOV there to find. Vehicles get the same number as the camera.

## Depends on

`default-fov-95` (the camera archetype it edits) and the mod's `patch.dat` layout. A `patch.dat`
whose entity libraries are compressed, or without that camera archetype, makes it report "Couldn't
find expected data".
