# Difficulty Overhaul

Realistic damage and AI shooting for Far Cry 2, scaled by the game's own difficulty setting. An FCSE
plugin that changes `Dunia.dll` in memory.

The goal is lethal combat in both directions where every hit is earned: a bullet that connects is
serious for the player and for a mercenary, and whether it connects depends on range, movement,
visibility, recoil and the time a shooter needs to settle his aim. Everything is tuned on Hardcore;
Easy and Normal will be eased from it and Infamous made harder, so the difficulties differ in how
forgiving the game is rather than in how well the enemy aims. The values are constants in the
plugin, with nothing on the Mod Configuration Menu.

## Features

| Feature | What it does |
|---|---|
| Damage taken | A bullet hits the player by where it lands, as a multiple of the game's damage: head 4, torso 2.5, arms and legs 1.5, hands and feet 1. The last health bar no longer quarters the damage. |
| Damage dealt | The player's hits on a soldier use head 6, torso 2.5, arms and legs 1.25, hands and feet 0.75 in place of the game's 6, 1, 1, 0.5, 0.5, 0.5 |
| Hit model | How the AI's shots at the player land. Aim settles over 0.6 s plus 0.02 s per metre after he acquires the player, where the game misses outright and then hits at full accuracy; the chance is the real probability of a hit, capped at 0.85 so no shot is certain, point blank included; he holds his fire until he sees the player clearly (a visibility of 0.4, the game's own "clear" line in combat), rather than spending free ammunition on a player he cannot see; the chance then falls with how well he sees the player, full from a visibility of 0.6; each shot's recoil costs the next up to 60 % of its chance, recovering in about a quarter of a second, in place of the game's forced misses after a hit; and he aims at the head only 15 % of the time, where the game alternates head and torso |
| Intuition | After a soldier spots or re-spots his target he keeps knowing where it really is, in sight or not, for 1 s in place of the game's 6, so breaking line of sight breaks his track. This is the soldier's own window, so it applies whoever he is tracking |
| Hit log | Writes every hit on the player, and every hit the player lands on a soldier, to `bin\fcse.log`: stim type and level, body part, range, health before and after, and whether a soldier was wounded or killed. Every AI shot at the player gets a line too: its chance, the recoil factor, the range and whether it hit |

A hit log line reads:

```
soldier 0001A2F4 hit: pierce level 14, torso, at 31.2 m, health 150 -> 46 (Hardcore, act 1, hit locations on)
player hit: pierce level 9, no body part, at 18.7 m, health 1075 -> 1028 (Hardcore, act 1, hit locations off)
```

## Planned

- **A vest.** A torso hit from a rifle nearly kills the player, which is realistic; a vest is how
  they will survive one. How it is obtained, whether its plates wear, and whether mercs wear them
  too are still open. Until then the player fights without one.

## Build

```
.\build.ps1                  # x86 release -> DifficultyOverhaul.dll
.\build.ps1 -Config debug
.\build.ps1 -Install "<game>\bin"    # copies into bin\plugins\
.\verify_build.ps1           # x86, static CRT, FCSE_Load exported
python ..\..\scripts\verify_patterns.py
```

Needs FCSE with plugin API v8.
