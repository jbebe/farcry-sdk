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
| Big | `0x00FC0140` | [~] | DS `_gunfire/stereo/rpg` `echo_1` / `tail_1`, loudest 50 ms -18 dB (-10 and -15 were too loud) | rifles, MGs, shotguns, Desert Eagle, RPG-7, Carl Gustaf |
| Small | `0x00FC0120` | [~] | DS `_gunfire/stereo/rpg` `echo_2` / `tail_2`, loudest 50 ms -29 dB (11 dB under big) | Makarov, Star .45, Uzi, MAC-10, M79, MGL-140, MK19 |
| None | | | | 6P9, MP5, Dart Rifle, silenced shotgun, flare gun, crossbow |

Automatics play their echo only once they fire per round, from the user's cut of their fire loop.

## Sidearms

| Weapon | 1P | NPC | Echo | Source |
|---|---|---|---|---|
| Makarov | [x] | [~] | [~] | JSRS pm close_3, curved fade from 128.7 ms (1P) and from 143.3 ms (NPC, mono close_3, a separate take). Echo: small caliber |
| 6P9 (silenced Makarov) | [~] | [~] | none | DS `_sd` 9_2 sd_close_3, curved fade from 94.9 ms (NPC: its mono downmix), at the original's loudness |
| Star .45 | [~] | [~] | [~] | DS 1911 close_1, curved fade from 138.6 ms, 1P at -1.5 dB (the original's -4.1 was weak). NPC: mono close_1, a separate take, curved fade from 152.5 ms, at the original's loudness. Echo: small |
| Desert Eagle | [x] | [~] | [~] | JSRS deagle close_3, curved fade from 140 ms (NPC: mono close_3). 1P at the original's loudness, NPC undriven at -9 dB (-3 dB was very over-amped). DS deagle's takes sounded like a drum kick. Echo: big |
| Uzi | [x] | [x] | [x] | DS bizon close_4b, curved fade from 110 ms (NPC: mono close_4, the closest take, from 115 ms), both at -3 dB, played per round from banks `0x00FC0200`/`0x00FC0204`. Echo: small |
| MAC-10 (and Mike's rusty) | [x] | [x] | [x] | JSRS ump close_2, curved fade from 85 ms (NPC: its mono), both at -3 dB (DS ump close_1 never sounded right, however driven or filtered). Per round from banks `0x00FC0210`/`0x00FC0214`. Echo: small |
| M79 (and Mike's rusty) | [x] | [x] | [x] | DS ugl close_1, 112 ms of mechanism noise trimmed, curved fade from 145 ms (NPC: mono close_1, a separate take, trimmed to its launch and faded from 95 ms). Both at the original's loudness. Echo: small |
| Flare gun | [ ] | [ ] | [ ] | |

## Primary

| Weapon | 1P | NPC | Echo | Source |
|---|---|---|---|---|
| AK-47 (and gold) | [x] | [x] | [x] | DS ak103 close_4, 5 ms trimmed, curved fade from 80 ms, before the room's reflection at 93 ms, at -3.5 dB. NPC: mono close_1 (no mono close_4 is the same recording), trimmed to its shot, same fade, at -3 dB. Per round from banks `0x00FC0220`/`0x00FC0224`. Echo: big |
| FAL (and persistent) | [x] | [x] | [x] | DS scar close_3, 9 ms trimmed, curved fade from 85 ms, before the room's reflection at 90 ms, at -3.5 dB. NPC: mono close_3 (not the same recording), same fade, at -5 dB. Per round from banks `0x00FC0230`/`0x00FC0234`. Echo: big |
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
