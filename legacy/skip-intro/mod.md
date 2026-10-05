---
name: Skip Intro
version: "1.1"
author: scubrah
url: https://www.nexusmods.com/farcry2/mods/320
archive: skip-intro-mod.zip
sha256: 6da307f20ef4ba535af53c9fc8ae13a52042d20e3dee3df6e707262eaf20fe53
baseline: steam
analyzed: 2026-10-05
source: none - inferred from the changes; the archive has no readme and the Nexus page did not load (HTTP 403)
---

# Skip Intro 1.1

scubrah's Skip Intro: a new game starts at the hotel in Pala with the town escape mission, without
the taxi ride, and the Jackal's briefing in the hotel room is cut.

This archive is not the Nexus release itself. It holds that release's two Domino scripts as loose
files, byte for byte the ones inside `FC2 Skip Intro-320-1-1-1651917084.zip` (also in `tmp/mods/`).
The Nexus release ships them in a full `patch.dat`, which also raises `MouseFilter` `maxOutput` from
10 to 100 in five action maps of `config/inputactionmapcommon.xml`. This archive leaves those edits
out. Who repackaged it is not recorded.

Scubrah later folded the same edits into Scubrah's Patch as an optional skip behind a yes/no box
(see [`skippable-intro`](../scubrahs-patch/features/skippable-intro.md)). Here the skip is
unconditional.

The skip happens when a new game starts, so it does nothing for a save that is already past the
opening.

## How it ships

| In the archive | What it is |
|---|---|
| `Data_Win32/domino/user/master_world1.world1.lua` | The world 1 master graph, with the new-game branch rewired |
| `Data_Win32/domino/user/a1sm01_townescape.a1sm01_mission.lua` | The town escape mission, with the hotel scene cut short |

Both are loose files. The engine has no loose-file override, so as shipped they do nothing: they
have to be packed into `patch.dat` (`jackall-cli mod build`, or `legacy pick` from this analysis).

## Bundles

- `skip-intro` - the whole mod: the three components below

## Coverage

`legacy check` is clean: all 6 changes are claimed by exactly one page, and all 3 lines of the
inferred list are covered. 4 pages: 3 components, 1 bundle. Nothing is unresolved. None of it is in
this repo already. UFCP's skip intro skips the logo movies before the menu, not the opening
sequence.

## Published feature list

There is none. These are the features read from the changes:

- Skip the opening sequence: a new game starts the town escape mission at the hotel, without the taxi ride
- Skip the Jackal's briefing in the hotel room
- Shorter wake-up in the hotel after the briefing
