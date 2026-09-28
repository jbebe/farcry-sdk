# Weapon sound checklist

Every single-player gun and device, and which of its sounds have been replaced. A sound is done once it is
picked in Audacity, cut, imported and heard in game.

- **1P**: the shot the player hears from their own weapon (stereo)
- **NPC**: the shot heard from someone else's weapon (mono)
- **Echo**: the tail after the shot

`[~]` is built and deployed, waiting to be heard; `[x]` is heard and kept.

Sources: Dark Signal (`DS`), JSRS (`JSRS`). Retail's own first-person event plays an environment tail
(`0x004565A6` for single shots, `0x004B291E` on release for automatics) beside the gun's sound; replacing only
the gun's audio keeps it.

## Echoes

Built by `build/echoes.py`. Each tier's bank holds the player's echo at the tier's id (the near echo, stereo,
unpositioned: a positioned event plays nothing through a first-person sound type) and NPCs' at the id +
`0x10` (a mono near echo and a mono far tail, positioned, crossing over at 40 m, the near gone by 80 m),
which the plugin plays for NPC shots.

| Tier | Id | Built | Near / far | Weapons |
|---|---|---|---|---|
| Big | `0x00FC0140` | [~] | DS `_gunfire/stereo/rpg` `echo_1` / `tail_1`, loudest 50 ms -10 dB | rifles, MGs, shotguns, Desert Eagle, RPG-7, Carl Gustaf |
| Small | `0x00FC0120` | [~] | DS `_gunfire/stereo/rpg` `echo_2` / `tail_2`, loudest 50 ms -18 dB (8 dB under big) | Makarov, Star .45, Uzi, MAC-10, M79, MGL-140, MK19 |
| None | | | | 6P9, MP5, Dart Rifle, silenced shotgun, flare gun, crossbow |

Automatics play their echo only once they fire per round, from the user's cut of their fire loop.

## Sidearms

| Weapon | 1P | NPC | Echo | Source |
|---|---|---|---|---|
| Makarov | [x] | [x] | [~] | 1P: JSRS pm close_3, cut 128.7 ms. NPC: DS mono pm close_1, cut 185 ms. Echo: small caliber |
| 6P9 (silenced Makarov) | [~] | [~] | none | DS `_sd` 9_2 sd_close_3, cut 94.9 ms, at the original's loudness |
| Star .45 | [ ] | [ ] | [ ] | |
| Desert Eagle | [ ] | [ ] | [ ] | |
| Uzi | [ ] | [ ] | [ ] | |
| MAC-10 | [ ] | [ ] | [ ] | |
| M79 | [ ] | [ ] | [ ] | |
| Flare gun | [ ] | [ ] | [ ] | |

## Primary

| Weapon | 1P | NPC | Echo | Source |
|---|---|---|---|---|
| AK-47 (and gold) | [ ] | [ ] | [ ] | |
| FAL | [ ] | [ ] | [ ] | |
| G3KA4 | [ ] | [ ] | [ ] | |
| M16 | [ ] | [ ] | [ ] | |
| MP5 (suppressed) | [ ] | [ ] | [ ] | |
| Dragunov | [ ] | [ ] | [ ] | |
| AS50 | [ ] | [ ] | [ ] | |
| Ithaca 37 | [ ] | [ ] | [ ] | |
| SPAS-12 | [ ] | [ ] | [ ] | |
| USAS-12 | [ ] | [ ] | [ ] | |
| MGL-140 | [ ] | [ ] | [ ] | |

## Special

| Weapon | 1P | NPC | Echo | Source |
|---|---|---|---|---|
| M1903 | [ ] | [ ] | [ ] | |
| Dart Rifle | [ ] | [ ] | [ ] | |
| PKM | [ ] | [ ] | [ ] | |
| M249 | [ ] | [ ] | [ ] | |
| M2 | [ ] | [ ] | [ ] | |
| MK19 | [ ] | [ ] | [ ] | |
| RPG-7 | [ ] | [ ] | [ ] | |
| Carl Gustaf | [ ] | [ ] | [ ] | |
| LPO-50 flamethrower | [ ] | [ ] | [ ] | |
| Mortar | [ ] | [ ] | [ ] | |

## Mounted

| Weapon | 1P | NPC | Echo | Source |
|---|---|---|---|---|
| M2 | [ ] | [ ] | [ ] | |
| M249 | [ ] | [ ] | [ ] | |
| MK19 | [ ] | [ ] | [ ] | |

## DLC

| Weapon | 1P | NPC | Echo | Source |
|---|---|---|---|---|
| Crossbow | [ ] | [ ] | [ ] | |
| Sawed-off shotgun | [ ] | [ ] | [ ] | |
| Silenced shotgun | [ ] | [ ] | [ ] | |

## Explosions and thrown

| Device | Sound | Source |
|---|---|---|
| M67 grenade (also M79 and MGL-140 rounds) | [ ] | |
| Molotov | [ ] | |
| IED | [ ] | |
| Rocket (RPG-7, Carl Gustaf) | [ ] | |
| Mortar shell | [ ] | |
