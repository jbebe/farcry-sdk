---
title: Single-player gameplay changes
kind: bundle
claims:
  - "Single-player gameplay changes in the shared game configuration: enemy tactics, loadouts and grenades, reinforcements, faction map, AI routines, hit locations, sprint, HUD, muzzle flash, upgrades, shop stats, one syringe, three cut missions"
includes: [gameplay-adaptive-behaviour, gameplay-ai-loadouts, gameplay-grenade-drops, gameplay-reinforcements, gameplay-map-army, gameplay-routine-odds, gameplay-hit-locations, gameplay-sprint-drain, gameplay-hud, gameplay-muzzle-flash, gameplay-upgrades, gameplay-shop-summary, gameplay-one-syringe, gameplay-restored-missions]
---

Everything in `engine/gamemodes/gamemodesconfig.xml` outside the editor's game mode. These blocks
are shared by every mode, so they change the campaign as much as the editor. Some feed the AI
services [`ai-editor-mode-services`](ai-editor-mode-services.md) adds to the editor; others (HUD,
upgrades, shop, syringes, missions) only matter in the campaign.

Where they come from is unknown. They are not Realism Plus Redux's values.
