---
name: Far Cry 2 Editor With AI - Enemies and Ally
version: "1.00"
author: Princeton73
url: https://www.nexusmods.com/farcry2/mods/354
archive: Far Cry 2 Editor With AI - Enemies and Ally-354-1-00-1763043267.rar
sha256: 3214eb8b7444cbda110995a02d6d83a27155d3f43a83fe5fe1051378606d94d5
baseline: gog
analyzed: 2026-10-05
source: none - inferred from the changes; the Nexus page did not load (HTTP 403) and has no real description
---

# Far Cry 2 Editor With AI - Enemies and Ally 1.00

Princeton73's map editor mod that puts the campaign's AI into editor maps. Soldiers of both
factions, the buddies, faction leaders and civilians can be placed from the palette. They come alive
when the map is played from the editor, because the editor's game mode is given the single-player AI
services.

It is built on an older community editor mod. The editor title credits Janne252, the custom objects
are named for RaZoR, and the palette and template-world files carry the header comment of the tool
they were saved with, "Far Cry 2 - Multi... Editor v1.3.2.2". Its single `patch.dat` also carries a
set of single-player gameplay, material and visual changes of unknown origin, which act in the
campaign too.

## How it ships

| In the archive | What it is |
|---|---|
| `Far Cry 2/Data_Win32/patch.dat`, `patch.fat` (479 MB, dated 2017-01-03) | A full replacement patch archive. Against GOG's own `patch.dat`, 246 files and 78,342 fragments differ, and everything else is the base game repacked. |

- **Baseline:** the archive was built on the GOG/retail build. Analysed against Steam, it would also
  show the Steam build's own differences, among them every UI package and the Russian strings.
- **One archive, so no merging:** installing it replaces the whole patch archive, so it cannot sit
  beside another `patch.dat` mod.
- **The campaign changes too:** the gameplay, material and visual changes act in a campaign save as
  well as in the editor.
- **Every language becomes English:** the seven translations (cs, fr, de, hu, it, pl, es) are
  replaced by English, rolled back to the 1.00 wording.
- **The RaZoR objects need a separate `dlc2` package:** they live in a `dlc2` entity library that
  this archive ships but does not register. The mod's own strings say "DLC2 required Ingame!".
  Without that package, the 56 palette entries naming them and the three script samples spawn
  nothing ([`editor-razor-library`](features/editor-razor-library.md)).

## Notable mechanisms

- **AI in the editor, through the game-mode config:** `CGameMode::CreateServices` builds each listed
  service from a registered creator and silently skips unknown names. The mod lists about 70 class
  names, real and not, and the eight real single-player services among them take
  ([`ai-editor-mode-services`](features/ai-editor-mode-services.md)).
- **Lua in editor maps:** archetypes carrying a `CDominoComponent` that runs a community script
  under `DLCDomino\Community\RaZoR\`
  ([`scripts-community-samples`](features/scripts-community-samples.md)).
- **AI that sees through walls:** every material's `fTransparency` is 1, and AI line of sight
  multiplies it along the ray, so no surface blocks sight, in the campaign included
  ([`materials-transparency`](features/materials-transparency.md)). This looks like an accident of
  a "set all" edit rather than a feature.

## Bundles

- `editor-war-ai`: Princeton73's AI in the editor
- `editor-scripting`: Domino scripts in editor maps
- `editor-content`: the Janne252/RaZoR editor mod it is built on
- `gameplay`, `materials`, `graphics`: what rides along into the campaign
- `strings`: English everywhere, a side effect

## Coverage

`legacy check` is clean: all 92,401 changes are claimed by exactly one page, and all 7 lines of the
inferred list are covered. 64 pages: 51 components, 1 shared piece, 7 bundles, 5 noise rules.

- **Verified in the engine (`verified: re`):** 4 components. These are the editor mode's services
  (`CGameMode::CreateServices` and the service factory), adaptive behaviour, the routine odds
  (`CHumanPersonality::GetNeedOrder`) and material transparency
  (`CSensorySystemHelpers::ValidateLineOfSightWithTransparency`). All four were traced in the
  symbolized server build.
- **Partial:** 2.
  - `editor-razor-library`: nothing in the archive loads it.
  - `gameplay-reinforcements`: four of its six new archetype names exist in no game file.
- **Large change counts are positional:** 10,186 particle changes are world 2's library copied in,
  paired by position (`editor-particles`). 77,943 are the seven translations overwritten with
  English.
- **Needs an in-game check:** whether placed AI moves at all. The editor has no navmesh, and nothing
  here adds one. Also whether the `dlc2` objects and scripts load with the mod as shipped, and every
  page's "Uncertain".
- **Already in this repo:** nothing.

## Published feature list

There is none. These lines are read from the changes:

### Princeton73's AI

- AI enemies and allies in the map editor: single-player soldiers, buddies, faction leaders and civilians can be placed in a map and come alive in the editor's play-test

### From the editor mod it is built on

- Janne252's map editor mod: hundreds more palette objects (vehicles, buildings, props, signs, rocks, plants, weapons, lights, effects), new terrain textures, roads and vegetation brushes, and the campaign's particles and animations in the editor
- Domino scripts that run inside editor maps: a placed object can spawn pickups and characters, react when a pickup is taken, open a message box and raise the water

### Riding along into the campaign

- Single-player gameplay changes in the shared game configuration: enemy tactics, loadouts and grenades, reinforcements, faction map, AI routines, hit locations, sprint, HUD, muzzle flash, upgrades, shop stats, one syringe, three cut missions
- Surface materials: AI sees through every surface, fire passes through everything without smoke, bullets either pierce a material or not at all
- Visual changes: savannah foliage, bark, decal, smoke, moon and sun-flare textures, long-lasting decals, 16x anisotropic filtering, HDR and bloom at every quality, calmer grass, lower birds, and a brighter sky on editor maps
- English text in every language: seven translations replaced by English, and the English rolled back to the 1.00 wording
