---
name: Far Cry 2 Redux
version: "3.3"
author: Hunter (BigTinz)
url: https://www.nexusmods.com/farcry2/mods/286
archive: Far Cry 2 Redux-286-3-3-1626991981.7z
sha256: 04589d54d5437187cf01ac1305eecc08bce464d8b1619924ec967e5d1e96e121
baseline: steam
analyzed: 2026-10-05
source: the feature lines of the archive's READ ME FIRST.txt (its notice, new controls and changelog), verbatim; the Nexus and ModDB descriptions did not load (HTTP 403)
---

# Far Cry 2 Redux 3.3

Hunter's (BigTinz) overhaul, released 2021-07-22. The readme credits Tom, FoxAhead, Evergreen, Rick,
RaZoR-FIN, Jackal, the Infamous Fusion team, Thirdkeeper and Lasercar. It shares a lineage with Tom's
mods: several material files and the street-sign re-export are byte-identical to Realism Plus's (see
`legacy/realism-plus`).

Redux changes weapons and their handling, enemy and buddy loadouts, patrols and AI odds, the
player's movement, the economy, the map and GPS, graphics settings and the HUD. It adds a holster and
an inspect key, puts the flare gun on the gadget slot, adds three playable women and replaces a
buddy rescue with the cut "Black Mamba" mission. Its engine-side fixes come from a bundled
third-party tool, FoxAhead's Multi-Fixer, not from a patched `Dunia.dll`.

## How it ships

| In the archive | What it is |
|---|---|
| `Far Cry 2 Redux/data_win32/<taxi ride>/<player position>/patch.dat`, `patch.fat` | Four full patch archives of about 51 MB each: Full or Short Taxi Ride, with or without the player's position on the map. |
| `Far Cry 2 Redux/bin/FarCry2MFLauncher.exe`, `FarCry2MF.dll` | FoxAhead's Far Cry 2 Multi-Fixer, the launcher the readme asks players to start the game with ([`engine-multi-fixer`](features/engine-multi-fixer.md)). |
| `READ ME FIRST.txt`, `SouthMapBuddy.jpg` | Install steps, controls, changelog, and a picture of the replaced rescue. |

Only the readme mentions separate Steam and GOG versions. This archive has one set of patches,
built from Steam's patch archive.

### What was analysed

The recommended **Short Taxi Ride / No player position on map** patch, with the Multi-Fixer files.
The other choices change little:

- **Full Taxi Ride:** the same archive without the `domino/user/openingsequence/` script, so the
  opening plays at vanilla length ([`missions-short-taxi-ride`](features/missions-short-taxi-ride.md)).
- **Player position on map:** differs only in the override library. The map keeps its player marker
  and blinking markers, the GPS markers blink, and the map arrow models are blanked. That is
  redundant, because the objectives name no arrows in either variant
  ([`nav-no-player-position`](features/nav-no-player-position.md)).

### Baseline

Steam. Against GOG, whose vanilla patch has no override library, the mod's 3,823 override changes
would collapse into one whole file, and about 4,950 UI and string build differences would appear.

## Bundles

`weapons`, `gameplay`, `fixes`, `controls`, `missions`, `graphics`, `navigation`, `ui`. The
published lines are claimed by the component pages directly, so the bundles carry no claims.

## Coverage

`legacy check` is clean: all 6,359 changes are claimed by exactly one page, and all 28 published
lines are covered. 122 pages: 104 components, 8 bundles, 10 noise rules.

- **The readme gets a few things wrong:**
  - The Black Mamba notice and the changelog name two different rescues, the "Sediko" one and the
    "Dogon Village" one. They are the same mission, A2BU07, whose objective is set in the Dogon
    Sediko ([`missions-black-mamba`](features/missions-black-mamba.md)).
  - The two diamond-reward lines rest on data the engine never reads. Traced in the server build,
    the reward code ignores both edits ([`missions-diamond-rewards`](features/missions-diamond-rewards.md)).
  - "Lowered Ironsight FOV of the scoped rifles" mostly raises the values, which means less zoom
    ([`weapons-scope-fov`](features/weapons-scope-fov.md)).
  - "Reverted default machete" and the boot-instability fix have no change behind them
    ([`graphics-frame-cap`](features/graphics-frame-cap.md), partial).
- **Dead edits:** 1,154 changes edit archetype copies the game never reads, all on
  [`noise-world-dlc-placeholder-copies`](features/noise-world-dlc-placeholder-copies.md). The mod
  even retuned those copies.
- **Blanking by corruption:** several GPS icon models are replaced by a deliberately unparseable
  file, and the women's models by copies with their head materials renamed
  ([`nav-no-gps-safehouses`](features/nav-no-gps-safehouses.md),
  [`player-roster-women`](features/player-roster-women.md)).
- **Already in this repo:** the Multi-Fixer's fixes and options, the mouse cap, the frame cap,
  skip intro and the iron-sight FOV (all in UFCP); the rim light and the night sky (Sky Overhaul).
- **Needs an in-game check:**
  - whether Black Mamba starts the usual way: the HQ doorman's graph still offers A2BU07, and the
    mission's shutdown removes a buddy id nothing sets;
  - the second golden AK-47 cache, built from entities copied with their ids;
  - the enemy sniper pack's `AS50_Merc`, which exists in no library;
  - every page's "Uncertain".

## Published feature list

### Notice and controls

- The south map Sediko rescue mission has been REPLACED with the "Black Mamba" buddy rescue mission due to frequent bugs resulting from the Sediko mission.
- Holster - N
- Inspect - I
- Flare gun - Tap 1 twice
- Map - Tab

### Changelog

- Mod updated to fix the amount of diamonds received from missions.
- Mod updated to include options for full/short taxi ride and player position on the map
- Default Map/Gadget button is now Tab to prevent conflict with holster
- DLC weapons no longer respawn while you're in the armory
- Throwing a grenade no longer holsters your weapon
- Added the Black Mamba buddy rescue mission. This mission now replaces the Dogon Village rescue in ACT 2.
- Removed internal frame rate cap, reverted default machete, and hopefully fixed the random boot instability for the steam version.
- Attempting to fire while holstered will draw the weapon (Thanks for the tip, Tom)
- The player no longer already has an empty bottle of pills at the beginning of the game.
- Faster HUD fade
- Added key rebinds for holster and inspect in the Options menu
- Added a manual holster function
- Added a manual inspection weapon function
- Fixed Uzi visual glitch when crouch walking after aiming
- Enemy flamethrowers have more ammo now
- Wounds/decals stay on screen longer
- Added Flora, Michele, and Nasreen to roster
- Fixed Star .45 run animation
- Auto holster function
- Updated multifixer (Thanks Fox!)
- Changed some diamond rewards
- Lowered Ironsight FOV of the scoped rifles. They should no longer "float".
- Use this program to cap your framerate, skip intro videos, disable blinking, fix bugs, enable bonus missions, and tweak your FOV.
