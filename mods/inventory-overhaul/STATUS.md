# Inventory Overhaul

Purpose: Two guns of any class, and a notebook held in both hands in real time on Tab for everything else the player carries and reads.
Stage: active

## Features

- [done] Two guns of any class on keys 2 and 3; a gun picked up with two carried replaces the one in hand - user: "works perfectly"
- [done] Picked-up gun comes to hand - fc2drive only, the user has not played it
- [done] Items: stowed guns keep their wear and loaded rounds, four guns in all with the slots; other items by id and count
- [done] Notebook on Tab, given with the map, held in both hands, its right page bending with the hands - user: "it works in all situations"
- [done] Items page: guns A and B, Items with the diamonds, the belt with its counts; the wheel moves the selection
- [done] Post-it for the selected row: picture, name, condition word, ammo, upgrade icons, ratings - a bought upgrade not seen yet
- [in progress] Condition word thresholds - applied to raw 0-1000 reliability, so a gun reads "new" until nearly broken (bug)
- [done] Left mouse uses the selected row: stows gun A or B, heals on the syrettes - user-confirmed
- [done] Context menu on a stowed gun: To slot A, To slot B - user: "it fully works"
- [done] X drops the gun in hand, or the notebook's selected row, on a new Drop control - stowed gun and item drop seen by fc2drive
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
- [new] Malaria on the pills' post-it - agreed: "malaria becomes the pills' card"
- [new] Camo Suit in Items once bought - agreed 2026-10-26
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
- 0x0071AE70: stacks - the input dispatcher every mod with a control mid-hooks; FCSE stacks same-address hooks
