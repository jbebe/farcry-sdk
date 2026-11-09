---
sidebar_position: 15
---

# The mod skeleton

How every mod in `mods/` is laid out, so a release, a mod manager and the next person to open the
folder find each part where they expect it. It is what the released mods have in common, written down;
`/release-check` checks a mod against it (`skeleton_check` in `scripts/modstatus.py`).

```
mods/<mod>/
  README.md                 what the mod is and how to build it, for readers of the repository
  README-release.md         the readme inside the release zip, with no relative links
  CHANGELOG.md              the versions and their notes; the release reads both from here
  STATUS.md                 features, the user's approvals, release order, collision decisions
  .gitignore                the build tree, local material, the built plugin under layer/plugins/
  assets/                   the Nexus page and nothing else
    nexus-summary.txt       plain text, at most 350 characters
    nexus-description.bbcode
    images/                 every picture the page uses
  layer/                    the JackAll layer the release zips as it is
    mods/                   game data, at the paths the game reads it from
    plugins/<mod>/          where build.ps1 stages the DLL, which stays out of git
  CMakeLists.txt            an FCSE plugin only: the 32-bit build
  CMakePresets.json
  build.ps1                 -Config release|debug, and -Install <game bin folder>
  src/
    main.cpp                FCSE_Load
    engine/                 the Dunia.dll seams, apart from the mod's own logic
  tests/                    when the mod has tests
    *.py                    game tests: fc2drive scripts that check the game's state
    *.cpp                   unit tests, only for a plugin with a lot of pure logic
```

Outside the folder, a released mod has its `.github/workflows/<tag prefix>-release.yml` and a row in
`RELEASING.md`'s table.

## What is checked

- The five files at the top. The page text and pictures are the release check's assets row.
- Nothing in `assets/` but the two page files and `images/`. Build inputs, source art and texture
  headers live in a folder of their own beside `src/`, named for what they are: Flashlight's
  `hud-icon/`, Inventory Overhaul's papers in `notebook/`, Sound Overhaul's local `sounds/`.
- A `layer/` for a mod with no plugin. A plugin with no data, like Legacy Plugin, has none.
- For an FCSE plugin: `CMakeLists.txt`, `CMakePresets.json`, `build.ps1` and `src/main.cpp`; and, when
  it has a layer, a `.gitignore` line keeping the built DLL under `layer/plugins/` out of git.
- A release workflow whose `directory` is the mod, and the mod's row in `RELEASING.md`.

## Tests

Most of a mod is hooks into the running game, so its tests are game tests: each `tests/*.py` drives
the game through fc2drive and DevTools' remote control - on the test range, or in world1 or world2
from a save - and reads the outcome off the game's state (`tools/fc2drive/README.md`, "A mod's
tests"). `/release-check` runs them against the mods deployed to the install; CI has no game, so
they never run there.

Unit tests are for the rare plugin with a lot of pure logic. That logic moves into its own files, which
the tests link without the rest:

```cmake
include("${CMAKE_CURRENT_SOURCE_DIR}/../../tools/FCSE/include/fcse_tests.cmake")
fcse_add_tests(ballistics_tests tests/ballistics_tests.cpp SOURCES src/ballistics.cpp)
```

The mod's `build.ps1` then takes `-Tests` and runs ctest after the build, as FCSE's does, and its
release workflow passes `-Tests` so every release runs them.

## What is advice

- `src/engine/` for the code that names engine addresses, so the logic above it reads without them.
- `verify_build.ps1` for a plugin whose build has to be proven 32-bit and static-CRT before release.
- `data/` for the scripts that generate layer data, such as archetype fragments.
- `work/` for local intermediates a script regenerates, gitignored.
- A `CLAUDE.md` when the mod needs instructions of its own for work inside it.
