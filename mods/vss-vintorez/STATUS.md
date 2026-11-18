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
- [done] Shot sound, first and third person - user 2026-11-18: validated, before the re-encode
- [done] Reload animation, registered under the dragunov package, with 17 MOVE fragments
- [done] Aiming Overhaul's scope drawn as the Dragunov's PSO-1 - user, with AO: "everything works perfectly"
- [done] The shot banks 004bf5e9 and 004bf5eb re-encoded from the source clip with the current encoder, out of the IMA-ADPCM layout JackAll wrote before 2026-10-09 that gave a click and a muffled shot
- [scrapped] Wood on its own material (DART_RIFLE_METAL is free) for a wood-only highlight - dropped by the user
- [scrapped] A normal map through a Generic material - dropped by the user; it would lose the worn look
- [scrapped] The multiplayer dropped pickup - skipped on the single-player rule, it still has no barrel

## To validate

- The shot sound, first and third person, after the re-encode: no click at the start, not muffled - nothing heard

## Approvals

- played: da21184b 2026-11-18 - alone, with Sound Overhaul and Aiming Overhaul, then with Realism Plus Redux under them: "okay it works", "It all works nicely", "all works"
- page text: 1687389e 2026-11-18 - with the load-below-AO-and-Sound-Overhaul line: "I also approve the vss page text"

## Checks

- simplify: 20db6f0 2026-11-15 - since mod-vss-1.1.0

## Release order

- after fcse-1.4.0
- after aiming-overhaul-1.1.0

## Collisions

- aiming-overhaul: fixed by loading the VSS after Aiming Overhaul - both set the Dart Rifle's scope keys
- sound-overhaul: fixed by loading the VSS after Sound Overhaul - both ship the Dart Rifle's shot banks 004bf5e9 and 004bf5eb
