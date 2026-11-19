---
sidebar_position: 8
---

# Weather

**Status:** in talk 2026-11-19. Nothing is planned or built. The weather system belongs to
`mods/sky-overhaul` (issue #71); its controls go in DevTools' Environment tab. Marked
**(proposed)** are choices not yet agreed; every number is a first draft.

## The goal

- **Weathers that read as weather**, each with its sub-weathers, all made from one set of levers. A
  new weather is a new row of lever values, not new code.
- **Realistic for the region:** the jungle rains most, the savannah has long dry stretches, the
  desert almost never rains, and storms are seen from far off.
- **Weather follows rules:** morning fog only on a calm morning after a clear night; a dark cirrus
  sheet with a few dark cumulus always ends in a storm.

## Decisions

- **A weather map** (2026-11-19). Shower and storm cells a few kilometres across drift with the
  wind through an air mass that covers the whole world, which is about 5 km across. What is overhead
  is the player's weather, what is far off is seen in the sky, a storm can be watched coming, and
  rain falls under its cell only. Cells form by the zone beneath them, so the jungle is wetter. The
  cloud shader reads a coverage map instead of one number; the clouds are already a noise field in
  world space.
- **No seasons** (2026-11-19). The zone shares below hold all game.
- **Visuals only** (2026-11-19). Weather does not change what guards see or hear, or how cars grip.
- **Wet ground later** (2026-11-19), once rain itself has been seen in play.
- **The sandstorm stays the missions'** (2026-11-19): the retail `SandStorm` presets play only where
  a mission calls them, never in the rotation.
- **The draft rows, numbers and shares stand** (2026-11-19) and are changed in play where they feel
  wrong.
- **The weather is worked out, not saved** (2026-11-19). Every cell's sub-weather, and how far a
  blend has gone, is a function of the engine's storm hour, which saves already, and a fixed seed,
  so a loaded save comes back with the weather it was made in. Two things to check: the storm curve
  loops after 92.66 storm hours, about 15 real hours, so the weather would repeat unless a longer
  curve in the layer lets the hour run on; and whether sleeping moves the storm hour along with the
  clock.

## What the game does now

One axis, the storm factor (0–1), read from a looping curve that is calm 94% of the time; it blends
the `Storm` presets (light, fog, cloud, sky, wind) over the zone's Clear, Jungle or Desert base, and
the desert holds it at zero. Rain is a roll on cloud coverage that has never been seen in play. Wind
is the preset's force times a random 0.8–1.5. Sky Overhaul's storm is thicker clouds, cirrus over the
whole sky and grey light, with the clear fog kept.

What ships unused or barely used: the `Cloudy` light and cloud presets, the `SandStorm` fog, cloud
and wind (Act 2's Border Storm mission only), the rain particles and splashes on the player, and
rain ambience loops per zone (jungle, wetland, savannah). Nothing ships for lightning, thunder or wet
surfaces. Details: [storm, rain and wind](../engine-internals/time-of-day-and-lighting.md#storm-rain-and-wind),
[rain and storms](../cut-and-dormant/vegetation-fire-weather.md#rain-and-storms),
[environment presets](../modding/environment-presets.md).

The game's clock runs at time scale 6: a game hour is 10 real minutes, a game day 4 real hours.

## The levers

| # | Lever | Range | Where it lives | State |
| --- | --- | --- | --- | --- |
| L1 | Cumulus coverage | 0–1 (today 0.44) | Sky Overhaul cloud layer | exists |
| L2 | Cumulus water: how solid and dark | 0.005–0.3 (today 0.021, storm 0.09) | cloud layer | exists |
| L3 | Cumulus height: base and thickness | base 100–4000 m, thickness to 3000 m | cloud layer | exists; a storm tower (to ~10 km) is past today's cap: new |
| L4 | Cumulus size | 500–12000 m | cloud layer | exists |
| L5 | Underside shade | 0–1 (storm 0.8) | cloud layer | exists, storm only |
| L6 | Cirrus reach | 0–1 (today 0.41) | high sheet | exists |
| L7 | Cirrus opacity | 0–1 (today 0.04, storm 0.8) | high sheet | exists |
| L8 | Contrails | 0–1 | high sheet | exists |
| L9 | Air haze | crisp 0.5 – thick 1.8 (today 0.75) | sky model | exists, storm only |
| L10 | Sunlight share | 1–0.2 (storm 0.3) | sky model | exists, storm only |
| L11 | Sky grey | 0–0.85 | sky model | exists, storm only |
| L12 | Engine storm factor: grey world light, storm bloom and god rays | 0–1 | engine; the plugin writes it (the script override sticks at 0.1–0.2) | exists |
| L13 | Cloud shadows, glare cover, light shafts | follow L1 and L7 | Sky Overhaul | exists |
| L14 | Fog distance and amount | end 30–500 m | engine fog setters, written each frame | exists |
| L15 | Fog height: fog lying low in valleys | engine fog's height curves | engine | exists, untested |
| L16 | Fog colour | any | engine; Sky Overhaul retints it already | exists |
| L17 | Heat haze and mirage | 0–1 | Sky Overhaul | exists, gated by sun and storm |
| L18 | Rain on or off | flag | engine `CRainComponent`, the plugin forces it | exists |
| L19 | Rain strength: drizzle to downpour | 0–1 | engine blends 0 to 1; whether a fraction thins the drops needs RE | RE |
| L20 | Rain sound | per zone | engine ambience, follows L18 | exists |
| L21 | Wind force | 0–250 | engine; `SetWindOverride` works in game | exists |
| L22 | Gusts | multiplier | engine fluctuation; a gust front ahead of a storm is new | partly |
| L23 | Wind direction | heading | engine drift; override works | exists |
| L24 | Lightning inside cloud | flash | cloud shader | new |
| L25 | Lightning bolt | drawn line | sky pass, drawn procedurally (no asset) | new |
| L26 | Flash on the world | a few frames | scene light or the grade pass | new |
| L27 | Thunder | sound | new recordings in a data layer, played the way Flashlight plays its click; after the flash by distance, ~3 s per km | new |
| L28 | Sun halo: a 22° ring in a thick cirrus sheet, the classic sign of a coming front **(proposed)** | 0–1 | sky shader | new |
| L29 | Rainbow opposite a low sun as rain clears **(proposed)** | 0–1 | sky shader | new |
| L30 | Wet ground: darker, with a sheen, in and after rain | 0–1 | a screen-space pass | new, later |
| L31 | Dust in the air | — | postponed with the distant dust | postponed |

## The weathers

Cumulus is L1 coverage · L2 water · L3 height. Cirrus is L6 reach · L7 opacity. Fog is L14 end and
L16 colour; "game" means the clear fog as the presets give it (about 400–500 m by day).

| ID | Sub-weather | Cumulus | Shade | Cirrus | Air | Sun | Fog | Heat haze | Rain | Wind | Lightning | Lasts |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| **C** | **Clear** | | | | | | | | | | | |
| C1 | Clear sky | 0 | 0 | 0 | normal | 1 | game | full | – | calm–breeze | – | hours |
| C2 | Light cirrus | 0 | 0 | 0.3 · 0.04 | normal | 1 | game | full | – | calm–breeze | – | hours |
| C3 | Strong cirrus | 0 | 0 | 0.9 · 0.15 | normal | 0.95 | game | full | – | calm–breeze | – | hours |
| C4 | A few cumulus | 0.2 · 0.015 · flat | 0 | 0–0.2 · 0.04 | normal | 1 | game | full | – | breeze | – | hours |
| C5 | Lots of cumulus and cirrus (today's sky) | 0.44 · 0.021 · normal | 0 | 0.41 · 0.04 | normal | 1 | game | full | – | breeze | – | hours |
| C6 | Thick but light cumulus, no cirrus | 0.55 · 0.012 · tall | 0 | 0 | normal | 1 | game | most | – | breeze | – | hours |
| C7 | Hazy and dry **(proposed)** | 0–0.1 · 0.015 · flat | 0 | 0.2 · 0.04 | milky 1.2 | 0.9 | 300 m, warm | strong | – | breeze | – | a day or more |
| **F** | **Foggy** | | | | | | | | | | | |
| F1 | Fog under full cirrus | 0 | 0 | 1 · 0.3, halo | milky 1.2 | 0.7 | 150 m, white-grey | off | – | calm | – | hours |
| F2 | Fog, cumulus and full cirrus | 0.4 · 0.021 · normal | 0.2 | 1 · 0.3 | milky 1.2 | 0.7 | 150 m, white-grey | off | – | calm | – | hours |
| F3 | Storm warning: full dark cirrus, a few dark cumulus | 0.2 · 0.05 · tall | 0.5 | 1 · 0.6, halo | milky 1.4 | 0.5 | 150–250 m, grey | off | – | calm, rising | – | 1–3 h, then always S2 or S3 |
| **M** | **Mist, over any calm weather** | | | | | | | | | | | |
| M1 | Morning fog | as the weather | – | as the weather | – | – | 60–100 m, white, low | off | – | calm only | – | dawn until the sun is ~15° up |
| M2 | Mist after rain **(proposed)** | as the weather | – | as the weather | – | – | 200 m, white | off | – | calm | – | ~1 h after rain |
| **O** | **Overcast and rain (proposed)** | | | | | | | | | | | |
| O1 | Overcast | 0.85 · 0.05 · flat | 0.5 | hidden | milky 1.4 | 0.4 | game | off | – | breeze | – | hours |
| O2 | Drizzle | 0.9 · 0.06 · flat | 0.6 | hidden | milky 1.4 | 0.35 | 300 m, grey | off | drizzle | breeze | – | 1–4 h |
| O3 | Steady rain | 0.95 · 0.08 · flat | 0.7 | hidden | thick 1.6 | 0.3 | 200 m, grey-green | off | rain | windy | – | 2–6 h |
| **S** | **Storm** | | | | | | | | | | | |
| S0 | Distant storm | your sky as C4–C6, plus one dark tower on the horizon | – | – | – | – | game | as the weather | – | as the weather | far flashes, low rumble | 1–2 h |
| S1 | Gathering: cumulus growing tall and dark | 0.5 · 0.06 · towering | 0.6 | 0.8 · 0.4 | 1.4 | 0.6 | game | off | – | gust front | first strikes | 20–40 min |
| S2 | Thunderstorm | 0.65 · 0.09 · towering | 0.8 | 1 · 0.8 | thick 1.8 | 0.3 | game (the storm rule) | off | shower | gusty | some | 30–90 min |
| S3 | Heavy storm | 0.9 · 0.12 · towering | 0.9 | 1 · 0.8 | thick 1.8 | 0.2 | 120 m, blue-grey | off | downpour | gale | frequent | 15–45 min |
| S4 | Clearing | 0.3 · 0.03 · normal, broken | 0.2 | 0.5 · 0.1 | crisp 0.5 | 1 | game, then M2 | off until dry | – | easing | – | 1–2 h |
| **W** | **Wind and dust (proposed)** | | | | | | | | | | | |
| W1 | Breezy | any C row, faster drift | | | | | | | – | windy | – | hours |
| W2 | Windy and hazy | 0–0.1 | 0 | 0.2 | milky 1.3, warm | 0.85 | 250 m, ochre | weak | – | windy–gale | – | hours |
| W3 | Sandstorm, missions only | the retail `SandStorm` presets | | | | | 30 m, ochre | off | – | gale | – | as the mission holds it |

Every weather also runs at night: the clouds take the moon's light as they do now, and lightning reads
best in the dark.

## The rules

1. **Blends, not cuts.** Moving between rows eases every lever over 20–60 game minutes (3–6 real
   minutes); a storm front comes faster, 10–15 game minutes.
2. **Two roads to a storm (proposed).**
   - *Afternoon storm*, the tropical one and the most common: a clear morning (C1–C4), cumulus
     building through the day (C4, C5, C6, S1), a thunderstorm between 14:00 and 18:00 (S2, sometimes
     S3), clearing by evening (S4), a clear night, morning fog at dawn if it stays calm.
   - *Front*, at any hour: the warning sky F3 always leads to S2 or S3 within 1–3 game hours, and F3
     never shows without a storm after it. The full dark cirrus is the storm's own anvil spreading
     ahead of it.
3. **Morning fog** forms only after a clear or clearing night with calm wind, is thickest at dawn
   and burns off as the sun climbs. Wind above a breeze stops it. Common in the jungle and by water,
   rare on the savannah, never in the desert.
4. **Wet after rain:** no heat haze or mirage until the ground has had 1–2 game hours of sun; the air
   is crisp; the jungle steams (M2).
5. **Heat haze** follows the sun, less what cloud stands in front of it, and only on dry ground.
6. **Rain** only under heavy cumulus or overcast. None in the desert (the engine already blocks rain
   at desert 0.6 and above).
7. **Lightning** only in S0–S3. Thunder comes after its flash by distance.
8. **Wind:** calm before the storm (F3), a gust front as it breaks (S1 to S2), strongest in S3,
   easing in S4.
9. **Storm fog stays clear** in S1 and S2, as the storm rule has it; only the heavy storm, the
   rain rows and the fog rows thicken and tint the fog.
10. **Missions win.** While a retail script holds a weather override (Border Storm's sandstorm,
    `a3sm15`'s storm), the system follows it and resumes after.

## How often: the zones

Shares of game time per weather family, a first draft, the same all game. The zone is the engine's
own savannah, jungle and desert weighting under each cell of the map.

| | Jungle | Savannah | Desert |
| --- | --- | --- | --- |
| Clear (C) | 35% | 55% | 65% |
| Foggy (F1, F2) | 10% | 5% | 0 |
| Overcast and rain (O) | 25% | 10% | 2%, overcast only |
| Storm (F3, S1–S4) | 25% | 15% | 3%, seen from afar (S0) |
| Wind and dust (W) | 5% | 15% | 30% |
| A calm morning has fog (M1) | 70% | 30% | 0 |
| A day has an afternoon storm | ~50% | ~25% | under 5% |

## What is new to build

- **Rewired:** every sky lever that follows the storm factor today follows the weather instead.
- **Engine, from the plugin:** the storm factor, the fog setters, the wind override and the rain
  flag; the engine's own storm curve and rain roll kept quiet. Needs RE: whether a rain strength
  below 1 thins the drops, and what the fog's height curves do.
- **New:** the weather map and its scheduler; cumulus towers taller than 3 km; lightning
  in cloud, bolts and the flash on the world; thunder recordings and their playback; the DevTools
  Environment tab picking and forcing a weather and showing what comes next.
