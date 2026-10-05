---
title: Weapons usable inside armories
kind: component
bundle: gameplay
claims:
  - "You can now use weapons while inside armories"
status: located
systems: [weapons, player]
match:
  - "levels/*/generated/worldsectors/worldsector*.data.fcb/*#Components/CDoor/bPullToWeaponSafe"
exclude: []
requires: []
verified: diff
---

# Weapons usable inside armories

Walking through the door of a weapons shop's armory (the storage room where bought weapons are
picked up) no longer puts the player into weapon-safe mode, so the equipped weapon stays drawn and
usable inside.

## How

`CDoor/bPullToWeaponSafe` goes `True` -> `False` on the ten armory doors, five per world, all
`InteractiveDoors.Industrial_AnimatedDoor01_12` / `_19` placed entities:

- world 1: `2056078781921102730` (w1_b_2), `2056079362851106934` (w1_b_3), `2056096498417952504`
  (w1_c_3), `2057865182842991732` (w1_c_4), `2056068481673077472` (w1_d_2)
- world 2: `2056094098445579865` (w2_b_2), `2056093316912528342` (w2_b_4), `2056706351205211164`
  (w2_c_3), `2056770221496097031` (w2_d_2), `2056094142112480912` (w2_d_4)

No other door in the game changes. The same ten door ids are the ones the armory respawn cooldown
script names, which is how they are identified as the armory doors.
