# Inventory Overhaul

Purpose: Two guns of any class, and a notebook held in both hands in real time on Tab for everything else the player carries and reads.
Stage: active

## Features

- [done] Two guns of any class, A on key 3 and B on key 2; a gun picked up with two carried replaces the one in hand - user: "works perfectly"; #51 keys and an old save's primary in A verified 2026-11-12
- [done] Picked-up gun comes to hand - fc2drive only, the user has not played it
- [done] Items: stowed guns keep their wear and loaded rounds, four guns in all with the slots; other items by id and count
- [done] Notebook on Tab, given with the map, held in both hands, its right page bending with the hands - user: "it works in all situations"; first opens on gun A, verified 2026-11-12
- [done] Items page: guns A and B, Items with the diamonds, the belt with its counts; the wheel moves the selection
- [done] Post-it for the selected row: picture, name, condition word, ammo, upgrade icons, ratings - a bought upgrade not seen yet; #52 every gun's post-it, DLC and rusty guns included, verified 2026-11-12
- [done] Condition word from the gun's reliability as a share of its MaxReliability - fc2drive: new, used, worn, worn, failing at 100, 70, 45, 30, 10 %; carried and stowed verified 2026-11-12
- [done] Left mouse uses the selected row: stows gun A or B, heals on the syrettes - user-confirmed
- [done] Context menu on a stowed gun: To slot A, To slot B - user: "it fully works"
- [done] X drops the gun in hand, or the notebook's selected row, on a new Drop control - stowed gun and item drop seen by fc2drive; #49 a dropped gun lands on its side, verified 2026-11-12
- [done] Docs page: other mods' notes, photos and cassettes, the Jackal and Predecessor tapes, played with a click - user: "it works!"
- [done] Paper reader: a note or photo read whole over the right page, the wheel turning its sheets - user: "everything looks good"
- [done] Handwriting (Caveat Bold) only on the paper being read - 20 px without Cyrillic not yet judged by the user
- [done] Missions page: reputation, the current job with its objectives and the Underground's - seen by self-test
- [done] Buddies page: active buddies first, then the others, the missing and the dead faded
- [done] R turns the page with the pause menu's own flip at twice the speed, half its sound - seen by self-test; the user has not heard it
- [done] Pause menu without Resume, Journal, Buddies and the tapes, its main page as the game-over dialog; Upgrades stays
- [done] Other mods' notes, photos, cassettes and items known by a naming convention - user: "it all works"
- [done] Example note, photo and cassette on the Docs page until Tales of Africa ships, never saved - the user heard the cassette
- [done] Test window in DevTools' overlay: Stow, Take, Give, Discard, Fill, +1 diamond
- [new] INVENTORY_OVERHAUL_FILL and the Lua start variable removed once tests set up the notebook through DevTools - plugins hold no test logic
- [new] Malaria on the pills' post-it - agreed: "malaria becomes the pills' card"
- [new] Camo Suit in Items once bought - agreed 2026-10-26
- [done] The two sound banks on ids of their own, 00fe0001 and 00fe0002: their old records 0x00FD0001-0x00FD0004 were also in Vehicle Overhaul's 00fd0000.spk, and a shared id crashed the game in Sound Overhaul's echo work
- [new] Box page at the crates: the bought guns as a catalogue and stored things, one shared box #6
- [new] One fetch mission - design log step 9
- [postponed] Diamonds, food, pills and ammo as items - later, once decided
- [postponed] Notebook in vehicles - on foot only for now
- [postponed] Notebook labels and gun names in the player's language - English in every language for now
- [postponed] Paper grain on the pages - the user called it "awful"; to be retried later
- [scrapped] Weight and carrying capacity - tried and dropped 2026-10-26
- [scrapped] Key legend on the notebook - user: "we dont need it"; never re-propose
- [scrapped] Record/stats page - numbers about the player stay out of the notebook
- [scrapped] Upgrades in the notebook - stays in the pause menu for other players
- [scrapped] Story synopses from the pause menu's Journal - dropped by the user
- [scrapped] Notebook on DirectX 10 - the user does not care; never spend time on it

## To validate

- A bought upgrade's icon on the post-it - not seen
- A picked-up gun comes to hand - fc2drive only
- The handwriting at 20 px on a paper being read - not judged
- The Missions page - self-test only
- R's page turn: the pause menu's flip at twice the speed, half its sound - not heard
- Saving with the notebook out, then loading that save twice - #46
- The cassette and its click on the Docs page, with Vehicle Overhaul deployed, after the banks moved to 0x00FE ids - fc2drive clip only: the click, then the cassette, each matched to its audio

## Approvals

- played: 7950320 2026-11-01 - implied by the inventory-overhaul-1.0.0 release; earlier "it all seem to be working" (2026-10-29)
- page text: 7950320 2026-11-01 - implied by the inventory-overhaul-1.0.0 release

## Checks

## Release order

- after fcse-1.5.0
- after jackall-1.7.0
- after vortex-0.6.0

## Collisions

- DevTools: fixed by FCSE hook stacking - both hook the entry of the pause service's signal listener
- flashlight: fixed by 98535d3c - its light fell on none of the notebook's pages
- map-overhaul: fixed by 137373d1 and 9e951c86, and by building the notebook without its layer - its patterns start past this mod's hooks on the pause listener and the map's Update; the notebook copies the map's banks from the install, which its layer remakes
- tales-of-africa: merged by JackAll, no shared field - both edit hud.mgb; Tales of Africa adds its own pictures, materials and exports to this mod's `a_io_paper_pictures`, and every name both ship is identical
- 0x0071AE70: stacks - the input dispatcher every mod with a control mid-hooks; FCSE stacks same-address hooks
