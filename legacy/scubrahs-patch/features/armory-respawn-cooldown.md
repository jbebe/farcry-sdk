---
title: Armory weapon respawn cooldown
kind: component
bundle: balancing
claims:
  - "Added weapon respawn cooldown period (after grabbing a weapon from any armory, it will not respawn for 1 hour - choose your loadout carefully)"
status: located
systems: [economy, weapons]
match:
  - "_hash/{07d356ff,40732c2f}.lua"
  - "worlds/*/generated/world*.omnis.fcb/dominoomnientity_weaponrespawncooldownmanager_w?.*.xml"
exclude: []
requires: [outposts-delay-presets]
verified: diff
---

# Armory weapon respawn cooldown

Taking a weapon from an armory crate puts that weapon on cooldown in every armory of the world:
its crates are removed and do not come back until `WeaponRespawnDelay` seconds have passed (3600 s,
one hour, by default) and the player has left and re-entered the area.

## How

- Two new omni entities, `DominoOmniEntity_WeaponRespawnCooldownManager_W1` in `world1.omnis.fcb`
  and `_W2` in `world2.omnis.fcb`, run `domino\User\weaponcooldownmanager_w1.lua` / `_w2.lua`,
  stored nameless as `_hash/07d356ff.lua` (header "Bazaar Weapon Respawn Cooldown Manager
  (World1)") and `_hash/40732c2f.lua` ("... (World2)").
- Each script keeps one flag per weapon type (32 of them, the IED, the golden AK-47 and the three
  DLC weapons among them) and a `PickupMissionItem` listener on that weapon's crate in each of the
  world's five armories.
- A pickup sets the weapon's flag and starts a `Delay` with `Seconds = "Weapon"`. When it elapses
  the flag clears and the five listeners are enabled again.
- A 0.1 s `Delay` loops `BazaarDoorUsed`, which calls `RemoveEntity` on all five crates of every
  weapon whose flag is set, so a fresh crate never stays in the world during the cooldown. The
  script also binds the ten armory doors (`Door` boxes, `OnPush`/`OnPull`), but their
  `StartListening` calls are commented out; the loop does the work instead.

## Depends on

- `outposts-delay-presets` turns `Seconds = "Weapon"` into
  `Globals.MASTER_GameGlobals.WeaponRespawnDelay`, whose default `3600` sits in the shared globals
  hunk `domino/user/master_gameglobals.globals.lua@L90` and which `ScubrahsPatch.lua` can change
  (User Configurable); neither is claimed here.
- The crate ids for the golden AK-47 and the DLC weapons are the mod's own new crates (weapons
  bazaar shop features). Without them those listeners name entities that do not exist; the rest
  still works (inference).
