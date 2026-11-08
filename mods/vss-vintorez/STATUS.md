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
- [new] Wood on its own material (DART_RIFLE_METAL is free) for a wood-only highlight - open option, not picked
- [new] A normal map through a Generic material - open option, not picked; loses the worn look
- [scrapped] The multiplayer dropped pickup - skipped on the single-player rule, it still has no barrel

## Approvals

- played: 276998b 2026-10-09 - with Aiming Overhaul: "everything works perfectly" (the PSO-1 from its own data)
- page text: 83ea468 2026-10-09 - implied by the mod-vss-1.1.0 release; changed since in 299b0f4 to ab21b3d

## Checks

## Release order

- after fcse-1.4.0
- after aiming-overhaul-1.1.0

## Collisions

- aiming-overhaul: fixed by loading the VSS after Aiming Overhaul - both set the Dart Rifle's scope keys
