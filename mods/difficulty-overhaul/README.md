# Difficulty Overhaul

Realistic damage and AI shooting for Far Cry 2, scaled by the game's own difficulty setting. An FCSE
plugin that changes `Dunia.dll` in memory, and a data layer of changes to the soldiers' brain.

The goal is lethal combat in both directions where every hit is earned: a bullet that connects is
serious for the player and for a mercenary, and whether it connects depends on range, movement,
visibility, recoil and the time a shooter needs to settle his aim. Everything is tuned on Hardcore,
and every difficulty plays at those values for now; Easy and Normal will be eased from it and
Infamous made harder, so the difficulties differ in how forgiving the game is rather than in how well
the enemy aims. The values are constants in the plugin, with nothing on the Mod Configuration Menu.

## Features

| Feature | What it does |
|---|---|
| Damage taken | A bullet hits the player by where it lands, as a multiple of the game's damage: head 24, torso 5, arms and legs 3, hands and feet 2. With a rifleman's hits at 20 m and 1,025 health, four torso hits kill and one head hit does. The last health bar no longer quarters the damage. |
| Healing | The heal key's surgery on the last bar leaves the game's two bars, 40 % of maximum health: on Hardcore one rifleman's torso hit from death. A syrette leaves 65 %, two hits from death, where the game fills health to full, and is refused at 65 % or above. Below 65 % nothing regenerates. Above it health climbs to full in 3 s on Casual, 10 s on Experienced, 6 minutes on Hardcore, starting 5 s after the last hit, and never on Infamous, where the game refills only the bar health is in, within a second. Sleeping in a bed and medic cabinets still heal to full. Each heal is logged as `healed:` |
| Damage dealt | The player's hits on a soldier use head 6, torso 2.5, arms and legs 1.25, hands and feet 0.75 in place of the game's 6, 1, 1, 0.5, 0.5, 0.5 |
| Going down | A soldier the player's hit in an arm, leg, hand or foot would kill goes down wounded instead, every time: he calls for help, crawls to cover, and may shoot back from the ground. The game rolls for it against the player's reputation, 15 % early in the story, and lets only a few soldiers be down at once. Torso and head hits keep the game's roll |
| Hit model | How the AI's shots at the player land. Aim settles over 0.6 s plus 0.02 s per metre after he acquires the player, where the game misses outright and then hits at full accuracy; the chance is the real probability of a hit, capped at 0.85 so no shot is certain, point blank included; it falls with how well he sees the player, full from a visibility of 0.6, and with how fast the player crosses his aim, as 1 / (1 + the angle crossed per second in radians), so running across his line of fire is harder to hit than running at him; each shot's recoil costs the next up to 60 % of its chance, recovering in about a quarter of a second, in place of the game's forced misses after a hit; and he aims at the head only 15 % of the time, where the game alternates head and torso, and never in his first 3 s on the player: first contact, a reacquired sight, a peek from a new spot or a near miss that pins him all start that over, so a first sighting cannot end in a headshot |
| Fire discipline | A soldier holds his fire at the player until he recognises him: his own "clear" line, which perception puts at the distance he recognises the player from. Holding, he stays where he is, aimed, and fires the moment he recognises the player: the shoot task is told his aim is not on the player yet, which keeps it waiting, where refusing the shot would end it and send him elsewhere. A soldier a hit has panicked fires regardless |
| Reaction | A soldier who spots the player before he has fired in the fight fires as fast as he can within 10 m, and beyond that holds his fire 0.075 s per metre while he makes up his mind: 3 s at 50 m. The game starts a soldier's aim timer at his first shot, so a timer that has not started marks one who has not fired yet. A hit that panics him ends the hold; once he has fired, nothing holds him |
| Range falloff | With a pistol or SMG a soldier's chance against the player also falls with range, on top of the game's shared rifle curve, halving at 20 and 30 m. A shotgun is lifted 1.15 times above the shared curve out to 20 m, about 0.7 for a settled shot at 15 m, and halves 8 m past that |
| Intuition | After a soldier spots or re-spots his target he keeps knowing where it really is, in sight or not, for 1 s in place of the game's 6, so breaking line of sight breaks his track. This is the soldier's own window, so it applies whoever he is tracking |
| Suppression | The player's fire shakes a soldier's aim but never stops it. A bullet that lands near him or passes close leaves him 70 % of his chance for 1 s; one that hits him panics him: for 1 s he fires back straight away, however little he sees, at a quarter of his chance |
| Squad memory | A squad keeps hunting the player at the spot it last saw him for as long as he stays within 15 m of it, where the game forgets him 10 s after last seeing him. Only slipping away unseen lets them lose him |
| Watched spot | A soldier keeps his aim on where the player vanished: a peek from that spot meets a settled rifle, while reappearing more than 0.1 rad away as he sees it (3 m at 30 m) makes him settle his aim again |
| Perception | From how far a soldier recognises the player, and from how far he notices something and comes to look, in place of the game's own checks of distance, view, stance and movement. By day, in the open, straight ahead of an idle soldier: walking upright 60 m, moving crouched 50 m, still, standing or crouched, 40 m; he notices from 1.2 times as far, and a player moving in daylight in the open catches his eye from 100 m upright, 70 m crouched. At his side, within his 180°, half. Behind him never, but a player walking upright within 20 m is heard and noticed, and sensed in full at arm's length; crouched, never. Crouched in high grass, half, the grass a soldier's check found counting for half a second more, since the check now and then misses it once; the camo suit hides a still player in grass beyond 3 m from a soldier who is not fighting, read by the plugin, since the game's own camouflage never hides anyone. Cover hides the player only where it hides his head: of the game's three rays to his body, one that reaches him counts as all three. At night 25 m at most, noticed from 30 m, whatever he does; a muzzle flash no longer lights him up. Alerted or fighting, every distance 1.25 times as far, and through a sniper scope as much farther again as the game's scope reaches. Animals, and the player in a vehicle, keep the game's own sight. Each first notice and recognition is logged as `perception:` |
| Launchers | A soldier with an RPG or a Carl Gustav fires it at the player only while the player is in a working vehicle. At a player on foot he draws his other gun: his primary, or his pistol or SMG when he has none. He keeps watching: the player getting into a vehicle brings the launcher out, and getting out puts it away. The game hands him the launcher whenever his target is beyond 20 m, so the first time he may raise it, find the player on foot and put it away; after that he remembers. His shots at other soldiers and at buddies are as shipped. Part of the layer's brain |
| Stand-down | An alert ends. 2 minutes into an alert that nothing new has started over, a soldier falls back to where he stood when he was alerted, aiming along the way, and goes back to idle. The game ends an alert only for an idle soldier who looked into something he glimpsed and found nothing, or once the player is inside a safe zone; any other alert, from a gunshot or a fight, kept him searching for good. Part of the layer's brain |
| Combat log | Every hit on the player and every hit the player lands on a soldier, every AI shot at the player, and what each feature did to a soldier, one line each. See [Combat log](#combat-log) |

## Combat log

Each game session writes its own file, `bin\difficulty-overhaul-logs\combat-<date>_<time>.log`, and
the folder keeps the last 20, since `fcse.log` starts over at every launch. Every line also goes to
`fcse.log`. A soldier is named by his entity id, the same in every line about him:

```
soldier 3A802DED reaction: spotted the player at 58.6 m, holds his fire 2.5 s
soldier 3A802DED shot at player: chance 0.66 (recoil 1.00, sighting 1.00, crossing 1.00, shaken 1.00) at 58.6 m, 2.6 s on him, at the torso, hit; visibility 0.77, assault rifle; the player standing at 0.0 m/s
player hit: pierce level 10, torso, at 58.6 m, health 495 -> 231 of 495, by 3A802DED (Hardcore, act 1, hit locations off)
player died: killed by 3A802DED at 58.6 m
soldier 3A802DED hit: pierce level 8, torso, at 58.0 m, health 250 -> 146 (Hardcore, act 1, hit locations on)
soldier 3A802DED suppressed: hit, he panics at 58.6 m
soldier 7C000123 fire discipline: holds his fire at 30.2 m, visibility 0.12
soldier 7C000123 died, not by the player: stim type 4 level 1000
```

`fight_report.py` reads a session and reports it: totals by range, each death with who fired in the
10 s before it, each soldier from first sighting to last shot, and flags for what should not happen,
such as a shot inside a reaction hold or a death within 2 s of the killer's first shot:

```
python fight_report.py                          the newest session
python fight_report.py <file>                   a session file, or an fcse.log
python fight_report.py --at 19:30:43            the lines in the 10 s before that time
python fight_report.py --soldier 3A802DED       every line about one soldier
```

Launched with `-perceptiontrace`, the game also logs every soldier's check of the player four times a
second: how far, where in his view, what hides the player, and the distances he notices and
recognises from. fc2drive's perception scenario reads it to know where the soldier is looking.

## Planned

- **A vest.** A torso hit from a rifle nearly kills the player, which is realistic; a vest is how
  they will survive one. How it is obtained, whether its plates wear, and whether mercs wear them
  too are still open. Until then the player fights without one.

## Build

```
.\build.ps1                  # x86 release -> DifficultyOverhaul.dll, staged into layer\plugins\
.\build.ps1 -Config debug
.\build.ps1 -Install "<game>\bin"    # copies into bin\plugins\
.\verify_build.ps1           # x86, static CRT, FCSE_Load exported
python ..\..\scripts\verify_patterns.py
.\build-layer.ps1            # the brain's fragments in layer\, from the installed game's merc brain
```

`layer\` is what the release zips: the plugin under `plugins\`, and the brain's changes as fragments
under `mods\scripts\game\newbrains\mercbrain.ai.rml\`, one file per plan or task, which JackAll
merges into the game's own brain at install. `build-layer.ps1` makes them: it takes
`mercbrain.ai.rml` out of the installed game, makes the edits and writes what differs with
`jackall-cli ai fragments`. Install with `jackall-cli mod build --game "<game>" --layer layer`,
together with any other layers in play.

Needs FCSE 1.4.0 or later (plugin API v8), and JackAll 1.7.0 or the Vortex extension 0.6.0, or later,
to install the brain's fragments.
