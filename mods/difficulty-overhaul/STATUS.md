# Difficulty Overhaul

Purpose: Realistic, lethal combat with the player both ways: damage, his healing, and whether and how well soldiers see, fire at and fight him as a squad.
Stage: active

## Features

- [done] Real rounds: each gun's damage from its real cartridge, no AI damage curves, no close-range bonus - range-checked
- [done] Cartridge entity data (bullet mass, muzzle velocity, energy) for other mods - read by Aiming Overhaul's shot shake
- [done] Hit locations on soldiers: a head hit kills, torso 0.165, arms and hands 0.105, legs and feet 0.3, four AK-47 hits to the body as if under a soft vest; the player takes hits whole - range-checked, unplayed
- [done] Blood by muzzle energy, only where the round goes in, a dark red mist with droplets - played: "it works flawlessly"
- [done] Blood spatter behind a body hit and a puddle under the dead - ground spatter "consistent"; on objects unseen
- [done] Health by difficulty in AK-47 hits from full: Easy 37, Normal 28, Hardcore 3, Infamous 1 - only Hardcore range-checked
- [done] Last-bar cushion on Easy and Normal only - range-checked on Hardcore
- [done] Healing: the game's own bar refill, none on Infamous - range-checked on Hardcore
- [done] Going down: a leg or foot hit that would kill a soldier downs him instead, then limb hits on him do 0.11 - range: "good like this" with arms downing too; legs only range-checked, unplayed
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
- [done] The player's flashlight, Flashlight's rules read from its beam channel: lit lifts night, in the beam all around, 60 m in the dark - fc2drive-checked at night, unplayed
- [done] Fog limits sight, a sandstorm's to 40 m - range-checked
- [done] A soldier at the wheel sees only through a 50 degree windscreen - no soldier seen driving
- [done] Camo suit hides a still player in grass beyond 3 m from a soldier not fighting him - fc2drive-checked
- [done] Notice delay only at his side, at most 2 s; in his focus at once - side delay not run
- [done] Shots give the player away to soldiers already alert, within the gun's report - range-checked
- [done] Launchers fired at the player only in a vehicle; on foot he draws his other gun - switching as he gets in and out unseen
- [done] Squad roles: half assault, half suppress - range-checked
- [done] Assaulters fire from cover; flanks 20/15 m wide; the game's height attack and relocating switched on - range-checked
- [done] Cover by where the player is: crouch and fire at him in the open beyond a 20 m run to cover, cover first when he is behind cover, searches 30 m, else the flank below - range-checked at 10 m and 20 m; 20 m and 30 m unplayed
- [done] Stand-down: an alert ends after 90 s, the soldier walks back to where he was - range row not run
- [done] Short exposures from cover: up 1.5 s at a time, the pop-up twice in 9 s, then another spot preferred - unplayed
- [done] Duck at the crack: a round past a soldier at cover in a fight with the player drops him 1.5-2.5 s, then he moves on - unplayed
- [done] Flank a player behind cover: a soldier with no cover in reach runs out 20/15 m to his side, a random side, then comes in by the game's rush, standing, in place of the straight rush - range-checked: two of three came in from 38-40 m to 8-13 m, the third kept flanking at 40 m; unplayed
- [done] Grenades in a fight: every soldier carries one grenade, none more, spent by any throw; one throws at where he last saw a player kept behind cover 3 s, 8-30 m off, lobbed at 45°, else 65° or 30° where blocked, 10 s between throws - the game's own fight throw, never triggered as shipped - range-checked: throws at 20-30 m spent the grenade, the others ran from it; recorded at the long wall, the grenade over the player's wall; played: "I cant stay at one place because I'm smoked out with grenades ... This is perfect."
- [scrapped] Dead soldiers drop a grenade only if they still carry it, no roll - "dropped grenades should work like the base game, the thrown grenades do not affect it"
- [new] Alerted by a kill: onlookers crouch before drawing - the game already sends them to cover or away 0.2 s after an unseen kill, standing 0.35 s to draw (fc2drive)
- [scrapped] Combat log per session in bin\difficulty-overhaul-logs, and its hit lines - moved to DevTools
- [done] Perception and brain traces for testing (-perceptiontrace, -braintrace)
- [done] Combat lines sent on FCSE's combat channel for DevTools' combat log, to fcse.log when nobody hears - both seen in game, GOG 2026-11-11
- [new] Traces removed once tests read soldiers' sight and brain tasks through DevTools' remote control - plugins hold no test logic
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
- [scrapped] A squadmate killed in view crouches him where the game stands him up to stare - the game's plan almost never runs (fc2drive)
- [scrapped] Re-settle after a reload, close-range recoil scaling, over-cover fade-in - decided against 2026-10-26

## To validate

- Health on Easy, Normal and Infamous, in AK-47 hits from full - only Hardcore checked
- No head aim in a soldier's first 3 s on the player - range row not run
- Pistols and SMGs losing accuracy with range - not run
- A soldier at the wheel sees only through his windscreen - no driver seen
- The notice delay at a soldier's side, at most 2 s - not run
- Soldiers switching from launchers as the player gets in and out of a vehicle - unseen
- Stand-down: an alert ends after 90 s and the soldier walks back - range row not run
- Soldiers take four AK-47 hits to the body and six to an arm, two to a leg put them down; a head hit still kills
- Caught in the open, a soldier runs to cover up to 20 m off before he crouches and fires where he stands
- Firing from cover: a soldier shows himself about 1.5 s at a time and comes up somewhere else after two pops
- A near miss on a soldier at cover makes him duck for 1.5-2.5 s before he moves on
- With Flashlight at night: a soldier looking your way sees the light from afar, one in the beam sees you all around, at its edge too (full or half cone angle unverified); with it off the night hides you
- Behind a rock with soldiers coming over open ground: they spread out to your sides and come in from there, not in a line

## Approvals

- played: 8e20a82 2026-11-06 - alpha4 on Steam, Hardcore: "I feel like this is it now. I can't add anything to it, the experience was right."
- played: ad7a904f 2026-11-14 - world 1 on GOG, with UFCP, Aiming, Flashlight, Sky, Sound, Vehicle and VSS: "I really enjoyed this round."
- played: 90229be9 2026-11-14 - grenades and the flank, world 1 on GOG with the same mods: "Okay this is starting to be really good. I cant stay at one place because I'm smoked out with grenades. They dont aggro on me because they are happy behind their covers and its me who need to make the move. This is perfect."

## Checks

## Release order

- after fcse-1.5.0
- after jackall-1.8.0
- after vortex-0.7.0
- with or after the next Flashlight release, the first that sends its beam
- the difficulty spread (item G) decided lever by lever
- heavy testing (#8)
- a revision of damage, AI shooting and the health system (user, 2026-11-04)
- the user makes the Nexus page (NEXUS_MOD_ID empty in difficulty-overhaul-release.yml)

## Collisions

- flashlight: fixed by moving the guards' sight of the light into this mod - Flashlight no longer hooks CSensorySystem::AdjustFOV, this mod reads its beam on flashlight.beam
- aiming-overhaul: merged by JackAll, no shared field - both edit the guns' entity-library archetypes and particle libraries
- sound-overhaul: merged by JackAll, no shared field - both edit the guns' entity-library archetypes
