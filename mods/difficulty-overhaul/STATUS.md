# Difficulty Overhaul

Purpose: Realistic, lethal combat with the player both ways: damage, his healing, and whether and how well soldiers see, fire at and fight him as a squad.
Stage: active

## Features

- [done] Real rounds: each gun's damage from its real cartridge, no AI damage curves, no close-range bonus - range-checked
- [done] Cartridge entity data (bullet mass, muzzle velocity, energy) for other mods - read by Aiming Overhaul's shot shake
- [done] Hit locations on soldiers: a head hit kills, torso and legs 1, arms 0.6, hands and feet 0.3; the player takes hits whole
- [done] Blood by muzzle energy, only where the round goes in, a dark red mist with droplets - played: "it works flawlessly"
- [done] Blood spatter behind a body hit and a puddle under the dead - ground spatter "consistent"; on objects unseen
- [done] Health by difficulty in AK-47 hits from full: Easy 37, Normal 28, Hardcore 3, Infamous 1 - only Hardcore range-checked
- [done] Last-bar cushion on Easy and Normal only - range-checked on Hardcore
- [done] Healing: the game's own bar refill, none on Infamous - range-checked on Hardcore
- [done] Going down: a limb hit that would kill a soldier downs him instead - range: "good like this"
- [done] Hit model: aim settles with range, real chance capped at 0.85, recoil, crossing, 15 % head aim - "good like this"
- [done] Hunting soldiers settle in 0.3 s and can hit with the first shot, only if alert 2 s before sighting - range-checked
- [done] No headshot on first contact: no head aim in a soldier's first 3 s on the player - range row not run
- [done] Fire discipline: hold fire until he recognises the player, at a glimpse in a fight - "It works! I never felt like this in a game"
- [done] Range falloff: pistols and SMGs lose accuracy with range, a shotgun deadly to 20 m - pistols and SMGs not run, accepted
- [done] Intuition 1 s in place of 6 s - range: "they lost my real position"
- [done] Suppression: a near miss shakes his aim for 1 s, a hit panics him into firing back - range passes
- [done] Squad memory: hunted while within 15 m of the last sighting - holding never shown on the range, accepted
- [done] Watched spot: a re-peek from the same spot meets a settled rifle - "they expect me to peek from the same spot"
- [done] Perception: notice and recognise distances by stance, movement, side, behind, night - the user's model; walk-ins all match
- [done] Fighting soldiers see 1.5 times as far, alerted 1.25 - user: "It feels realistic"
- [done] Grass: crouched in grass halves the distances; tall grass on a crest hides - "I dont see them but they dont see me either"
- [done] Bushes hide the player wherever their leaves are - fc2drive-checked
- [done] Shade by day halves recognition; a lamp at night extends it 1.25 times - fc2drive-checked
- [done] Fog limits sight, a sandstorm's to 40 m - range-checked
- [done] A soldier at the wheel sees only through a 50 degree windscreen - no soldier seen driving
- [done] Camo suit hides a still player in grass beyond 3 m from a soldier not fighting him - fc2drive-checked
- [done] Notice delay only at his side, at most 2 s; in his focus at once - side delay not run
- [done] Shots give the player away to soldiers already alert, within the gun's report - range-checked
- [done] Launchers fired at the player only in a vehicle; on foot he draws his other gun - switching as he gets in and out unseen
- [done] Squad roles: half assault, half suppress - range-checked
- [done] Assaulters fire from cover; flanks 20/15 m wide; the game's height attack and relocating switched on - range-checked
- [done] Cover by where the player is: crouch and fire at him in the open, cover first when he is behind cover - range-checked
- [done] Stand-down: an alert ends after 90 s, the soldier walks back to where he was - range row not run
- [done] Combat log per session in bin\difficulty-overhaul-logs, read by fight_report.py
- [done] Perception and brain traces for testing (-perceptiontrace, -braintrace)
- [in progress] Difficulty spread (item G): a decision for every lever - health pools and cushion done, the rest undecided
- [on hold] How enemies differ (item C): skill by role, guard posts below patrols below bodyguards - "don't implement, just discuss"
- [on hold] Blood drips from a wounded player - paused by the user
- [postponed] Talking guards' sight (perception phase 2) - user: don't touch social posts
- [scrapped] A vest - "Scrap it. We don't need it."
- [scrapped] Regeneration - "regen totally renders useless the syringes"
- [scrapped] Three healing states (surgery, syrette, slow climb to full) - "not well thought out", the game's health restored
- [scrapped] Reaction hold on first contact - "so they react fast on the first notice"
- [scrapped] The last man falls back (item B) - "doesn't fit the game"
- [scrapped] Shotgunners pushing inside 12 m, and the 3 m stop - the game already pushes from 50 m to 12 m
- [scrapped] Blood from full-auto fire on the body - "full auto in itself should not make a mist"
- [scrapped] Blood cone where a round comes out - "lets do only the front"
- [scrapped] Rescue fix for assaulters firing from cover - "I dont need rescue fix"
- [scrapped] Suppressive fire at the spot the player ducked out of sight - not wanted
- [scrapped] Headlights giving the player away - soldiers hear the car
- [scrapped] Damage falloff with range on the player's hits (item E) - not needed within 150 m
- [scrapped] Vertical sight limit (K) and a heard step meaning investigate (L) - covered by the round cone and J
- [scrapped] Recognition: an alerted soldier keeps looking - never acted, removed
- [scrapped] Re-settle after a reload, close-range recoil scaling, over-cover fade-in - decided against 2026-10-26

## Approvals

- played: 8e20a82 2026-11-06 - alpha4 on Steam, Hardcore: "I feel like this is it now. I can't add anything to it, the experience was right."

## Checks

## Release order

- after fcse-1.4.0
- after jackall-1.7.0
- after vortex-0.6.0
- the difficulty spread (item G) decided lever by lever
- heavy testing (#8)
- a revision of damage, AI shooting and the health system (user, 2026-11-04)
- the user makes the Nexus page (NEXUS_MOD_ID empty in difficulty-overhaul-release.yml)

## Collisions

