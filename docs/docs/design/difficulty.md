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
- **Only the player's fights change.** Fights between soldiers stay as shipped.
- **Fire discipline and grass define the mod.** A soldier holds his fire until he sees the player
  clearly, and grass hides a crouched player in combat too. Seen in game: "The bush, the tall grass
  actually makes them stop shooting. the sniper has no LoS and waits for me to peek."

## Built

| Step | What | In game |
|---|---|---|
| 1 | Hit log in `fcse.log` | used for every step since |
| 2 | Damage both ways by body part; the last-bar cushion removed | "felt realistic": four hits kill the player, two torso hits down a merc |
| 3 | Hit model: plain-chance roll, aim that settles with range, recoil, the 0.85 cap, head aimed at 15 % of the time | head share fixed to about a fifth of hits |
| 4 | Intuition 6 s → 1 s, sighting, fire discipline, grass, crossing speed, range falloff for pistols, SMGs and shotguns | fire discipline, grass and crossing seen; the rest assumed |
| 5 | Squad: suppression (near misses pin a soldier for 1 s and restart his aim); squad memory (hunted while within 15 m of where last seen); watched spot (a peek elsewhere makes him re-aim) | suppression works; memory and watched spot not yet |

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
- Read `bin\fcse.log` after each run.

A soldier's post only sets where he starts: in a fight he moves sideways around the player, so the
range in the log drifts from the scenario's distance.

### Built: check on the range

