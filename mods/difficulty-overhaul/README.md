# Difficulty Overhaul

Realistic damage and AI shooting for Far Cry 2, scaled by the game's own difficulty setting. An FCSE
plugin that changes `Dunia.dll` in memory.

The goal is lethal combat in both directions where every hit is earned: a bullet that connects is
serious for the player and for a mercenary, and whether it connects depends on range, movement,
visibility, recoil and the time a shooter needs to settle his aim. Hardcore is tuned by hand on the
Mod Configuration Menu; Easy, Normal and Infamous follow it by fixed ratios, and differ in how
forgiving the game is rather than in how well the enemy aims.

## Features

| Row in `[DifficultyOverhaul]` | What it does | Default |
|---|---|---|
| `Hit log` | Writes every hit on the player, and every hit the player lands on a soldier, to `bin\fcse.log`: stim type and level, body part, range, health before and after, and whether a soldier was wounded or killed | on |

The hit log is the measuring tool for tuning. A line reads:

```
soldier 0001A2F4 hit: pierce level 14, torso, at 31.2 m, health 150 -> 46 (Hardcore, act 1, hit locations on)
player hit: pierce level 9, no body part, at 18.7 m, health 1075 -> 1028 (Hardcore, act 1, hit locations off)
```

## Build

```
.\build.ps1                  # x86 release -> DifficultyOverhaul.dll
.\build.ps1 -Config debug
.\build.ps1 -Install "<game>\bin"    # copies into bin\plugins\
.\verify_build.ps1           # x86, static CRT, FCSE_Load exported
```

Needs FCSE with plugin API v8.
