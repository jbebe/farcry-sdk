---
sidebar_position: 5
---

# Difficulty

**Status:** in progress since 2026-10-21, built as `mods/difficulty-overhaul`. Damage, the AI's hit
model, fire discipline, intuition and grass are built and played. How enemies should differ from one
another is [not agreed](#open-how-enemies-differ).

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
| 5 | Squad, begun with suppression: the player's near misses pin a soldier for 1 s and restart his aim | not yet |

The mod's README lists each feature's values.

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
