# VSS Vintorez

Purpose: Replace the single-player Dart Rifle with a VSS Vintorez, as data and art only.
Stage: maintenance

## Features

- [done] The VSS mesh in all five LOD tiers on the Dragunov's skeleton, with collision, muzzle socket and refitted bounding boxes
- [done] Textures at retail's tier, with a worn look that grimes as the weapon degrades
- [done] Steel that reads as metal through a faint cube map, and the wood graded reddish-brown with a varnish shine
- [done] The weapon on the ground: both pickups with their full part lists
- [done] HUD, bazaar and weapon-select icons
- [done] The name in all eleven shipped languages
- [done] Semi-automatic, ten rounds off the sniper ammo pool, the Dragunov's jam and break behaviour
- [done] The Dart Rifle's damage and short suppression radius kept - deliberate, a stealth weapon
- [done] Shot sound, first and third person - not yet confirmed in game
- [done] Reload animation, registered under the dragunov package, with 17 MOVE fragments
- [done] Aiming Overhaul's scope drawn as the Dragunov's PSO-1 - user, with AO: "everything works perfectly"
- [new] The shot banks 004bf5e9 and 004bf5eb re-encoded: both are in the IMA-ADPCM layout JackAll wrote before 2026-10-09, a click and a muffled shot
- [scrapped] Wood on its own material (DART_RIFLE_METAL is free) for a wood-only highlight - dropped by the user
- [scrapped] A normal map through a Generic material - dropped by the user; it would lose the worn look
- [scrapped] The multiplayer dropped pickup - skipped on the single-player rule, it still has no barrel

## To validate

- The shot sound, first and third person - not confirmed

## Approvals

- played: 518010c0 2026-11-18 - alone, then with Sound Overhaul and Aiming Overhaul: "okay it works", "It all works nicely"
- page text: 1687389e 2026-11-18 - with the load-below-AO-and-Sound-Overhaul line: "I also approve the vss page text"

## Checks

- simplify: 20db6f0 2026-11-15 - since mod-vss-1.1.0

## Release order

- after fcse-1.4.0
- after aiming-overhaul-1.1.0

## Collisions

- aiming-overhaul: fixed by loading the VSS after Aiming Overhaul - both set the Dart Rifle's scope keys
- sound-overhaul: fixed by loading the VSS after Sound Overhaul - both ship the Dart Rifle's shot banks 004bf5e9 and 004bf5eb
