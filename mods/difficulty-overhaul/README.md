# Difficulty Overhaul

Realistic damage and AI shooting for Far Cry 2, scaled by the game's own difficulty setting. An FCSE
plugin that changes `Dunia.dll` in memory, and a data layer with one change to the soldiers' brain.

The goal is lethal combat in both directions where every hit is earned: a bullet that connects is
serious for the player and for a mercenary, and whether it connects depends on range, movement,
visibility, recoil and the time a shooter needs to settle his aim. Everything is tuned on Hardcore;
Easy and Normal will be eased from it and Infamous made harder, so the difficulties differ in how
forgiving the game is rather than in how well the enemy aims. The values are constants in the
plugin, with nothing on the Mod Configuration Menu.

## Features

| Feature | What it does |
|---|---|
| Damage taken | A bullet hits the player by where it lands, as a multiple of the game's damage: head 24, torso 5, arms and legs 3, hands and feet 2. With a rifleman's hits at 20 m and 1,025 health, four torso hits kill and one head hit does. The last health bar no longer quarters the damage. |
| Damage dealt | The player's hits on a soldier use head 6, torso 2.5, arms and legs 1.25, hands and feet 0.75 in place of the game's 6, 1, 1, 0.5, 0.5, 0.5 |
| Going down | A soldier the player's hit in an arm, leg, hand or foot would kill goes down wounded instead, every time: he calls for help, crawls to cover, and may shoot back from the ground. The game rolls for it against the player's reputation, 15 % early in the story, and lets only a few soldiers be down at once. Torso and head hits keep the game's roll |
| Hit model | How the AI's shots at the player land. Aim settles over 0.6 s plus 0.02 s per metre after he acquires the player, where the game misses outright and then hits at full accuracy; the chance is the real probability of a hit, capped at 0.85 so no shot is certain, point blank included; it falls with how well he sees the player, full from a visibility of 0.6, and with how fast the player crosses his aim, as 1 / (1 + the angle crossed per second in radians), so running across his line of fire is harder to hit than running at him; each shot's recoil costs the next up to 60 % of its chance, recovering in about a quarter of a second, in place of the game's forced misses after a hit; and he aims at the head only 15 % of the time, where the game alternates head and torso |
| Fire discipline | A soldier holds his fire at the player until he sees the player clearly (a visibility of 0.4, the game's own "clear" line in combat), rather than spending free ammunition on a player he cannot see |
| Range falloff | With a pistol or SMG a soldier's chance against the player also falls with range, on top of the game's shared rifle curve, halving at 20 and 30 m. A shotgun is lifted 1.15 times above the shared curve out to 20 m, about 0.7 for a settled shot at 15 m, and halves 8 m past that |
| Intuition | After a soldier spots or re-spots his target he keeps knowing where it really is, in sight or not, for 1 s in place of the game's 6, so breaking line of sight breaks his track. This is the soldier's own window, so it applies whoever he is tracking |
| Grass | A player crouched still in high grass stays hidden from a soldier who is fighting: grass takes 80 % of how visible the player is from 10 m out, where the game stops grass hiding the player the moment a soldier is in combat. With fire held until the player is seen clearly, soldiers no longer shoot into grass the player is hiding in |
| Suppression | A soldier the player's bullets hit, land near or pass close to holds his fire at the player for 1 s, and his aim settles again from scratch after it, so return fire keeps a shooter's head down |
| Squad memory | A squad keeps hunting the player at the spot it last saw him for as long as he stays within 15 m of it, where the game forgets him 10 s after last seeing him. Only slipping away unseen lets them lose him |
| Watched spot | A soldier keeps his aim on where the player vanished: a peek from that spot meets a settled rifle, while reappearing more than 0.1 rad away as he sees it (3 m at 30 m) makes him settle his aim again |
| Eyesight | How a soldier sees the player. His focus cone reaches 100 m by day where the game's reaches 60, and keeps the player fully visible out to half its length instead of a quarter, so a man standing in the open at 50 m is seen. His peripheral cone spans 180° instead of 120°, and there the player counts only 0.55 near the focus falling to 0.35 at 90°: enough for him to notice and turn, never enough to see the player as an enemy until he looks. Within 40 m, the share of the player that shows over cover counts double: the game traces three points on his body and counts a third for each it reaches, so a head and shoulders over a wall counted as a third of a man. In the periphery a player standing still or moving crouched counts a fifth as much as one walking upright or faster, so he is noticed for moving upright, not for creeping. A player walking or running upright behind a soldier is noticed within 20 m (as visible as 0.5, before distance and movement) and sensed in full at arm's length, 2 m; one creeping crouched is not. Night, the alert state, crouching, standing still, grass and cover still apply |
| Launchers | A soldier with an RPG or a Carl Gustav fires it at the player only while the player is in a working vehicle. At a player on foot he draws his other gun: his primary, or his pistol or SMG when he has none. He keeps watching: the player getting into a vehicle brings the launcher out, and getting out puts it away. The game hands him the launcher whenever his target is beyond 20 m, so the first time he may raise it, find the player on foot and put it away; after that he remembers. His shots at other soldiers and at buddies are as shipped. This is the layer's `mercbrain.ai.rml` |
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
.\build-layer.ps1            # layer\ from the installed game's merc brain, with the launcher change
```

`layer\` holds game files, so it is built rather than committed: `build-layer.ps1` takes
`mercbrain.ai.rml` out of the installed game, rewires its sniping plan and compiles it with
`jackall-cli`. Install it with `jackall-cli mod build --game "<game>" --layer layer`, together with
any other layers in play.

Needs FCSE with plugin API v8.
