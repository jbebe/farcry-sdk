---
sidebar_position: 5
---

# Difficulty

**Status:** in progress since 2026-10-21, built as `mods/difficulty-overhaul`. Seventeen features are
built; the [finish checklist](#finish-checklist) re-checks each on the test range and lists what is
left. How enemies should differ from one another is [not agreed](#open-how-enemies-differ).

## The goal

Combat that is realistic and fair:

- **Lethal both ways.** A hit is serious for the player and for a mercenary, and whether it lands
  depends on range, movement, visibility, recoil and the time a shooter needs to settle his aim.
- **Difficulty is forgiveness, not aim.** The settings differ in how much the game forgives the player,
  never in superhuman enemy accuracy.
- **The player is an experienced soldier from start to finish.** The story's progress must not make
  the game harder. The opposite of what this wants is a boss with ten times the last boss's health.

## Decisions

- **Hardcore is the gold standard.** Everything is tuned on Hardcore. Casual and Experienced are eased
  from it, and Infamous hardened, only once the mod is done.
- **Values are constants in the plugin.** No Mod Configuration Menu rows and no sliders, unless one is
  asked for.
- **Where the mod covers a lever, the game's own per-difficulty spread goes**, so the mod is the one
  source of difficulty.
- **Only the player's fights change,** but for damage. Since 2026-11-05 the guns and the hit locations
  are realistic for everyone: "The game has to be realistic, not the player-vs-world scenario."
- **The player gets fair treatment, the enemy realistic treatment** (2026-11-05): "player gets fair
  treatment against a horde of enemies, the enemy gets realistic treatment". A soldier drops as a
  man would; the player's health system is to be reworked. On 2026-11-05 the mod's healing and its
  removal of the last-bar cushion were scrapped, so the rework starts from the game's own: "the
  current approach is not well thought out".
- **Difficulty is how much the player can take** (2026-11-05): "we need hardcore to make you barely
  alive, infamous impossible, normal normal and easy easy." The guns hit as hard on every setting;
  the player's health is sized in AK-47 hits from full, the 8th killing on Easy, the 5th on Normal,
  the 3rd on Hardcore and the 1st on Infamous; the last bar's cushion stays on Easy and Normal only;
  and regeneration is quicker the easier the setting, never on Infamous ("its still hard for easy").
  On 2026-11-06 regeneration went: "regen totally renders useless the syringes / we need bigger
  health pool instead of regen, theres no other solution". Easy and Normal take as many AK-47 hits as
  in the game, 37 and 28, calculated from the game's own values; Easy, Normal and Hardcore heal with
  the game's own refill of the bar; Infamous still never heals but by syrette. The pools follow the
  AK alone, so against the game's counts there a G3 kills in 0.75 times the hits, a FAL or PKM in
  0.95, an M16 or MP5 in about 1.15, pistols in 1.25 to 1.8, and a Dragunov, an M1903 or a shotgun
  in 3 to 4.
- **Fire discipline and cover define the mod.** A soldier holds his fire until he can make the player
  out. Seen in game: "The bush, the tall grass actually makes them stop shooting. the sniper has no
  LoS and waits for me to peek."
- **Grass is not camouflage.** "crouching in grass is NOT camouflage. behind a bush or a tree is, but
  simple grass is not" (2026-11-02). Since the [perception model](#perception), grass halves the
  distances and the camo suit hides a still player in grass beyond 3 m.
- **Perception is one model, the user's** (2026-11-03): from how far a soldier notices and
  recognises the player, fitted to the user's own answers for 17 situations.
- **In front of him he sees you** (2026-11-06): the notice delay is for slipping between covers
  where he is not looking, so it runs only at his side, at most 2 s, and he no longer holds his fire
  on first contact: "so they react fast on the first notice".
- **A fight is a fight** (2026-11-06): "enemies don't shoot even though they are already in alert
  mode and already know where I am", and "I counted 5-6 that had LOS to me and yet they didnt shoot
  even though I already killed one of their mates". A soldier already alert to the player settles
  his aim in 0.3 s and his first shot can hit; a shot the player fires gives him away to every
  soldier who hears it and can see any of him. A soldier fighting him fires at a glimpse, at his
  notice line (2026-11-06, "lets do Option 3"): grass and bushes still shorten both distances, the
  unprotected band only grows from his recognition distance to his notice distance, 1.2 times it.
  Both only for a soldier already in the fight (2026-11-06, after a soldier on his phone 40 m off turned at
  the user's pistol shot and killed him at once: "That should not happen"): a shot gives the player away only
  to a soldier who was alert when he heard it, and the quick aim needs him alert 2 s before he caught sight
  of the player; one the shot alerts learns where it came from and is surprised.
- **Squad tactics belong here** (2026-11-06): "yes, I want the squad tactics in difficulty overhaul".
  First the game's own, switched on where it ships them off: flanking and height at long and medium
  range, moving when compromised; then fire from cover and crouching to shoot in the open, researched
  before they are built.
- **Squad tactics, picked** (2026-11-06): assaulters fire from cover and a flank goes wider at range, "A, C can be
  implemented"; "for B I want 50% assault 50% suppress"; "then do the crouch and shoot if no cover is nearby".
- **Cover first when the player is behind cover** (2026-11-06): "if I'm not in cover and their cover is too far,
  crouch, aim and shoot. if I'm behind cover, then, they have a bigger priority to move to cover than crouching
  and shooting"; too far is "10m"; with the player behind cover and none within 20 m, "he aggroes me".
- **Rescue left as it is** (2026-11-06): an assaulter who fires from his cover or holds in the open no
  longer reaches the game's check for a wounded squadmate, which runs after he has waited 3 s at cover;
  suppressors still check at every start of their attack. "I dont need rescue fix."
- **Firefights are aggressive.** Nothing the player does silences a soldier who sees him: suppression
  only spoils aim, and holding fire never sends a soldier elsewhere
  ([2026-10-29](#on-the-real-map)).

## Perception

Agreed 2026-11-03. The user gave the distances for 17 situations: where a soldier first notices
something (turns, comes to look) and where he recognises the player as an enemy and shoots. The model
below fits them. A soldier recognises the player within R and notices something within N; the mod
turns the two into the visibility the game compares with the soldier's own fuzzy and clear lines.

| Parameter | R |
|---|---|
| Base: day, in the open, straight ahead, idle | walking upright 60 m, moving crouched 50 m, still (standing or crouched) 40 m |
| In high grass, crouched | ×0.5 |
| Shade by day | ×0.5, of his head: anything solid within 100 m toward the sun, or a crown on that line; fades with the daylight (phase 3, 2026-11-04) |
| Camo suit, still, in grass | 3 m |
| Cover | hidden only where it hides his head; head and shoulders over a wall count as in the open |
| Tall grass on a crest between them | hidden as by a rock, unless his head clears the grass (2026-11-03) |
| A bush between them | ×(1 − its camouflage): ×0.1 for a savannah bush, two hide him; wherever its leaves are, on the line to his head (2026-11-04) |
| Night | at most 25 m, whatever he does |
| Lit by a lamp at night | the night limits ×1.25, 31 m: a point or spot light that is on, his head within its reach and a spot's cone, nothing solid between (phase 3, 2026-11-04) |
| Heavy fog | at most 40 m in a sandstorm, the heaviest; lighter fog in proportion to its full distance over its amount (2026-11-04) |
| Talking guards | ×0.5; deferred 2026-11-04, social posts left as shipped until needed |
| Driver | only through a 50° windscreen cone; a turret gunner sees 180° like a soldier on foot, and so does a passenger (phase 2; 50° and passengers 2026-11-04) |
| Alert, or just out of a fight | ×1.25, after the night and fog limits |
| Fighting | ×1.5, after the night and fog limits (2026-11-06: "It feels realistic") |
| At his side, within 180° | ×0.5 |
| Behind him | never; walking upright within 20 m he hears the player, "only crouching behind an enemy can get you behind them without noticing" |

He notices from 1.2 × R. In daylight, in the open and straight ahead, a moving player catches his eye
from farther: 100 m upright, 70 m crouched. At night he notices from 30 m at most, in heavy fog 45 m.

**Notice delay** (2026-11-04, only at his side since 2026-11-06). Noticing was where a fight started:
he turns, the player falls in his focus, and he recognises him at once. In his focus, the 60° he
looks along, a soldier not fighting the player notices him at once. At his side he does within 20 m;
beyond it the player must first be in his sight for T = 1 s × ((d − 20 m) / 20 m)² ÷ s, where s is
how far inside the notice distance N the player stands, 1 − d / N, capped at 2 s. Walking upright at
30 m it is 1.5 s. Out of his sight it is forgotten over 2 s. Nothing is shown on screen. Until it runs
out the game is told he sees nothing. From 2026-11-04 the delay ran in his focus too, beyond 30 m and
capped at 4 s, after the user's anchor of 2 s for running "like a madman from one cover to another in
a totally visible situation" at about 50 m. On 2026-11-06 the user took it out of the focus: "the
original intention was that I can slip between covers to move closer to the enemies when they are not
really looking in my direction. instead, it makes them react really slow when I'm in front of them."

Once he recognises the player he fires as soon as his aim is on him. From 2026-11-04 to 2026-11-06 he
held his fire 0.075 s per metre beyond 10 m first, 3 s at 50 m ("at 50m they squint a little, think
with their brain and then at 3s mark, they start shooting"); the user dropped it with the focus delay,
"so they react fast on the first notice". His aim still settles from his first shot.

A shot, the player's flashlight or a vehicle's headlights at night alert those who see or hear them
at once: they know where it came from and go to clear the spot, but they do not see the player
("they don't have nightvision"); recognising him still follows the table.

Built in phase 1, checked on the range by fc2drive (row 24), not yet played: the base, view, grass,
the camo suit, cover, night, the alert factor, the reaction time (removed 2026-11-06), fire discipline at the recognition
line, and a `perception:` line for each first notice and recognition. The plugin reads the camo suit
itself: the game's own camouflage reads the suit's bonus inverted and never hides anyone. A sniper aiming through his scope sees as much farther as the game's scope
makes him, a choice of the build's, but since 2026-11-04 no farther than night or fog allow (the
user's call). Tall grass on a crest between them (2026-11-03,
after the user was shot through grass over curved ground; walked since 2026-11-04, the plan the user
accepted from two research papers on grass and sight). The soldier's line to the player's head is
walked a metre at a time, leaving out 2 m at each end. Where it runs within the grass's 1.2 m of the
ground, and 0.3 m lower over it than the lower of their heads stands over its own, the game's
high-grass test there hides the player. Flat or evenly sloping ground never brings the line that low,
so crouching in grass stays the ×0.5 above. A soldier's walk holds 0.25 s, the game's own grass
sampling interval.

On the range's grass crest (fc2drive's `crest.py`, 2026-11-04), a soldier 36 m away kept his eyes on
the player behind its grassy part and never made him out, standing or crouched: every such look was
behind grass. He recognised him at 35.8 m over its bare part, and at 16.6 m on its top. In the grass
field's flat cases the walk never fired (424 looks). A walk took 1 to 4 µs on average. A few took 141
to 368 µs by the clock, but a thread-cycle count put the longest's own work at about 9 µs: the main
thread had been preempted mid-walk. Not played.

Bushes between them (2026-11-04, after the user's soldier on a crest saw through a bush, not grass).
The game counts a bush only inside a small sphere at its heart ([bushes and
trees](../engine-internals/ai.md#bushes-and-trees)), so the plugin counts it wherever its leaves
are: an upright column as wide as its narrower side and as tall as the bush, rounded at the top, in
full within three quarters of it and fading out to its edge. It still adds the game's camouflage,
less the leaves shot off, and asks about the line to the player's head. A bush is a RealTree whose
camouflage reaches lower than 1 m and that stands under 6 m, which leaves out two big diospyros trees
and the small palm. Trees keep the game's sphere in their crown. On the range a tamarix stands on the
bare crest's top, 30 m from a rifleman. As shipped he recognised the player through it at 26.2 m
standing and 29.8 m crouched, the bush giving cover 0.24 to 1. With the plugin, every look through it
gave cover 0.10, and he never made the player out, standing or crouched; 6 m aside, clear of it, he
recognised him at 30.4 m. In an earlier run he walked up to 3 m short of the bush and recognised the
crouched player past its edge at 19.1 m, cover 0.48. Not played.

Phase 2 (2026-11-04): fog, built, read from the fog the environment manager hands the renderer each
frame, and on the range a sandstorm brought a soldier to 45 m and 40 m. Drivers, built: a soldier
in a vehicle's driving seat sees only within 25° of the way it faces, beyond that as behind him;
gunners and passengers already see as on foot, the mod's view zones taken from their own view. The
seat lookup found the player at the wheel of a Datsun on the range; no soldier has been seen driving
under it. Talking guards deferred. Headlights are dropped: the soldiers already hear the car. The flashlight
stays in the Flashlight mod, as long as it works with this one. Phase 3: shade and lamps.

Shade (2026-11-04), built. The player is in shade when a ray from his head toward the sun meets
anything solid within 100 m, or the line crosses a bush's or tree's crown: an upright ellipsoid as
wide as its narrower side, from the bottom of its camouflage sphere to its top. It halves every
distance by day and fades out with the daylight, and a player in shade no longer catches the eye
from 100 m. Found for the player once a quarter of a second. On the range (fc2drive's `shade.py`),
crouched still at eight spots around the tamarix on the crest's top, only the spot on the far side
from the sun was in shade, the west one at 9 and the east one at 17, on every look, and the soldier
recognised him from half as far there. Around a Datsun no spot was: a crouched head stands higher than
its ends. Not played.

Lamps (2026-11-04), built. The player is lit when a point or spot light that is on has his head
within its radius, and a spot within its cone, with nothing solid between them, found while the sun
is down. A flashlight or a car's headlights face away from his head, so their cones leave him out.
Lit, the night's limits reach 1.25 times as far, 31 m to recognise and 37.5 m to notice. The lights
are read from the scene's own list, which holds only those near the player. On the range
(fc2drive's `lamp.py`) one of `tmpla`'s point lights spawned 3 m up: 2 m beside it every look found
the player lit and an idle rifleman 15 m off recognised him from 31 m, 15 m north of it from 25 m. At
the ranch in world 1 a room's lamp lit the player inside, and a step outside, still within its
reach, the wall between left him unlit. Not played.

## Built

| Step | What | In game |
|---|---|---|
| 1 | Hit log in `fcse.log` | used for every step since |
| 2 | Damage both ways by body part; the last-bar cushion removed | "felt realistic": four hits kill the player, two torso hits down a merc |
| 3 | Hit model: plain-chance roll, aim that settles with range, recoil, the 0.85 cap, head aimed at 15 % of the time | head share fixed to about a fifth of hits |
| 4 | Intuition 6 s → 1 s, sighting, fire discipline, grass, crossing speed, range falloff for pistols, SMGs and shotguns | fire discipline, grass and crossing seen; the rest assumed |
| 5 | Squad: suppression (near misses pin a soldier for 1 s and restart his aim); squad memory (hunted while within 15 m of where last seen); watched spot (a peek elsewhere makes him re-aim) | suppression works; memory and watched spot not yet |
| 6 | Real rounds (2026-11-05): each gun's impact level from its round, no soldiers' damage curves, no close-range bonus, a head hit kills anyone but the player, a torso hit never downs; replaces step 2 | range, row 26 |
| 7 | The game's own health back (2026-11-05): the mod's healing and its removal of the last-bar cushion scrapped, a blank slate for the health rework | nothing to check: the game as shipped |
| 8 | Regeneration (2026-11-05): above one bar, one bar to full in 5 minutes from the game's 5 s after the last hit, in place of the game's refill of the bar health is in | range, row 27 |
| 9 | Health by difficulty (2026-11-05): maximum health and bar curves scaled to AK-47 hits from full, the last bar's cushion on Easy and Normal only, regeneration per difficulty | Hardcore on the range, rows 27 and 28 |
| 10 | Blood only from buckshot (2026-11-05): no blood spray from a bullet, on soldiers or the player; shotguns keep theirs through their own impact record | range, row 29 |
| 11 | Quick in front (2026-11-06): the notice delay only at his side, capped at 2 s; the reaction hold removed | range, row 25 |
| 12 | The game's hit counts (2026-11-06): Easy and Normal pools take as many AK-47 hits as in the game, 37 and 28; regeneration removed, the game's own refill of the bar back on Easy, Normal and Hardcore, none on Infamous | range, row 27, Hardcore only |
| 13 | Alert aim and shots that give the player away (2026-11-06): an alert soldier settles in 0.3 s, from when he caught sight of the player too, and his first shot can hit; a shot heard within the gun's own radius makes the player out to a soldier who can see any of him, until 3 s out of his view | range, row 30 |
| 14 | Fire at a glimpse (2026-11-06): a soldier in a combat state fires at his fuzzy line rather than his clear one | range, row 31 |
| 15 | Only in the fight (2026-11-06): a shot gives the player away only to a soldier already alert when he hears it; the quick aim only for one alert 2 s before he caught sight of the player | range, row 30 |
| 16 | Fighting sight and the game's tactics (2026-11-06): a soldier fighting the player sees 1.5 times as far as idle, alerted 1.25; flanking and height switched on at long and medium range, relocating when under fire, hurt or shot at by another target | range, row 32 |
| 17 | Squad roles, cover and crouching (2026-11-06): half a squad assaults, half suppresses; an assaulter at his cover fires from it with the suppressor's pop-up, strafe and stand-up attacks and looks for cover he can fire from; a flank runs 20 m across at long range, 15 m at medium; with no cover near, a soldier crouches where he stops to fire | range, rows 32-34 |
| 18 | Cover by where the player is (2026-11-06): the plugin tells each soldier's brain, on agent flag 34, whether the player has been in the open for him within 1 s; with him in the open and the soldier's cover more than 10 m off, the soldier crouches and fires where he stands, looking again every 3 s and at once when the player leaves the open; a suppressor with no cover does the same; with the player behind cover, the soldier runs to his cover however far, and with none in reach takes any within a 20 m run of himself, or rushes the player; he crouches only while the player is in the open | range, row 35 |
| 19 | Blood by round (2026-11-07): a hit sprays blood only as the real round would show it, and only where it goes in: none from a pistol or SMG, a rifle round's or shotgun pellet's from a head hit, the .50 BMG's from any hit; on soldiers and the player. The spray is a dark red mist with three droplets in place of the game's animated blob; replaces step 10. Since 2026-11-08 by the whole round's muzzle energy, over 1,000 J on a head hit and over 10,000 J on any, which adds the Desert Eagle, and the mist 0.45 m | range, row 29 |
| 20 | Blood behind and below (2026-11-08): a body hit from a round over 1,000 J spatters the ground, a wall, an object or a vehicle up to 3 m on along its line, an object showing the part that falls on it; a soldier killed after a head hit or three other bullets leaves a puddle 0.8 m across where he stood. Both stay until the decal budget recycles them. Blood from full-auto fire on the body, from a pull's third round, was built and removed the same day; drips from a wounded player are paused | row 29 |

The mod's README lists each feature's values.

## Finish checklist

Every feature gets checked again, one at a time, on the test range (DevTools' Range tab, `mods/DevTools/range`) before the mod
counts as finished. The range is a flat plain with 14 walls, so what happens there comes only from
what is spawned into it.

**Setup for every row:**

- Hardcore.
- Travel with `#GameChangeWorld("testrange", "2054037628277228842")`.
- Soldiers come from DevTools' Spawner: a squad of 1 to 6, 25 m ahead. DevTools invincibility is on
  unless the row tests damage to the player.
- Read the session's combat log after each run, in `bin\difficulty-overhaul-logs`, with the mod's
  `fight_report.py`; its README's Combat log section says how.

A soldier's post only sets where he starts: in a fight he moves sideways around the player, so the
range in the log drifts from the scenario's distance.

### Built: check on the range

| # | Feature | On the range | Expect | Before | Range result |
|---|---|---|---|---|---|
| 1 | Hit log | any fight | `soldier … hit:`, `player hit:` and `shot at player:` lines | used every step | passes |
| 2 | Damage taken | invincibility off; take hits from one soldier | about 4 torso hits kill; one head hit kills; no softer last bar | felt realistic | at torso 2.5 and head 4, 8 torso hits killed (132 each of 1,025 health; a head hit 211) and the last bar took full damage. Raised to torso 5 and head 24, the user's choice: passes, 4 torso hits killed (264 each), and 2 torso hits then a head hit (1,266). Replaced on 2026-11-05 by row 26 |
| 3 | Damage dealt | shoot one soldier in the torso, then one in the limbs | about 2 torso hits or 4 limb hits down him | seen | passes: 2 torso hits killed one (176 each of 250), 3 leg hits the other (88 each). Replaced on 2026-11-05 by row 26 |
| 4 | Hit model: settling | step out in front of a soldier at 10 m, then at 50 m | first shots miss; the far one takes visibly longer to settle | seen | passes. 10 m: his chance climbs from 0 to 0.53 in about 0.8 s. 50 m: from 0 to 0.67 before recoil in about 1.6 s, first hit 1.3 s after his first shot; after a reload he resumed fully settled |
| 5 | Hit model: recoil and cap | stand still in the open against a full-auto soldier | hits spread out within a burst; no shot is certain, point blank included | seen | passes: a burst opens at his full chance (0.52-0.69 at 20 m) and the next shots get about 0.6 of it; the highest chance was 0.80 at 5 m, and at 4-5 m 13 of 21 shots hit |
| 6 | Hit model: head aim | take about 20 hits | about a fifth on the head | fixed and seen | passes: 9 of 57 hits on the head (16 %), over the machine gunner run and one before it at 11 m |
| 7 | Crossing | strafe across a soldier's line at 20 m, then run straight at him | far fewer hits while crossing | seen | passes: strafing at 12 m costs about 23 % of his chance, running at him nothing; the reduction is accepted |
| 8 | Range falloff | a squad with pistols or SMGs, at 10 m and at 40 m | much lower `chance` at 40 m than a rifle gets | assumed | passes for the shotgun: settled 0.30-0.43 at 10-13 m, 0.04-0.08 at 34-43 m (a rifleman: 0.67 at 50 m); pistols and SMGs not run, accepted by the user: no enemy carries one as his main weapon. Shotgun raised to about 0.7 at 15 m, falling off past 20 m, at the user's call: a settled shot at a still player was 0.61-0.81 at 14 m, 0.40-0.46 at a moving one, then 0.41 at 21 m, 0.10 at 37 m, 0.04 at 45 m |
| 9 | Fire discipline | crouch behind a 1.2 m barrier, then lie at the edge of his view | no shooting until he can make you out (visibility 0.2); holding, he stays put, aimed, and fires as you show | loved; changed 2026-10-29 from 0.4 and refusing the shot, not run | low wall passes once it was barriers: he fires at each peek; the eroded wall's broken top flipped his view of a crouched player between 0.54 and 0.27 every frame, around the 0.4 line. The sniper at 90 m passes, the user's check |
| 10 | Grass | Grass field: crouch in the grass, a soldier at 20 m and one at 50 m | the one at 20 m sees and shoots you, idle, alerted or fighting; the one at 50 m does not | loved at 90 % from 10 m; moved on 2026-11-02 to nothing within 30 m, fading in to full at 40 m, after an alerted soldier walked past the user in grass ("Grass hides me too well"), not run | not played. fc2drive's `grass.py` on the range's new grass field (2026-11-03): a still, crouched player was recognised at 14.8 m and 19.8 m with `in grass` on the line, and not at all from 50 m; on open ground at 20 m the line has no `in grass` |
| 11 | Intuition | break line of sight behind a wall and move along it | he loses your true position within about 1 s and searches where you were last seen | assumed | passes at the long wall: "they lost my real position. they hunt the last known position and then they eventually get bored of it like they should" |
| 12 | Suppression | fire near a soldier, then hit him | he keeps firing: `shaken 0.70` on his shots for 1 s after a near miss; after a hit, `hit, he panics` and he fires back at once at `shaken 0.25` | worked as a 1 s pin; redesigned 2026-10-29, not run | passes: 15 near misses logged; after one his chance drops to 0-0.03 and climbs back over his settling time (0.42 1.3 s later at 36 m). A burst already under way finishes, at a chance of 0, before the pin holds him |
| 13 | Squad memory | get seen, hide behind a wall for more than 30 s | they keep hunting you; `squad memory: … still hunted` | not seen | not shown yet: at the long wall the squad flanked or hunted the player within the game's own memory, and the sniper (at 90 m; at 150 m the game removed him) never lost him either, so the plugin never had to hold on; accepted by the user |
| 14 | Squad memory: slipping away | from behind the wall, move more than 15 m away unseen | they lose you 10-25 s later and search | not seen | passes at the long wall: lost 34 s after the last sighting, once the player had slipped back |
| 16 | Eyesight | Rifleman at 50 m: stand in the open in front of him. Rifleman side-on at 30 m: start at the edge of his vision | he sees you at 50 m; at his side he notices, turns and then sees you | new | stock and the mod alike: he only investigated at 50 m and walked past a player who stepped aside. 50 m in front passes: he engages at once, every run. Side-on passes: standing at his side he did not notice; stepping into his view, he turned and fired once he faced the player, "it feels good". Recognition (an alerted soldier keeps looking after a faint sighting) was removed: it never acted in any run. Standing behind a barrier at 20 m he was unseen until he jumped: the body-coverage rays counted a head and shoulders as a third of a man. Within 30 m that share now counts double, the user's choice: passes, a soldier who walked inside 30 m recognised the player standing behind the wall. Raised to 40 m, the user's call: 30 m was not enough |
| 15 | Watched spot | peek twice from the same corner, then from 3 m along the wall | quick shots at the same corner; slower, missing first shots at the new spot; `watched spot: … aim restarts` | not seen | restarted his aim during burst pauses while the player stood in the open; fixed to read his unbroken sighting time. Passes at the long wall: back at the same spot after 5 s he fired at once at 0.46-0.70; 9 m along, the aim restarted at 0.00-0.17 |
| 17 | Going down | Two riflemen at 20 m: shoot one only in the arms and legs | the hit that would kill him downs him instead, every time: he falls, calls out, crawls, maybe shoots from the ground; `soldier … hit: … wounded` | new: limb hits died outright on a failed 15 % roll | passes with a level 16 rifle, a single-hit weapon: every first leg hit downed him at 49, torso hits killed, and a comrade carried one on his shoulders, then dropped him on the ground. A normal rifle's lethal limb hit not seen, accepted by the user |
| 18 | Launchers | Rocket man at 40 m: stand in the open, on foot, then get into a vehicle | no rocket at the player on foot: he fires his pistol or SMG, raising the launcher at most once, unfired. Getting into a vehicle brings the launcher out | new: "I peeked at a rock and a guy 40m from me sent an rpg rocket in my direction. That is unfair. In a car, it can work" | on foot he fired his pistol. In a car he kept to the pistol until he dodged the car and his plan restarted, then fired rockets: he chose once, when he drew. Now he keeps watching and switches as the player gets in and out; not yet seen |
| 19 | Shotgunners | Shotgunner behind cover: stay behind your line and let him come | he closes in from cover to cover, pausing about 1 s at each, and inside 12 m keeps pushing rather than holding back to fire; he stops about 3 m away | new: the game switches the shotgunner's push off inside 12 m; switched on at the player, the user's pick ("their weapon seems like a suitable tool for that") | he pushed in and walked into the player: "he has to stop at some point. like at 3 meters". Every soldier then stopped 3 m from the player, seen or not. Dropped with that stop, the user's call on 2026-10-28: the game already has the push on from 50 m to 12 m, and stopping near 12 m is close enough |
| 20 | No headshot on first contact | Rifleman at 20 m: step out in front of him, take hits with invincibility on, duck out of sight and step out again | every `shot at player:` line under 3.0 s on him says `at the torso`; after that about one in seven says `at the head`, and ducking out starts it over | new: from the research, the user's pick ("that really rustles people's jimmies"); his first shot could go for the head | not run |
| 21 | Healing | Rifleman at 20 m, invincibility off: take hits down to the last bar, heal, heal again, then hide and wait | at 1,025 maximum the surgery leaves 410 health (40 %) and a syrette 666 (65 %), `healed:` lines; a second syrette is refused; nothing comes back below 666, and from there health reaches full in 6 minutes, starting 5 s after the last hit (3 s on Casual, 10 s on Experienced, never on Infamous) | new: the user's three states ("surgery makes you survive one more shot, syrette makes you survive 2 shots, full regen makes you survive the 4 shots"); the game's surgery gave two bars, a syrette full health, and the bar health was in refilled within a second | not run. Scrapped on 2026-11-05 with the last-bar cushion's removal: the game's healing is back |
| 22 | Reaction | Rifleman at 50 m, idle: stand in the open in front of him until he spots you, then stay | `reaction: spotted the player at 50 m, holds his fire 2.2 s`, no shot for 2.2 s, then his first `shot at player:` says about `2.2 s on him` | new 2026-10-29: an idle soldier at 58.6 m opened fire the moment he noticed the user standing in a bush and killed him 1.2 s later ("my last death was bullshit"); the user's pick of a hold on first contact only, not on every peek ("I want them spraying when I peek") | removed 2026-11-06, never run: "make their first notice delay ... gone, so they react fast on the first notice" |
| 23 | Stand-down | Rifleman at 40 m, idle: fire a shot into the air out of his sight, then hide where he cannot find you | he searches, then 90 s after the alert last started over walks back to where he stood and goes back to idle | new 2026-11-02: after a shot into the air a squad searched for minutes and never went back to its posts; in the game only an idle soldier's look at something glimpsed, or the player in a safe zone, ends an alert ([how an alert ends](../engine-internals/ai.md#how-an-alert-ends)); the user's pick of 2 minutes, in the brain; 90 s since 2026-11-03, the user's call | not run |
| 24 | Perception | `python tools\fc2drive\scenarios\perception.py`: 20 cases on the duel lane, at the low wall and in the grass field, by day and at night, in front of him, at his side and behind, the camo suit given by its bonus plan | each case's first `perception:` distances within a few metres of the model's | new 2026-11-03, [the model](#perception); supersedes rows 10, 16, 22 and item N | fc2drive, 2026-11-03, not played. A level counts only while the trace says he kept the player in the case's zone, with 3 tries; the player faces him. Matches wherever judged: still at 35 m and 45 m, crouched still at 35 m, crouched at night at 20 m, at his side at 15 m, heard behind him at 20 m, crouched behind him never, standing at the wall at 33 m; in grass, crouched and still at 15 m and only noticed at 22 m, moving crouched noticed at 22 m, standing at 30 m as in the open. Unjudged: every approach and most nevers, since an idle soldier looks away within seconds. The camo suit hid no one at first, the game reading its bonus inverted ([grass and stealth](../engine-internals/ai.md#grass-and-stealth)); read by the plugin since, it hides as the model says (item N). Then the walk-in cases, a soldier walking one of the range's patrol routes to the player, each sighting judged in the zone it happened in: all ten match. Noticed and recognised at 48/40 m standing or crouched still, 100/60 m walking upright to him, 61.7/50 m crouch-walking (he was glancing aside from 71 to 63 m), 30/25 m at night walking and crouched still, 24/20 m crouched still in grass, 30/25 m sidestepping in grass; never at his side 35 m off his line, never crouched behind the low wall with him 18 m away |
| 25 | Notice delay | `python tools\fc2drive\scenarios\notice.py`: an idle rifleman walks the lane's patrol route toward the player by day, who stands still upright or crouched, walks to him, or runs to and fro | he notices where the delay, walked over his own traced looks, says he does: later than the notice distance, after the seconds in sight the model gives | new 2026-11-04: "running like a madman from one cover to another in a totally visible situation should give you around 2 seconds" | fc2drive, 2026-11-04, not played: every case where the model puts it, within a look. Standing still, noticed at 39.5 m (model 39.7 m), walking to him 38.5 m (38.5 m), crouched still 35.8 m (35.8 m), running to and fro 85.5 m after 4.0 s in sight (85.7 m), where row 24 had 48 m still and 100 m moving upright. An idle soldier glances about, so the delay fills only while the player is in his focus and drains between glances; row 24's walk-in notice distances predate it. 2026-11-06, the delay only at his side, not played: in his focus he noticed at once, where the model puts it: still at 47.8 m and crouched still at 45.5 m, his first looks inside his 48 m notice distance, and recognised him at 40.0 m; walking to him at 71.1 m and running to and fro at 89.8 m, inside 100 m; each after 0.0 s in sight. At his side not run |
| 26 | Real rounds | `python tools\fc2drive\scenarios\damage.py`: single shots at a rifleman's back from 10 m with the Makarov, Uzi, AK-47, FAL, SPAS-12 and AS50, standing and crouched; then a rifleman shoots the player, invincibility off | each hit at its gun's level: a torso hit takes 134, 185, 350, 399, 117 a pellet and 1,000, an arm 0.6 of it, a head hit kills; the rifleman's AK takes 350 from the player | new 2026-11-05: "weapon damage must be tied to the specific weapon with their specific ammo"; "ai damage curve should not exist"; "bonus damage should not exist at point blank" | fc2drive, 2026-11-05, not played: every hit at its gun's level. Torso hits took 134, 185, 117 a pellet, and the AK, FAL and AS50 killed; arm hits took 80 from the Makarov and 210 from the AK, which downed him at 41; a SPAS pellet in the legs 117. The rifleman's AK took 349 a hit from the player. A shotgun's off-centre pellets have their own stim, `Stim_ImpactDamageSecondary`, at the game's level until it was set too |
| 27 | Regeneration, then healing | `python tools\fc2drive\scenarios\healing.py` (`regeneration.py` until 2026-11-06): a rifleman at 20 m, invincibility off, until one hit lands; cleared; 30 s later a second rifleman's first hit | a `regenerating:` line 5 s after the hit; the second hit's health before it where one bar to full in 300 s puts it, below the top of the bar the hit left him in | new 2026-11-05: "we need to make a slow regen feature if the player is not below the threshold for the field aid"; the user's picks: replace the game's refill, 5 minutes, the game's 5 s | fc2drive, 2026-11-05, not played: 1,025 -> 676, the climb started 5.0 s later with one bar 205, full in 128 s; 37.7 s in, health stood at 779, as predicted, where the game's refill would have reached the bar's top, 820, within a second. Below one bar not run. 2026-11-06, regeneration removed, the game's refill back: 1,025 -> 676, and 30 s later health stood at 820, the top of that bar, as the game refills it |
| 28 | Last bar | `python tools\fc2drive\scenarios\last_bar.py`: a shotgunner at 20 m shoots the player, invincibility off, until he dies | every hit takes its full damage, one on the last bar too, on Hardcore | new 2026-11-05: the cushion off on Hardcore and Infamous, kept on Easy and Normal | fc2drive, 2026-11-05, not played: pellets took 117 each from 1,025 down to 92, inside the 205 bar, and the next took its full 117 and killed, where the cushion would have taken 29. The light machine gunner, who carries an AK-47 on the range, killed with his third hit, 349 each. The range plays Hardcore only, so Easy, Normal and Infamous are unchecked: their curves are in the built patch |
| 29 | Blood | `python tools\fc2drive\scenarios\blood.py`: a Makarov, AK-47, SPAS-12 and AS50 round into a rifleman 10 m off, standing so it lands high and crouched so it lands on his body, then a rifleman and a shotgunner shoot the player, who faces them; a shot straight after each hit and 0.3 s later | a red mist and a few droplets from the AK round and the shell only on a head hit, from the AS50 on any hit, never from the Makarov; nothing where a round comes out | new 2026-11-05: "remove the blood splatters for all guns except pellet guns, for enemies and for the player too"; 2026-11-07: "the base game blood splatter is what you would realistically see for a rifle headshot", then "too blobby, too chunky. we need a mistier, sprinklier one", and of a mist at the entry or a cone at the exit, "lets do only the front" | fc2drive, 2026-11-05, not played: 0.3 s after an AK round in the legs, and in the torso, nothing flew off the soldier; after a SPAS shell red droplets flew off his legs, twice. On the player neither the rifleman's hits nor the shotgunner's showed a spray from first person; what shows is the game's red hit flash, a screen effect of its own, unchanged. Blood by round, fc2drive, 2026-11-07: the AK and the Makarov in the torso showed nothing; AS50 head and back hits threw fine droplets; the first mist, the game's puff at 0.5 m in its own dark tint, never showed in play, and in blood red at 0.7 m it hung as a red haze 0.3 s after an AS50 head hit and through the SPAS's rear sight. Played: "it works flawlessly", then darkened a little. 2026-11-08, played: full-auto body blood worked and was removed ("full auto in itself should not make a mist"); spatter on the ground "consistent"; none on a concrete barrier, a placed entity, until the game's four-corner fit was let pass for the mod's decals, which is unseen; the mist and the puddle made smaller |
| 30 | Shots give him away | `python tools\fc2drive\scenarios\given_away.py`: a rifleman faces the player, who crouches still in the open, beyond the 50 m an alert soldier makes him out from, and fires one round away from him: an AK-47 at 60 m and at 100 m, a Dragunov at 100 m | given away within the gun's report, 75 m and 150 m, not beyond; his first shots above 0 | new 2026-11-06: 5-6 soldiers with a line of sight never fired after the user killed one of them | fc2drive, 2026-11-06, not played: the AK at 60 m gave him away at 59.1 m and the soldier fired 1.0 s later, his first shots at 0.14, 0.09 and 0.20, two of them hits, where first shots had been 0.00; at 100 m it did not; the Dragunov at 100 m did, at 100.7 m, and in one run that soldier closed to 74 m before firing, his first at 0.28, in another he only advanced within 12 s. Held on his recognition line, the game kept him searching: it makes a target out only past the line and follows a visibility below 0.9 slowly, so a given-away player is seen in full. Since the user's death to a soldier his own shot had just alerted, two rounds: the first only alerted the AK's soldier at 60 m, who noticed him and came on; the second gave him away at 50.1 m. With his alert counted from the first round, every frame, he had been searching about 3 s when he caught sight of the player, so he aimed as a hunter: first shots 0.03, 0.18, 0.33; counted from perception's looks, which can start late or go stale, he had read as surprised. The Dragunov's soldier, alert for seconds, closed to 75 m and fired at 0.20, 0.11, 0.22 |
| 31 | Fire at a glimpse | `python tools\fc2drive\scenarios\glimpse.py`: a rifleman 15 m off sees the player and opens fire; the player moves to 55 m and crouches still, past the 50 m a fighting soldier recognises him from, within the 60 m he notices him from | he keeps firing at a low chance where before he held | new 2026-11-06, the user's pick of option 3 | fc2drive, 2026-11-06, not played: his lines 0.20 and 0.40, the player seen at 0.30; in 8 s 37 shots and 9 hits at chances of 0.18 to 0.25, no hold, then he closed to 44 m; a second run 27 shots and 5 hits. The watched spot now restarts his aim on a glimpse too |
| 32 | Fighting sight and tactics | `python tools\fc2drive\scenarios\tactics.py`: on open ground east of the hub, with no cover in reach, a squad of three riflemen 40 m off fights the player, who stands still; their positions come from the perception trace | a fighting soldier notices a still player from 72 m and recognises him from 60 m; an assaulter runs out to the player's side | new 2026-11-06 | fc2drive, 2026-11-06, not played: 72 m and 60 m in every look. The flank runs as the game builds it, a 7 m step to the side, then a 4 s wait: with the brain trace (`-braintrace`), all nine soldiers of three squads took the assault role, found no cover, and each made one or two flank steps in 15 s, moving round the player by 10 to 20 degrees. At 40 m a 7 m step is that small; close in, where the game ships it on, it takes him well round. The height check and the relocate scanners ran; neither had anything to act on in the open. With the flank 20 m and 15 m across, the soldiers went up to 33 to 35 degrees round the player in 15 s, where 7 m had taken them 10 to 20 |
| 33 | Squad roles | `python tools\fc2drive\scenarios\tactics.py open 25`: the squad of three 25 m off in the open, within the 35 m the game splits a squad in | one assaulter and two suppressors, by the brain trace's plans | new 2026-11-06, the user's 50/50 | fc2drive, 2026-11-06, not played: the split came out 1 assault, 2 suppress for three, 1 and 1 for two, in both tries that fought; at 40 m, past the 35 m, all of them assaulted, as the game has it |
| 34 | Firing from cover and crouching | `python tools\fc2drive\scenarios\tactics.py wall`: the squad 36 m off north of the low wall, the player south of it, the wall 28 m from him, where a soldier in his medium band looks for cover; and the open run for the crouch | an assaulter who reaches the wall fires from it; with no cover a soldier crouches where he stops | new 2026-11-06 | fc2drive, 2026-11-06, not played: two assaulters reached the wall, chose a cover attack and strafed out from its end, as a suppressor did; before the assaulter's cover search took the suppressor's heuristic, one reached the wall and found nothing there to fire from, so he stood. Every soldier with no cover crouched at the end of his flank runs, up to a third of their shots fired crouched. No pop-up over the wall seen: each took the corner |
| 35 | Cover by where the player is | `python tools\fc2drive\scenarios\tactics.py side`: the player standing in the open 40 m south of the squad, the low wall off to one side 33 m from him; `tactics.py behind 36 crouched`: the player 3 m south of the wall, standing until the first shot and then crouching behind it | in the open, a soldier whose cover is more than 10 m off holds and fires crouched; behind the wall, every soldier makes for cover near himself, finds none, and rushes | new 2026-11-06 | fc2drive, 2026-11-06, not played: in the open an assaulter found the wall more than 15 m off and held four times, 23 of his 31 shots crouched; the others had no cover in reach and ran out to the player's side. Behind the wall every soldier fighting him searched near himself once he crouched, and each rushed him; the cover the game's search found lay at the player's own wall, so the run of more than 20 m sent him at the player. The search's 20 m is not measured from the soldier, so the path the soldier would run decides it |

### Still to do before it is finished

**Before release, the user's call on 2026-10-29:** the camo suit (N), grass visibility (row 10) and heavy
testing; the vest (F) was scrapped on 2026-11-06. Added 2026-11-04: perception phase 3 (shade and lamps), and a revision of
the damage, the soldiers' shooting and the health system.

| # | Item | State |
|---|---|---|
| A | **Flanking a remembered position.** A soldier relocates around the spot where the player was last seen | the game already does it: at the long wall the squad flanked to where it last saw the player, every run; nothing to build unless a test asks |
| B | **The last man.** A squad's lone survivor does something: falls back, or fights on | scrapped, the user's call on 2026-10-28: running away "doesn't fit the game". The game's order only makes him bark, and its fall-back-to-start plan was the cheap way to make him run |
| C | **How enemies differ.** Skill by role: guard posts below vehicle patrols below bodyguards; the arsenal stays as the game has it | direction set, [on hold](#open-how-enemies-differ) |
| D | **Tuning candidates** from the review of step 4: hold fire at each soldier's own "clear" value rather than a fixed 0.4; sighting and the speed evaluator both count the player's movement; range falloff from each weapon's own effective range; grass in combat (0.8) hides more than when alerted (0.765) | closed, the user's call: the game's own combat "clear" is 0.4, the fire line already |
| E | **Damage falloff with range** for the player's hits on soldiers | not needed, the user's call: a rifle round barely slows within the 150 m soldiers live at |
| F | **A vest** | scrapped, the user's call on 2026-11-06: "Scrap it. We don't need it." |
| G | **The difficulty spread.** Ease Casual and Experienced and harden Infamous, from the Hardcore values | every lever [listed below](#open-the-difficulty-spread) gets an answer before the mod is finished; after A to C |
| I | **Periphery sees movement.** Outside his focus a still player is barely noticed, a moving one is | built: a still or crouch-moving player counts 0.2 of one moving upright in the periphery, full from 2 m/s (crouch-moving split off at the user's request: it is as fast as a walk); works in game, user 2026-10-26 |
| J | **Close-range awareness.** Within about 2 m a soldier senses the player at any angle, but a crouching player can always approach without alerting him; walking or running alerts him | built: moving upright at 0.5 m/s or more, both cones go all round within 20 m and behind his eyes he counts 0.5 (noticed); within 2 m he counts in full. The 20 m is the user's; walking, running and jumping count, crouching never; works in game, user 2026-10-26 |
| K | **Vertical sight limit.** About 45° above and 60° below his eye line | skipped, the user's call: the cone test is already a round 3D cone, 90° from his head direction, and a narrower band would only matter at steep angles |
| L | **A heard step means investigate.** A footstep sends a soldier to look; only seeing the player makes him hostile | skipped, the user's call: J already makes a walking player behind a soldier noticed, not shot |
| M | **AI view in the game.** Each soldier's cones, how well he sees the player and where he thinks the player is, drawn as lines in the 3D scene after the frame renders, depth-tested, not through DevTools' overlay or a world-to-screen projection | built in DevTools as the AI view toggle (Mod Configuration Menu); works in game, cones redone as ground sectors and lines widened at the user's request |
| N | **The camo suit.** The game's suit makes a crouched, still player in high grass invisible beyond 2 m (4 m to an alerted soldier), but only outside combat | built 2026-11-02, the user's pick ("should help past 10m if player stand still"): invisible from 10 m to idle and alerted soldiers alike, still nothing in a fight; not run. Replaced on 2026-11-03 by the perception model's 3 m, the plugin reading the suit itself, since the game's camouflage never fires ([grass and stealth](../engine-internals/ai.md#grass-and-stealth)): fc2drive, a soldier 4 to 13 m away never saw a player crouched still in grass in the suit, and every one within 3 m recognised him, at 2.6 and 3.0 m |
| H | **Release.** CHANGELOG, README-release, the Nexus page and the release workflow | last |

Decided against on 2026-10-26: a reload making a soldier settle his aim again; recoil mattering less at close range; fading the over-cover doubling in before 40 m. The shotgun was raised to about 0.7 at 15 m, falling off past 20 m, instead.

## On the real map

2026-10-26, Hardcore, world 1, 14 minutes, with Sound, Sky and Aiming Overhaul: "I felt that it was
realistic. a bit raw, maybe people would call it fucking annoying, but I think the fair and hard is
definitely there."

From the log:

- Soldiers fired 145 shots at the player, from 10 m out to 100 m. Twelve hit, 11 in the torso and 1 in
  the head, and the player died three times.
- The player hit soldiers 34 times, which killed nine and downed two.
- Near misses pinned soldiers 28 times.

The easier settings are where the rawness is for others to soften ([item G](#still-to-do-before-it-is-finished)).

What stood out: "the immense pinned down feeling. when someone aimed at me on an empty field without
cover, I was pretty much dead. which is exactly what should happen." The player's own weapons felt bad,
which the user reads as room to improve them later, not as a fault of this mod.

2026-10-29, Hardcore, world 1, two runs of about 7 minutes at the same post, with Sound and Aiming
Overhaul. Being pinned in the open still felt right, but hunting soldiers one by one from cover was too
easy: "when I shoot at them they pause making them very vulnerable", and they were "repositioning or
shoot and stop and wait" instead of fighting from cover. Over the two runs suppression pinned soldiers
98 times, and in the second run fire discipline held them 50 times. Every refusal ended the soldier's
shoot task, so the game chose something new for him, often moving. The user's direction: a firefight
is aggressive,
"the enemies need to kill me, not thinking about it", and a soldier who sees the player and knows
where he is keeps firing until the player relocates. Changed the same day, not yet seen in game:

- **Suppression never stops a soldier's fire.** A near miss leaves him 70 % of his chance for 1 s.
  A hit panics him: for 1 s he fires back at once, however little he sees, at a quarter of his chance.
  This is the user's design.
- **Fire discipline holds the trigger, not the task.** He fires once he can make the player out, his
  own fuzzy line (0.2 in combat) rather than the clear one (0.4). Holding, his shoot task is told his
  aim is not on the player yet, which keeps it waiting where he stands.
- **Grass in combat hides 90 %,** as from an idle soldier, so a hidden player stays under the lower
  fire line.

Not wanted: suppressive fire at the spot where the player ducked out of sight.

Later the same day an idle soldier 58.6 m away noticed the user standing in a bush and fired at once,
ten rounds in 1.1 s while his aim settled; two hit and killed him ("this is too much of a reaction
time"). Added, not yet seen: a soldier who spots the player before he has fired in the fight holds his
fire for 1 s plus 0.025 s per metre while his aim settles ([row 22](#built-check-on-the-range)). Not
wanted: holding fire until his aim is half settled on every sighting, which would end the spraying on
a peek.

2026-11-06, alpha 4 on Steam, with the squad tactics, firing from cover and cover by where the player
is: "I feel like this is it now. I can't add anything to it, the experience was right." A lone unaware
soldier went down "effortlessly"; with two or more "the picture changes drastically". "The best thing is
that they expect me to peek from the same spot. thats an almost instant death. I need to relocate, need to
find cover, exactly what hardcore needs." Grass works both ways: "I dont see them but they dont see me
either." Much testing is still to come.

## Open: how enemies differ

The game's enemies are all alike, and they grow stronger with the story, not with the player
([where difficulty acts](../engine-internals/ai.md#where-difficulty-acts),
[what sets one soldier apart](../engine-internals/ai.md#what-sets-one-soldier-apart)).

**Direction set on 2026-10-26, on hold:**

- **The arsenal stays as the game has it.** Better enemy weapons as the player progresses and moves to
  the second world.
- **What changes is skill, by role.** A half-dressed guard at a post is never as skilled as a guard
  protecting an important person, and the patrols that drive the map in Land Rovers are more skilled
  than the guard posts.
- **Proposed, not agreed:**
  - Three tiers: green for guard posts, regular for vehicle patrols, elite for bodyguards.
  - Skill scales aim settling, best chance, recoil control, suppression and head aim. Eyesight,
    hearing and memory stay the same for every tier.
  - A soldier's tier is read once from where he spawned, backed by the special archetypes.
- **Questions for the user:** whether three tiers is right; where snipers, RPG and mortar men go;
  whether temperament stays part of this; whether the difficulty setting shifts all tiers; whether
  skill should be readable on sight.

The earlier notes, not agreed:

- **What may scale.** The direction is the player's arsenal and armour, not the act or the mission
  count: better weapons may bring a harder fight, the story's progress may not. How the arsenal is
  measured is open. One candidate is the level at which the player's best weapon first appears in the
  enemy loadouts, which reuses the game's own weapon order.
- **Who a soldier is.** The direction is novices and veterans, and local fighters and foreign
  mercenaries, who behave differently:
  - novices of either kind are weaker and flee;
  - veteran locals fight bravely to recklessness;
  - veteran mercenaries fight best, but fall back or give up when surprised or faced at close range in
    plain sight.

  What marks a soldier as one or the other is open. What the game has: the `Caucasian` and `Nubian`
  archetypes, the SpecOps, sniper and assassination-target archetypes, and a random per-soldier
  reputation that only social behaviour reads.
- **What the difficulty setting adds** on top of that, if anything.
- **What carries the behaviour.** The game has no combat flee or surrender. A soldier can be sent to fall
  back (flag 57), to rush (43, 55), or allowed or refused to go down. Whether setting those flags from a
  plugin works is untested.

The game scales with progression in three places, which any answer has to deal with: enemy weapons
follow the mission count, weapon condition follows the act, and how often soldiers go down follows the
player's reputation.

## Open: the difficulty spread

Item G. Each lever below is to be scaled, flattened or left as it is, by an explicit decision, before the
mod counts as finished. Listed on 2026-10-28; nothing decided yet.

**The game's own levers** ([where difficulty acts](../engine-internals/ai.md#where-difficulty-acts)):

| Lever | Where | Casual → Infamous | With the mod |
|---|---|---|---|
| Player maximum health | data: `HealthMax_*` and `HealthBarSize_*` curves | 1,800 / 1,400 / 1,075 / 825 at the curves' start | scaled to AK-47 hits from full: 8,427 / 6,568 / 1,025 / 320 at the campaign's start, the 37th / 28th / 3rd / 1st kills, Easy and Normal the game's own counts |
| Last-bar cushion | code | quarters damage while the player is on his last bar | kept on Casual and Experienced, removed on Hardcore and Infamous |
| Syrette cap | code | 6 / 5 / 4 / 3 | untouched |
| Bleeding out on the last bar | data: `HealthDegenerationLevels` in `gamemodesconfig.xml` | a bar every 15 s on every setting; Infamous names a curve that does not exist | untouched |
| Health pickups, medic stations | code | per setting; Infamous medic stations take another branch, never checked | untouched |
| Max ammo | data: `iMaxAmmo{Casual,…}` per weapon | for example the dart rifle's 9 / 4 / 3 / 2 | untouched |
| Clip pickups, reloading | code | per setting, values not checked | untouched |
| Grenade drops from the dead | data: `ChanceToDropGrenade` | 1 / 0.5 / 0.33 / 0.25 | untouched |
| The player's weapons' jams and wear | code | per setting, values not checked | untouched |
| The player's close-range damage | data: `RangeMultipliers` per weapon | AK-47 at 0-2 m: +7 / +3 / +2 / none | removed on every setting |
| Upgrade bonuses | code | per setting | untouched |
| Enemy forced misses | data: `nForcedFailure*` per weapon | AK-47 misses 4-8 / 2-4 / 1 / 0 shots after a hit | replaced by the hit model on every setting |
| Squad role split | code | one soldier / 70 % / 70 % / 90 % of the squad assaulting | half assaulting on every setting (2026-11-06) |
| Malaria drain | code | per setting | untouched |
| Gamepad aim assist | code | per setting | untouched; gamepad only |

**The mod's own constants that could follow the setting.** Damage taken, aim settle, recoil recovery and
squad coordination are the forgiveness levers agreed on 2026-10-21:

| Constant | Hardcore now | Eased by |
|---|---|---|
| Damage the player takes | each gun's real-round damage, whole wherever it lands: 350 from an AK-47 | a share of it; the player's health system is to be reworked |
| Regeneration | the game's own refill of the bar, 5 s after the last hit | removed 2026-11-06, the pools carry Easy and Normal; Infamous never refills |
| Aim settle | 0.6 s + 0.02 s per metre | longer before his shots land |
| Recoil | a follow-up loses up to 60 %, recovering in 0.25 s | more lost, or slower recovery |
| Suppression pin | 1 s | longer pins |
| Head aim | 15 %, none in his first 3 s | a lower share, a longer grace |
| Squad memory | hunted within 15 m of the last sighting | a smaller radius |

**Proposed, not agreed:**

- The enemy is the same on every setting. The spread comes from the game's forgiveness levers that
  already differ (health, syrettes, ammo, grenade drops, jams), plus two of the mod's constants: damage
  taken and aim settle.
- The hit cap of 0.85, eyesight, fire discipline and grass stay the same on every setting: they are what
  the enemy is.

**Questions for the user:**

- The point-blank bonus for the player's bullets on the easier settings: flatten it, or keep it as
  forgiveness?
- Whether G goes ahead before C, which is on hold.
