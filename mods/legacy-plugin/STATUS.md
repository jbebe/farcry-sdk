# Legacy Plugin

Purpose: The Dunia.dll half of legacy community mods' preference tweaks as FCSE settings that default to the game as shipped; no fixes, no data layer.
Stage: maintenance

## Features

- [done] Briefings keep control - Scubrah's Patch
- [done] Keep weapon on rescue - Scubrah's Patch
- [done] No forced buddy rescues - Scubrah's Patch
- [done] No reputation drop - Scubrah's Patch
- [done] Hide diamond message - Scubrah's Patch
- [done] No blinking pickups - Realism Plus, FC2JackalFix
- [done] No save icons on map - Realism Plus
- [done] Limited saving - Realism Plus, FC2JackalFix
- [done] Brown filter - on by default, as the game ships; Scubrah's Patch, FC2JackalFix
- [done] No hit indicator - FC2JackalFix
- [done] HUD fade-out: always hidden, 1-60 s or always shown - FC2JackalFix
- [done] Skip tutorial pop-ups, or pop-ups and hints - FC2JackalFix
- [done] Aim toggle - FC2JackalFix
- [done] Sprint toggle - FC2JackalFix
- [done] Sprint turn - FC2JackalFix
- [done] No rim lighting - FC2JackalFix
- [done] No sprint and aim blur - FC2JackalFix

## Approvals

- played: c8e9531 2026-10-15 - the user checked all 17 tweaks in game: they work
- page text: dd2c12c 2026-11-18 - the user read nexus-description.bbcode and nexus-summary.txt: OK

## Checks

- simplify: 20db6f0 2026-11-15 - since legacy-plugin-1.0.0

## Release order

- after fcse-1.5.0

## Collisions

- 0x0071AE70: stacks - the input dispatcher every mod with a control mid-hooks; FCSE stacks same-address hooks
- aiming-overhaul: stacks - both mid-hook the sprint press (0x00144630, Steam); Aiming Overhaul skips it down a scope while the breath is held, the sprint toggle only times the keyboard press, and a skipped press leaves no sprint to latch; FCSE stacks same-address hooks
