# Difficulty Overhaul

**Before writing or editing any C++, header, or CMake file in this tree, invoke the
`implementation` skill** (`.claude/skills/implementation/SKILL.md`). Held to the same rules as
`mods/UFCP/`, whose `CLAUDE.md` this follows.

## What this is

Realistic damage and AI shooting, scaled by the game's difficulty setting. The design - which
engine levers it uses and how the four difficulties relate - is in `README.md`.

## Rules specific to this tree

- **Scope is combat with the player:** damage, the player's healing, and whatever decides whether
  and how well a soldier fires at the player, which includes what he knows and sees of the player (intuition, grass),
  how long his alert lasts, and the squad's tactics in a fight with him (the user, 2026-11-06).
  Detection that does not bear on a fight belongs elsewhere.
- **Only the player's fights, but for damage.** Hits on the player, hits by the player and shots at
  the player; fights between AIs stay as shipped. The intuition window is the soldier's own, so it
  reaches his other targets too. Damage is realistic for everyone (the user, 2026-11-05): the guns'
  levels and the hit locations are data, written by `data\weapons.py`.
- **Hardcore is the gold standard.** Every value is a Hardcore constant in the plugin, or in
  `data\weapons.py` for the guns; no MCM rows, no sliders, unless the user asks for one. Easy and Normal are eased and Infamous hardened from it
  at the end, by the game's own difficulty (`Game::CurrentDifficulty`).
- **One feature per file** in `src/`, exporting `Install<X>()`; engine seams more than one feature
  reads go in `src/engine/`. Features that need the same engine function each hook it; FCSE stacks
  them.
- **`tools/FCSE/include/fcse_api.h` is the entire dependency.** Never reach into `tools/FCSE/src/`.
- **Address interior sites with `FCSE::Pattern`**, checked with `scripts/verify_patterns.py`;
  `FCSE::Uplay` only for function starts and data the address library maps.
- **A feature that cannot find its site logs it and does nothing.** Every other feature still
  applies.

## Build

```
.\build.ps1                  # x86 release -> DifficultyOverhaul.dll
.\build.ps1 -Install "<game>\bin"    # copies into bin\plugins\
```