| # | Feature | On the range | Expect | Before | Range result |
|---|---|---|---|---|---|
| 1 | Hit log | any fight | `soldier … hit:`, `player hit:` and `shot at player:` lines | used every step | passes |
| 2 | Damage taken | invincibility off; take hits from one soldier | about 4 torso hits kill; one head hit kills; no softer last bar | felt realistic | at torso 2.5 and head 4, 8 torso hits killed (132 each of 1,025 health; a head hit 211) and the last bar took full damage. Raised to torso 5 and head 24, the user's choice: passes, 4 torso hits killed (264 each), and 2 torso hits then a head hit (1,266) |
| 3 | Damage dealt | shoot one soldier in the torso, then one in the limbs | about 2 torso hits or 4 limb hits down him | seen | passes: 2 torso hits killed one (176 each of 250), 3 leg hits the other (88 each) |
| 4 | Hit model: settling | step out in front of a soldier at 10 m, then at 50 m | first shots miss; the far one takes visibly longer to settle | seen | passes. 10 m: his chance climbs from 0 to 0.53 in about 0.8 s. 50 m: from 0 to 0.67 before recoil in about 1.6 s, first hit 1.3 s after his first shot; after a reload he resumed fully settled |
| 5 | Hit model: recoil and cap | stand still in the open against a full-auto soldier | hits spread out within a burst; no shot is certain, point blank included | seen | passes: a burst opens at his full chance (0.52-0.69 at 20 m) and the next shots get about 0.6 of it; the highest chance was 0.80 at 5 m, and at 4-5 m 13 of 21 shots hit |
| 6 | Hit model: head aim | take about 20 hits | about a fifth on the head | fixed and seen | passes: 9 of 57 hits on the head (16 %), over the machine gunner run and one before it at 11 m |
| 7 | Crossing | strafe across a soldier's line at 20 m, then run straight at him | far fewer hits while crossing | seen | passes: strafing at 12 m costs about 23 % of his chance, running at him nothing; the reduction is accepted |
| 8 | Range falloff | a squad with pistols or SMGs, at 10 m and at 40 m | much lower `chance` at 40 m than a rifle gets | assumed | passes for the shotgun: settled 0.30-0.43 at 10-13 m, 0.04-0.08 at 34-43 m (a rifleman: 0.67 at 50 m); pistols and SMGs not run, accepted by the user: no enemy carries one as his main weapon. Shotgun raised to about 0.7 at 15 m, falling off past 20 m, at the user's call: a settled shot at a still player was 0.61-0.81 at 14 m, 0.40-0.46 at a moving one, then 0.41 at 21 m, 0.10 at 37 m, 0.04 at 45 m |
| 9 | Fire discipline | crouch behind a 1.2 m barrier, then lie at the edge of his view | no shooting until he sees you clearly | loved | low wall passes once it was barriers: he fires at each peek; the eroded wall's broken top flipped his view of a crouched player between 0.54 and 0.27 every frame, around the 0.4 line. The sniper at 90 m passes, the user's check |
| 10 | Grass | crouch still in high grass, if the range has any | no shooting from 10 m out | loved | accepted untested by the user: the range has no tall grass |
| 11 | Intuition | break line of sight behind a wall and move along it | he loses your true position within about 1 s and searches where you were last seen | assumed | passes at the long wall: "they lost my real position. they hunt the last known position and then they eventually get bored of it like they should" |
| 12 | Suppression | fire near a soldier without hitting him | he stops firing for about 1 s, then misses at first; `suppressed:` lines | works | passes: 15 near misses logged; after one his chance drops to 0-0.03 and climbs back over his settling time (0.42 1.3 s later at 36 m). A burst already under way finishes, at a chance of 0, before the pin holds him |
| 13 | Squad memory | get seen, hide behind a wall for more than 30 s | they keep hunting you; `squad memory: … still hunted` | not seen | not shown yet: at the long wall the squad flanked or hunted the player within the game's own memory, and the sniper (at 90 m; at 150 m the game removed him) never lost him either, so the plugin never had to hold on; accepted by the user |
| 14 | Squad memory: slipping away | from behind the wall, move more than 15 m away unseen | they lose you 10-25 s later and search | not seen | passes at the long wall: lost 34 s after the last sighting, once the player had slipped back |
| 16 | Eyesight | Rifleman at 50 m: stand in the open in front of him. Rifleman side-on at 30 m: start at the edge of his vision | he sees you at 50 m; at his side he notices, turns and then sees you | new | stock and the mod alike: he only investigated at 50 m and walked past a player who stepped aside. 50 m in front passes: he engages at once, every run. Side-on passes: standing at his side he did not notice; stepping into his view, he turned and fired once he faced the player, "it feels good". Recognition (an alerted soldier keeps looking after a faint sighting) was removed: it never acted in any run. Standing behind a barrier at 20 m he was unseen until he jumped: the body-coverage rays counted a head and shoulders as a third of a man. Within 30 m that share now counts double, the user's choice: passes, a soldier who walked inside 30 m recognised the player standing behind the wall. Raised to 40 m, the user's call: 30 m was not enough |
| 15 | Watched spot | peek twice from the same corner, then from 3 m along the wall | quick shots at the same corner; slower, missing first shots at the new spot; `watched spot: … aim restarts` | not seen | restarted his aim during burst pauses while the player stood in the open; fixed to read his unbroken sighting time. Passes at the long wall: back at the same spot after 5 s he fired at once at 0.46-0.70; 9 m along, the aim restarted at 0.00-0.17 |
| 17 | Going down | Two riflemen at 20 m: shoot one only in the arms and legs | the hit that would kill him downs him instead, every time: he falls, calls out, crawls, maybe shoots from the ground; `soldier … hit: … wounded` | new: limb hits died outright on a failed 15 % roll | passes with a level 16 rifle, a single-hit weapon: every first leg hit downed him at 49, torso hits killed, and a comrade carried one on his shoulders, then dropped him on the ground. A normal rifle's lethal limb hit not seen, accepted by the user |
| 18 | Launchers | Rocket man at 40 m: stand in the open, on foot, then get into a vehicle | no rocket at the player on foot: he fires his pistol or SMG, raising the launcher at most once, unfired. Getting into a vehicle brings the launcher out | new: "I peeked at a rock and a guy 40m from me sent an rpg rocket in my direction. That is unfair. In a car, it can work" | on foot he fired his pistol. In a car he kept to the pistol until he dodged the car and his plan restarted, then fired rockets: he chose once, when he drew. Now he keeps watching and switches as the player gets in and out; not yet seen |
| 19 | Shotgunners | Shotgunner behind cover: stay behind your line and let him come | he closes in from cover to cover, pausing about 1 s at each, and inside 12 m keeps pushing rather than holding back to fire; he stops about 3 m away | new: the game switches the shotgunner's push off inside 12 m; switched on at the player, the user's pick ("their weapon seems like a suitable tool for that") | he pushed in and walked into the player: "he has to stop at some point. like at 3 meters". Every soldier then stopped 3 m from the player, seen or not. Dropped with that stop, the user's call on 2026-10-28: the game already has the push on from 50 m to 12 m, and stopping near 12 m is close enough |
| 20 | No headshot on first contact | Rifleman at 20 m: step out in front of him, take hits with invincibility on, duck out of sight and step out again | every `shot at player:` line under 3.0 s on him says `at the torso`; after that about one in seven says `at the head`, and ducking out starts it over | new: from the research, the user's pick ("that really rustles people's jimmies"); his first shot could go for the head | not run |
| 21 | Healing | Rifleman at 20 m, invincibility off: take hits down to the last bar, heal, heal again, then hide and wait | at 1,025 maximum the surgery leaves 410 health (40 %) and a syrette 666 (65 %), `healed:` lines; a second syrette is refused; nothing comes back below 666, and from there health reaches full in 6 minutes, starting 5 s after the last hit | new: the user's three states ("surgery makes you survive one more shot, syrette makes you survive 2 shots, full regen makes you survive the 4 shots"); the game's surgery gave two bars, a syrette full health, and the bar health was in refilled within a second | not run |

### Still to do before it is finished

