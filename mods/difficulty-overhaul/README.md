# Difficulty Overhaul

Realistic damage and AI shooting for Far Cry 2, scaled by the game's own difficulty setting. An FCSE
plugin that changes `Dunia.dll` in memory, and a data layer of changes to the soldiers' brain, the
guns, the hit locations and the player's health.

The goal is lethal combat in both directions where every hit is earned: a bullet that connects is
serious for the player and for a mercenary, and whether it connects depends on range, movement,
visibility, recoil and the time a shooter needs to settle his aim. Everything is tuned on Hardcore.
The difficulties differ only in how much the player can take and how he heals, never in how well the
enemy aims or how hard his guns hit. The values are constants in the plugin, with nothing on the Mod
Configuration Menu.

## Features

| Feature | What it does |
|---|---|
| Weapon damage | Every gun's bullet is as lethal as its real round, whoever fires it: its impact level is the one whose damage on the game's curve is nearest 7.3 × √(one projectile's muzzle energy in joules), from the round's bullet mass and the gun's own muzzle velocity. Makarov 134, Star .45 168, Uzi, MP5 and MAC-10 185, M16 and M249 300, AK-47, G3 and Desert Eagle 350, FAL, Dragunov, PKM and M1903 399, AS50 and M2 1,000, a buckshot pellet 117, 100 from the DLC shotguns. The soldiers' damage curves are gone, so a soldier's gun hits as hard as the player's, and so is the close-range bonus on every difficulty. Part of the layer, with each gun's round as FCSE entity data any mod may read: `Cartridge.Name`, `Cartridge.BulletMass` in grams, `Cartridge.MuzzleVelocity` in m/s, `Cartridge.Projectiles` and `Cartridge.MuzzleEnergy` in joules for the whole round |
| Blood | A bullet that hits anyone, the player too, throws no spray of blood; a shotgun's pellets still do. Every gun's bullets play the game's impact record, `Weapon.Bullet`, whose flesh particles are blank; the shotguns' play `Weapon.Pellet`, a copy of it that keeps them. The 1 s wound decal stays on both. Part of the layer, as the whole `materialimpacts.xml` |
| Hit locations | The player takes a hit whole wherever it lands, as in the game. Anyone else takes it by where it lands: a head hit kills, torso and legs 1, arms 0.6, hands and feet 0.3, in place of the game's 6, 1, 1, 0.5, 0.5, 0.5. The body part of each hit on the player is in the hit log. Part of the layer, `gamemodesconfig.xml` |
| Health | The player's maximum health on each difficulty is sized in AK-47 hits from full, at the sickness level the campaign starts at. Easy and Normal take as many as in the game, the 37th and the 28th killing, where a soldier's AK took 70 a hit: the game's curves scaled by 4.96, as much as the real round outhits that, to 8,427 and 6,568. Hardcore keeps the game's 1,025, the 3rd hit killing, and Infamous 320, the 1st, scaled by 0.4. A bar is still a fifth of it. Sickness still shrinks it as in the game, to 6,444, 5,081, 825 and 280, keeping the game's own counts on Easy and Normal, 29 and 23 at the worst. Part of the layer, data\health.py |
| Damage taken | The last health bar quarters a hit on Easy and Normal, as in the game, and no longer on Hardcore and Infamous, where a hit on it takes its full damage |
| Healing | On Easy, Normal and Hardcore the game's own refill: 5 s after the last hit, the bar health is in fills to its top, and an emptied bar needs a syrette. On Infamous health never refills. Below one bar the game is unchanged: the last bar bleeds out and the surgery leaves two bars. A syrette, beds and medic cabinets still fill health |
| Going down | A soldier the player's hit in an arm, leg, hand or foot would kill goes down wounded instead, every time: he calls for help, crawls to cover, and may shoot back from the ground. The game rolls for it against the player's reputation, 15 % early in the story, and lets only a few soldiers be down at once. A head or torso hit never downs anyone: it wounds or kills |
| Hit model | How the AI's shots at the player land. Aim settles over 0.6 s plus 0.02 s per metre after he acquires the player, where the game misses outright and then hits at full accuracy; a soldier already alert to him or fighting him settles in 0.3 s, from when he caught sight of the player as well as from his last shot, so his first shot can hit and a peek where the player vanished meets a settled rifle; the chance is the real probability of a hit, capped at 0.85 so no shot is certain, point blank included; it falls with how well he sees the player, full from a visibility of 0.6, and with how fast the player crosses his aim, as 1 / (1 + the angle crossed per second in radians), so running across his line of fire is harder to hit than running at him; each shot's recoil costs the next up to 60 % of its chance, recovering in about a quarter of a second, in place of the game's forced misses after a hit; and he aims at the head only 15 % of the time, where the game alternates head and torso, and never in his first 3 s on the player: first contact, a reacquired sight, a peek from a new spot or a near miss that pins him all start that over, so a first sighting cannot end in a headshot |
| Fire discipline | A soldier holds his fire at the player until he recognises him: his own "clear" line, which perception puts at the distance he recognises the player from. Holding, he stays where he is, aimed, and fires the moment he recognises the player: the shoot task is told his aim is not on the player yet, which keeps it waiting, where refusing the shot would end it and send him elsewhere. A soldier a hit has panicked fires regardless |
| Range falloff | With a pistol or SMG a soldier's chance against the player also falls with range, on top of the game's shared rifle curve, halving at 20 and 30 m. A shotgun is lifted 1.15 times above the shared curve out to 20 m, about 0.7 for a settled shot at 15 m, and halves 8 m past that |
| Intuition | After a soldier spots or re-spots his target he keeps knowing where it really is, in sight or not, for 1 s in place of the game's 6, so breaking line of sight breaks his track. This is the soldier's own window, so it applies whoever he is tracking |
| Suppression | The player's fire shakes a soldier's aim but never stops it. A bullet that lands near him or passes close leaves him 70 % of his chance for 1 s; one that hits him panics him: for 1 s he fires back straight away, however little he sees, at a quarter of his chance |
| Squad memory | A squad keeps hunting the player at the spot it last saw him for as long as he stays within 15 m of it, where the game forgets him 10 s after last seeing him. Only slipping away unseen lets them lose him |
| Watched spot | A soldier keeps his aim on where the player vanished: a peek from that spot meets a settled rifle, while reappearing more than 0.1 rad away as he sees it (3 m at 30 m) makes him settle his aim again |
| Perception | From how far a soldier recognises the player, and from how far he notices something and comes to look, in place of the game's own checks of distance, view, stance and movement. By day, in the open, straight ahead of an idle soldier: walking upright 60 m, moving crouched 50 m, still, standing or crouched, 40 m; he notices from 1.2 times as far, and a player moving in daylight in the open catches his eye from 100 m upright, 70 m crouched. At his side, within his 180°, half. Behind him never, but a player walking upright within 20 m is heard and noticed, and sensed in full at arm's length; crouched, never. Crouched in high grass, half, the grass a soldier's check found counting for half a second more, since the check now and then misses it once; the camo suit hides a still player in grass beyond 3 m from a soldier who is not fighting, read by the plugin, since the game's own camouflage never hides anyone. Cover hides the player only where it hides his head: of the game's three rays to his body, one that reaches him counts as all three. Tall grass on rising ground between them, a crest, hides the player as a rock would, unless his head clears it. The game's rays pass through grass, so the plugin walks the soldier's line to the player's head a metre at a time, leaving out 2 m at each end. Where the line runs within the grass's 1.2 m of the ground, and 0.3 m lower over it than the lower of their heads stands over its own, grass there hides him, by the game's own test. Flat or evenly sloping ground never brings the line that low, so the grass he crouches in is not this. A soldier's walk holds for a quarter of a second and takes a few microseconds. Bushes hide the player as the game counts them, each its species' camouflage less its shot-off leaves, 0.9 for every savannah bush, so one on the line cuts every distance to a tenth and two hide him. But the plugin counts a bush wherever its foliage is, an upright column as wide as its narrower side and as tall as it, rounded at the top and fading over its outer quarter, where the game counts only a small sphere inside it; and on the line to his head, not his torso. A bush is a RealTree whose camouflage reaches lower than 1 m and that stands under 6 m, which leaves out two big diospyros trees and the small palm; trees keep the game's sphere in their crown. In shade by day, half: shade of his head, from anything solid within 100 m of it toward the sun, or a bush's or tree's crown on that line, an upright ellipsoid as wide as its narrower side from the bottom of its camouflage sphere to its top; it fades out with the daylight, and a player in shade no longer catches the eye. It is the player's own, found once a quarter of a second. At night 25 m at most, noticed from 30 m, whatever he does; a muzzle flash no longer lights him up. Lit by a lamp, 1.25 times that, 31 m and 37.5 m: a point or spot light that is on, with his head within its reach and a spot's cone, and nothing solid between them, found while the sun is down. In fog in proportion to how far it lets anyone see, its full distance over how much of the view it takes: in a sandstorm's, the heaviest, 40 m at most, noticed from 45 m; storms trim the farthest sightings and clear weather nothing. Alerted or fighting, every distance 1.25 times as far, and through a sniper scope as much farther again as the game's scope reaches, though no farther than night or fog allow. A soldier at the wheel sees only through his windscreen, 50° about the way the vehicle faces, where the game lets anyone in a vehicle see all around; a gunner or a passenger sees as on foot. Animals, and the player in a vehicle, keep the game's own sight. A soldier not fighting the player notices him at once in his focus, the 60° he looks along, and within 20 m at his side; farther out at his side only once the player has been in his sight for the notice delay, 1 s for every (20 m past that line)², over how far inside his notice distance the player stands, 1 at his feet and 0 at its edge, and at most 2 s: walking upright at 30 m, 1.5 s. Out of his sight it is forgotten over 2 s, so a short dash from cover to cover past his side goes unnoticed. A shot gives the player away: a soldier who hears it, as far as the game carries the gun's report (an AK-47 75 m, a FAL 100 m, a Dragunov, PKM or M1903 150 m, a silenced gun 2.5 to 5 m), recognises him wherever anything of him shows in his view, until he has lost him from view for 3 s. Each first notice and recognition is logged as `perception:`, with the seconds since he first had the player in sight |
| Launchers | A soldier with an RPG or a Carl Gustav fires it at the player only while the player is in a working vehicle. At a player on foot he draws his other gun: his primary, or his pistol or SMG when he has none. He keeps watching: the player getting into a vehicle brings the launcher out, and getting out puts it away. The game hands him the launcher whenever his target is beyond 20 m, so the first time he may raise it, find the player on foot and put it away; after that he remembers. His shots at other soldiers and at buddies are as shipped. Part of the layer's brain |
| Stand-down | An alert ends. 90 seconds into an alert that nothing new has started over, a soldier falls back to where he stood when he was alerted, aiming along the way, and goes back to idle. The game ends an alert only for an idle soldier who looked into something he glimpsed and found nothing, or once the player is inside a safe zone; any other alert, from a gunshot or a fight, kept him searching for good. Part of the layer's brain |
| Combat log | Every hit on the player and every hit the player lands on a soldier, every AI shot at the player, and what each feature did to a soldier, one line each. See [Combat log](#combat-log) |

## Combat log

Each game session writes its own file, `bin\difficulty-overhaul-logs\combat-<date>_<time>.log`, and
the folder keeps the last 20, since `fcse.log` starts over at every launch. Every line also goes to
`fcse.log`. A soldier is named by his entity id, the same in every line about him:

```
soldier 3A802DED shot at player: chance 0.66 (recoil 1.00, sighting 1.00, crossing 1.00, shaken 1.00) at 58.6 m, 2.6 s on him, at the torso, hit; visibility 0.77, assault rifle; the player standing at 0.0 m/s
player hit: pierce level 10, torso, at 58.6 m, health 495 -> 231 of 495, by 3A802DED (Hardcore, act 1, hit locations off)
player died: killed by 3A802DED at 58.6 m
soldier 3A802DED hit: pierce level 8, torso, at 58.0 m, health 250 -> 146 (Hardcore, act 1, hit locations on)
soldier 3A802DED suppressed: hit, he panics at 58.6 m
soldier 7C000123 fire discipline: holds his fire at 30.2 m, visibility 0.12
soldier 7C000123 died, not by the player: stim type 4 level 1000
```

`fight_report.py` reads a session and reports it: totals by range, each death with who fired in the
10 s before it, each soldier from his first shot to his last, and flags for what should not happen,
such as a shot with no chance or a death within 2 s of the killer's first shot:

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
python data\weapons.py world1.xml world2.xml dlc1.xml tmpla.xml gamemodesconfig.xml materialimpacts.xml
python data\health.py world1.xml world2.xml dlc1.xml tmpla.xml
```

`data\weapons.py` writes the guns' fragments, `gamemodesconfig.xml` and `materialimpacts.xml`, and
`data\health.py` the player's health curves, from decoded retail entity libraries and the retail
files; their docstrings say which. The test range's guns and curves come from `tmpla`'s library, which DevTools copies into the
range's own, so `tmpla`'s fragments are written for `worlds\testrange`.

`layer\` is what the release zips: the plugin under `plugins\`, and the brain's changes as fragments
under `mods\scripts\game\newbrains\mercbrain.ai.rml\`, one file per plan or task, which JackAll
merges into the game's own brain at install. `build-layer.ps1` makes them: it takes
`mercbrain.ai.rml` out of the installed game, makes the edits and writes what differs with
`jackall-cli ai fragments`. Beside them are the guns' weapon and weapon-properties fragments and the
health curves, which JackAll merges field by field with any other mod's copies, and the whole
`gamemodesconfig.xml` and `materialimpacts.xml`.
Install with `jackall-cli mod build --game "<game>" --layer layer`,
together with any other layers in play.

Needs FCSE 1.4.0 or later (plugin API v8), and JackAll 1.7.0 or the Vortex extension 0.6.0, or later,
to install the brain's fragments.
