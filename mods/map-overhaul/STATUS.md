# Map Overhaul

Purpose: A realistic map in place of the game's drawn one, and no GPS: a used military topographic sheet held open in both hands.
Stage: active

## Features

- [done] World 1 as a NATO-style topographic sheet from the world's own data: tints, contours, roads, buildings, names - user: "perfect"
- [done] The previous owner's ballpoint notes: G.P. guard posts, S.H. safe houses, A.D. arms dealers, bus stops, place remarks
- [done] The sheet on the 25 area maps, the world map and the 21 detail maps as town plans - seen in game by fc2drive
- [done] No player arrow on the map - seen in game
- [done] No GPS in the hand or on a vehicle's dashboard - seen gone in game from an old save
- [done] Whole sheet held open in both hands, 3x3 fold panels, below the level view - 62.5 cm at 1:3 600 (d717551f) not yet seen in game
- [done] The sheet stays level as the view looks up and down - seen by script only
- [done] Left mouse held reads the sheet close - seen by script; the monocular during the zoom untried
- [done] Only the mission targets' icons, each fixed on its fold panel - the game's own icons for now; jitter #40
- [done] Bent, not cut, at the folds - seen by script
- [done] The paper lit by the world's light - seen by script; brightness not judged by the user
- [on hold] Lap sheet in vehicles, a small part folded crudely - car view parked by the user
- [new] World 2 sheet - the same choices as world 1
- [postponed] Objectives as a red sharpie circle, the user's own icons in place of the game's - the user makes them later
- [scrapped] Mast (cell tower) notes - user: useless info
- [scrapped] Guard posts' supplies and gun counts - G.P. only, the user's pick

## Approvals

## Checks

## Release order

- after fcse-1.5.0

## Collisions

- inventory-overhaul: fixed by 137373d1 and 9e951c86, and by building its notebook without this layer - patterns start past its hooks on the pause listener and the map's Update; its notebook copies the map's banks from the install, which this layer remakes
- DevTools: fixed by 137373d1 - the pause listener pattern starts past its hook on the entry
- flashlight: fixed by 137373d1 - the pause listener pattern starts past its hook on the second instruction
- 0x0071AE70: stacks - the input dispatcher every mod with a control mid-hooks; FCSE stacks same-address hooks