| # | Item | State |
|---|---|---|
| A | **Flanking a remembered position.** A soldier relocates around the spot where the player was last seen | the game already does it: at the long wall the squad flanked to where it last saw the player, every run; nothing to build unless a test asks |
| B | **The last man.** A squad's lone survivor does something: falls back, or fights on | scrapped, the user's call on 2026-10-28: running away "doesn't fit the game". The game's order only makes him bark, and its fall-back-to-start plan was the cheap way to make him run |
| C | **How enemies differ.** Skill by role: guard posts below vehicle patrols below bodyguards; the arsenal stays as the game has it | direction set, [on hold](#open-how-enemies-differ) |
| D | **Tuning candidates** from the review of step 4: hold fire at each soldier's own "clear" value rather than a fixed 0.4; sighting and the speed evaluator both count the player's movement; range falloff from each weapon's own effective range; grass in combat (0.8) hides more than when alerted (0.765) | closed, the user's call: the game's own combat "clear" is 0.4, the fire line already |
| E | **Damage falloff with range** for the player's hits on soldiers | not needed, the user's call: a rifle round barely slows within the 150 m soldiers live at |
| F | **A vest** | postponed, below; on hold |
| G | **The difficulty spread.** Ease Casual and Experienced and harden Infamous, from the Hardcore values | every lever [listed below](#open-the-difficulty-spread) gets an answer before the mod is finished; after A to C |
| I | **Periphery sees movement.** Outside his focus a still player is barely noticed, a moving one is | built: a still or crouch-moving player counts 0.2 of one moving upright in the periphery, full from 2 m/s (crouch-moving split off at the user's request: it is as fast as a walk); works in game, user 2026-10-26 |
| J | **Close-range awareness.** Within about 2 m a soldier senses the player at any angle, but a crouching player can always approach without alerting him; walking or running alerts him | built: moving upright at 0.5 m/s or more, both cones go all round within 20 m and behind his eyes he counts 0.5 (noticed); within 2 m he counts in full. The 20 m is the user's; walking, running and jumping count, crouching never; works in game, user 2026-10-26 |
| K | **Vertical sight limit.** About 45° above and 60° below his eye line | skipped, the user's call: the cone test is already a round 3D cone, 90° from his head direction, and a narrower band would only matter at steep angles |
| L | **A heard step means investigate.** A footstep sends a soldier to look; only seeing the player makes him hostile | skipped, the user's call: J already makes a walking player behind a soldier noticed, not shot |
| M | **AI view in the game.** Each soldier's cones, how well he sees the player and where he thinks the player is, drawn as lines in the 3D scene after the frame renders, depth-tested, not through DevTools' overlay or a world-to-screen projection | built in DevTools as the AI view toggle (Mod Configuration Menu); works in game, cones redone as ground sectors and lines widened at the user's request |
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

## Postponed

- **A vest.** A torso hit from a rifle nearly kills the player, which is realistic; a vest is how they
  will survive one. How it is obtained, whether its plates wear and whether mercs wear one are open.
  The [lore rules](./extending-the-world.md#rejected-outright) rule out plate armour.

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
| Player maximum health | data: `HealthMax_*` and `HealthBarSize_*` curves | 1,800 / 1,400 / 1,075 / 825 at the curves' start | untouched; the biggest thing still differing |
| Last-bar cushion | code | quarters damage while the player is on his last bar | removed on every setting |
| Syrette cap | code | 6 / 5 / 4 / 3 | untouched |
| Bleeding out on the last bar | data: `HealthDegenerationLevels` in `gamemodesconfig.xml` | a bar every 15 s on every setting; Infamous names a curve that does not exist | untouched |
| Health pickups, medic stations | code | per setting; Infamous medic stations take another branch, never checked | untouched |
| Max ammo | data: `iMaxAmmo{Casual,…}` per weapon | for example the dart rifle's 9 / 4 / 3 / 2 | untouched |
| Clip pickups, reloading | code | per setting, values not checked | untouched |
| Grenade drops from the dead | data: `ChanceToDropGrenade` | 1 / 0.5 / 0.33 / 0.25 | untouched |
| The player's weapons' jams and wear | code | per setting, values not checked | untouched |
| The player's close-range damage | data: `RangeMultipliers` per weapon | AK-47 at 0-2 m: +7 / +3 / +2 / none | untouched |
| Upgrade bonuses | code | per setting | untouched |
| Enemy forced misses | data: `nForcedFailure*` per weapon | AK-47 misses 4-8 / 2-4 / 1 / 0 shots after a hit | replaced by the hit model on every setting |
| Squad role split | code | one soldier / 70 % / 70 % / 90 % of the squad in the first role | untouched |
| Malaria drain | code | per setting | untouched |
| Gamepad aim assist | code | per setting | untouched; gamepad only |

**The mod's own constants that could follow the setting.** Damage taken, aim settle, recoil recovery and
squad coordination are the forgiveness levers agreed on 2026-10-21:

| Constant | Hardcore now | Eased by |
|---|---|---|
| Damage the player takes per body part | torso 5×, head 24× | lower multipliers |
| Healing | the surgery leaves 40 % of maximum health (the game's two bars), a syrette 65 %, and 65 % to full takes 6 minutes | more left by each, faster regen |
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
- The last-bar cushion: back on Casual and Experienced only?
- Whether G goes ahead before C, which is on hold.
