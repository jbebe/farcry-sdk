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
| G3KA4 | [x] | [x] | [x] | JSRS g3 close_1, 1 ms trimmed, curved fade from 75 ms, before the room's reflections at 100-120 ms, at -3.5 dB. NPC: its mono, the same, at -4 dB. Per round from banks `0x00FC0240`/`0x00FC0244`. Echo: big |
| M16 (and persistent) | [x] | [x] | [x] | DS lr300 close_4, 9 ms trimmed, curved fade from 65 ms, before the room's reflection at 80-100 ms, at -3.5 dB. NPC: mono close_4 (not the same recording), trimmed to its crack, same fade, at -5 dB. Per round from banks `0x00FC0250`/`0x00FC0254`. Echo: big |
| MP5 (suppressed; Mike's rusty, persistent) | [x] | [x] | none | DS `_sd` smg9 sd_close_4, 16 ms trimmed; it fires a second round 44 ms later, so faded from 28 ms at 1 dB/ms. NPC: its mono, the same, on the suppressed rolloff. 1P at -6 dB, NPC at -8 dB, a little over retail's loops (-10 dB was too quiet). Per round from banks `0x00FC0260`/`0x00FC0264` |
| Dragunov (AI, Merc, Mike's rusty, persistent) | [x] | [x] | [x] | Retail's own, kept by the user, curved fade from 115 ms, where its crack and blast drop, to lose its baked-in echo. At the original's loudness. Echo: big |
| AS50 (and persistent) | [x] | [x] | [x] | Retail's own, kept by the user, curved fade from 120 ms, where its highs drop, before its boom rings on for 400 ms. At the original's loudness. Echo: big |
| Ithaca 37 | [x] | [x] | [x] | DS mp133 Close_2, curved fade from 165 ms, where the boom drops into the room, at -3.5 dB (the original's -7.5 is 4 dB under the rifles). NPC: mono close_4 (not the same recording), from 160 ms, at -4 dB. Echo: big |
| SPAS-12 (and persistent) | [x] | [x] | [x] | DS spas12 Close_1, curved fade from 110 ms, where the blast drops, before the room rings on until 250 ms, at -3.5 dB. NPC (also the USAS-12's single shot): mono Close_3 (not the same recording), same fade, at -4 dB. Echo: big |
| USAS-12 (and persistent) | [x] | [x] | [x] | JSRS usas12 close_2, curved fade from 60 ms, before the room's bump at 110-160 ms (NPC: its mono). 1P at -4.5 dB, NPC at -5 dB, as the take starts 10.2 dB down. Per round from banks `0x00FC0270`/`0x00FC0274`. Echo: big |
| MGL-140 (and persistent) | [x] | [x] | [x] | JSRS ugl close_1, 28 ms trimmed, curved fade from 75 ms, where the launch drops, before the room's plateau (NPC: its mono, the same). Both at -6 dB, as the take starts 12 dB down (the original's -1.9 would need a 10 dB lift). Echo: small |

## Special

| Weapon | 1P | NPC | Echo | Source |
|---|---|---|---|---|
| M1903 (and Merc) | [~] | [~] | [~] | Retail's own, kept by the user after two rounds of candidates, curved fade from 120 ms, where it drops 6 dB, before its tail rings on 5-9 dB down. At the original's loudness. Echo: big |
| Dart Rifle (suppressed sniper) | [x] | [x] | none | JSRS `_sd` dmr sd_close_4, curved fade from 60 ms, before a reflection at 80 ms (NPC: its mono). Both at -5 dB (the original's -7.3/-6.7 was too quiet) |
| PKM (Mike's rusty, Merc) | [x] | [x] | [x] | JSRS m60 close_1, 24 ms trimmed, curved fade from 65 ms, before the room's reflection at 100 ms (NPC: its mono, the same). 1P at -2 dB, NPC at -3 dB (-3.5/-4.5 was too quiet). Per round from banks `0x00FC0280`/`0x00FC0284`. Echo: big |
| M249 (Merc, persistent) | [~] | [~] | [~] | JSRS m249 close_3, curved fade from 50 ms, before the room's reflection at 80 ms (NPC: its mono). 1P at -2 dB, NPC at -3.5 dB. Per round from banks `0x00FC0290`/`0x00FC0294`. Echo: big |
| M2 | [~] | [~] | [~] | JSRS m82 close_4, 12 ms trimmed, curved fade from 55 ms, where the body drops. NPC: mono close_2 (not the same recording). 1P at -1.5 dB, NPC at -2.5 dB. Per round from banks `0x00FC02A0`/`0x00FC02A4`. Echo: big |
| MK19 | [~] | [~] | [~] | DS ugl close_1 (the M79's take), 83 ms trimmed, curved fade from 165 ms, where the thump drops. NPC: mono close_1, a separate take, 20 ms trimmed to line up, faded from 110 ms. 1P at -2.5 dB, NPC at -3 dB. Per round from banks `0x00FC02B0`/`0x00FC02B4`. Echo: small |
| RPG-7 | [ ] | [ ] | [ ] | |
| Carl Gustaf | [ ] | [ ] | [ ] | |
| LPO-50 flamethrower | [ ] | [ ] | [ ] | |
| Mortar | [ ] | [ ] | [ ] | |

## Mounted

| Weapon | 1P | NPC | Echo | Source |
|---|---|---|---|---|
| M2 | [~] | [~] | [~] | The handheld M2's per-round shots |
| M249 | [~] | [~] | [~] | The handheld M249's per-round shots |
| MK19 | [~] | [~] | [~] | The handheld MK19's per-round shots |

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
