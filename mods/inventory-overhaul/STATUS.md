# Inventory Overhaul

Purpose: Two guns of any class, and a notebook held in both hands in real time on Tab for everything else the player carries and reads.
Stage: active

## Features

- [done] Two guns of any class, A on key 3 and B on key 2; a gun picked up with two carried replaces the one in hand - user: "works perfectly"; #51 keys and an old save's primary in A verified 2026-11-12
- [done] The mouse wheel through the machete, gun A and gun B, whatever their class - user 2026-11-19: machete, A, B and back work in game
- [done] The HUD's swap icon shows the gun a pickup replaces, and again when the other gun is drawn - user 2026-11-19: it shows the gun in hand and changes when the other is drawn; with a slot empty, verified by the user 2026-11-19
- [done] Picked-up gun comes to hand - user 2026-11-17: "it all works flawlessly"
- [done] Items: stowed guns keep their wear and loaded rounds, four guns in all with the slots; other items by id and count
- [done] Notebook on Tab, given with the map, held in both hands, its right page bending with the hands - user: "it works in all situations"; first opens on gun A, verified 2026-11-12
- [done] The page drawn onto the paper through the engine's texture binders and device, with no Direct3D hooks, so it works under dgVoodoo and DLSS injectors - pages and a resize match the old build (fc2drive); user 2026-11-14: "they work alright" #63
- [done] Items page: guns A and B, Items with the diamonds, the belt with its counts; the wheel moves the selection
- [done] Post-it for the selected row: picture, name, condition word, ammo, upgrade icons, ratings - user 2026-11-17: "it all works flawlessly"; #52 every gun's post-it, DLC and rusty guns included, verified 2026-11-12
- [done] Condition word from the gun's reliability as a share of its MaxReliability - fc2drive: new, used, worn, worn, failing at 100, 70, 45, 30, 10 %; carried and stowed verified 2026-11-12
- [done] Left mouse uses the selected row: stows gun A or B, heals on the syrettes - user-confirmed
- [done] Context menu on a stowed gun: To slot A, To slot B - user: "it fully works"
- [done] X drops the gun in hand, or the notebook's selected row, on a new Drop control - stowed gun and item drop seen by fc2drive; #49 a dropped gun lands on its side, verified 2026-11-12
- [done] Docs page: other mods' notes, photos and cassettes, the Jackal and Predecessor tapes, played with a click - user: "it works!"
- [done] Paper reader: a note or photo read whole over the right page, the wheel turning its sheets - user: "everything looks good"
- [done] Handwriting (Caveat Bold) only on the paper being read - user 2026-11-17: "it all works flawlessly"
- [done] Missions page: reputation, the current job with its objectives and the Underground's - user 2026-11-17: "it all works flawlessly"
- [done] Buddies page: active buddies first, then the others, the missing and the dead faded
- [done] R turns the page with the pause menu's own flip at twice the speed, half its sound - user 2026-11-17: "it all works flawlessly"
- [done] Pause menu without Resume, Journal, Buddies and the tapes, its main page as the game-over dialog; Upgrades stays
- [done] Other mods' notes, photos, cassettes and items known by a naming convention - user: "it all works"
- [done] Example note, photo and cassette on the Docs page until Tales of Africa ships, never saved - the user heard the cassette
- [done] Test window in DevTools' overlay: Stow, Take, Give, Discard, Fill, +1 diamond
- [done] INVENTORY_OVERHAUL_FILL and the Lua start variable removed: tests press the test window's Give and Fill through the channel `inventoryoverhaul`, with DevTools' remote `send`
- [done] Malaria on the pills' post-it, the pause menu Journal's words for the level - user 2026-11-17: "it all works flawlessly"
- [done] Camo Suit in Items once bought, with the shop's name and description - user 2026-11-17: "it all works flawlessly"
- [done] The two sound banks on ids of their own, 00fe0000 and 00fe0001: their old records 0x00FD0001-0x00FD0004 were also in Vehicle Overhaul's 00fd0000.spk, and a shared id crashed the game in Sound Overhaul's echo work - user: "changes in instance 3 are verified"
- [done] A gun picked up from the ground or the armory's wall: with A and B empty, into A; with one empty, into the empty one; with both full, or four guns in all, it replaces the slot in hand, else A; the replaced gun falls at the player's feet - user 2026-11-17: "it all works flawlessly"
- [done] A gun on the ground picked up with the gun in hand jammed: the game's own CPickup::CheckPickable refused it, now let through for gun pickups only - fc2drive: a jammed AK-47 swapped for a new one, the swap icon shown; the user's reload case is the bolt cycle, a game issue for UFCP #79
- [postponed] Safe-house weapon crates as the game has them: with two guns of other classes carried, the crate's gun is dropped at the player's feet - found in RE, not seen in game; user 2026-11-18: "its for a later release"
- [done] Notebook usable in the armory and other rooms without guns: Tab, the wheel, the left mouse button and X bound in the briefing action map they use; there X stays X when Drop is rebound - user 2026-11-17: "it all works flawlessly"
- [postponed] Box page at the crates: the bought guns as a catalogue and stored things, one shared box #6 - not yet
- [postponed] One fetch mission - design log step 9
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

- With the gun in hand jammed, aim at a gun on the ground: the prompt shows and the pickup swaps the jammed gun for it - fc2drive only

## Approvals

- played: 33965adb 2026-11-19 - "IO is verified by all aspects"; "they are all ready by me, the tester"
- page text: 046e36c7 2026-11-17 - "I approve it"

## Checks

- simplify: 44507230 2026-11-19 - since inventory-overhaul-1.0.0; 33965adb's wheel and swap icon reviewed, nothing to change

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
- 0x00422510: stacks - the engine device's EndScene, which Sky, Aiming, Vehicle and Inventory Overhaul, Third Person and DevTools hook; FCSE chains them
- 0x00422630: stacks - the engine device's Reset, which Aiming, Vehicle and Inventory Overhaul and DevTools hook; FCSE chains them
- aiming-overhaul: merged by JackAll, no shared field - both edit hud.mgb's area #F2CDBA4C; this mod adds its own element there, Aiming Overhaul only hides the red flash's placing, and JackAll pairs elements by name
