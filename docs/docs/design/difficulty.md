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
| 8 | Range falloff | a squad with pistols or SMGs, at 10 m and at 40 m | much lower `chance` at 40 m than a rifle gets | assumed | passes for the shotgun: settled 0.30-0.43 at 10-13 m, 0.04-0.08 at 34-43 m (a rifleman: 0.67 at 50 m); pistols and SMGs not run |
| 9 | Fire discipline | crouch behind a 1.2 m barrier, then lie at the edge of his view | no shooting until he sees you clearly | loved | low wall passes once it was barriers: he fires at each peek; the eroded wall's broken top flipped his view of a crouched player between 0.54 and 0.27 every frame, around the 0.4 line. The sniper half to run |
| 10 | Grass | crouch still in high grass, if the range has any | no shooting from 10 m out | loved |
| 11 | Intuition | break line of sight behind a wall and move along it | he loses your true position within about 1 s and searches where you were last seen | assumed | passes at the long wall: "they lost my real position. they hunt the last known position and then they eventually get bored of it like they should" |
| 12 | Suppression | fire near a soldier without hitting him | he stops firing for about 1 s, then misses at first; `suppressed:` lines | works | passes: 15 near misses logged; after one his chance drops to 0-0.03 and climbs back over his settling time (0.42 1.3 s later at 36 m). A burst already under way finishes, at a chance of 0, before the pin holds him |
| 13 | Squad memory | get seen, hide behind a wall for more than 30 s | they keep hunting you; `squad memory: … still hunted` | not seen |
| 14 | Squad memory: slipping away | from behind the wall, move more than 15 m away unseen | they lose you 10-25 s later and search | not seen | passes at the long wall: lost 34 s after the last sighting, once the player had slipped back |
| 16 | Eyesight | Rifleman at 50 m: stand in the open in front of him. Rifleman side-on at 30 m: start at the edge of his vision | he sees you at 50 m; at his side he notices, turns and then sees you | new | stock and the mod alike: he only investigated at 50 m and walked past a player who stepped aside. 50 m in front passes: he engages at once, every run. Side-on passes: standing at his side he did not notice; stepping into his view, he turned and fired once he faced the player, "it feels good". Recognition (an alerted soldier keeps looking after a faint sighting) was removed: it never acted in any run |
| 15 | Watched spot | peek twice from the same corner, then from 3 m along the wall | quick shots at the same corner; slower, missing first shots at the new spot; `watched spot: … aim restarts` | not seen | restarted his aim during burst pauses while the player stood in the open; fixed to read his unbroken sighting time. Passes at the long wall: back at the same spot after 5 s he fired at once at 0.46-0.70; 9 m along, the aim restarted at 0.00-0.17 |
| 17 | Going down | Two riflemen at 20 m: shoot one only in the arms and legs | the hit that would kill him downs him instead, every time: he falls, calls out, crawls, maybe shoots from the ground; `soldier … hit: … wounded` | new: limb hits died outright on a failed 15 % roll | passes with a level 16 rifle, a single-hit weapon: every first leg hit downed him at 49, torso hits killed, and a comrade carried one on his shoulders, then dropped him on the ground. A normal rifle's lethal limb hit not yet seen |

### Still to do before it is finished

| # | Item | State |
|---|---|---|
| A | **Flanking a remembered position.** A soldier relocates around the spot where the player was last seen | first a one-launch test: does setting the relocate flag (45) from the plugin make a soldier act on it |
| B | **The last man.** A squad's lone survivor does something: falls back, or fights on | waits on item C; the game's order sets a flag nothing reads |
| C | **How enemies differ.** Novices and veterans, locals and mercenaries; scaling with the player's arsenal, not the story | [not agreed](#open-how-enemies-differ) |
| D | **Tuning candidates** from the review of step 4: hold fire at each soldier's own "clear" value rather than a fixed 0.4; sighting and the speed evaluator both count the player's movement; range falloff from each weapon's own effective range; grass in combat (0.8) hides more than when alerted (0.765) | only if a test-range check asks for it |
| E | **Damage falloff with range** for the player's hits on soldiers | skipped in step 4 |
| F | **A vest** | postponed, below |
| G | **The difficulty spread.** Ease Casual and Experienced and harden Infamous, from the Hardcore values | after A to C |
| H | **Release.** CHANGELOG, README-release, the Nexus page and the release workflow | last |

## Postponed

- **A vest.** A torso hit from a rifle nearly kills the player, which is realistic; a vest is how they
  will survive one. How it is obtained, whether its plates wear and whether mercs wear one are open.
  The [lore rules](./extending-the-world.md#rejected-outright) rule out plate armour.

## Open: how enemies differ

The game's enemies are all alike, and they grow stronger with the story, not with the player
([where difficulty acts](../engine-internals/ai.md#where-difficulty-acts),
[what sets one soldier apart](../engine-internals/ai.md#what-sets-one-soldier-apart)). Not agreed:

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
