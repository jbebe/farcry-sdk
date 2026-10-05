---
title: Two guard posts outside Pala unmanned, one of them torn down
kind: component
bundle: gameplay
status: located
systems: [world, ai]
match:
  - "levels/w1_c_3/generated/worldsectors/worldsector3315.data.fcb/_layout.xml#**"
  - "levels/w1_c_3/generated/worldsectors/worldsector2756.data.fcb/_layout.xml#delete[*]"
exclude: []
requires: []
verified: diff
---

# Two guard posts outside Pala unmanned, one of them torn down

The two guard posts on the roads just outside Pala lose their soldiers, and the farther one is
removed altogether - huts, mounted machine guns, fences, cages, crates, the parked quad and the
pickups. No readme line names it.

## How

Both posts belong to the vanilla layer `missions\_disableformission\pgp_ai`, which the taxi ride
switches off for the opening and the tutorial (`A1BU00`) and `master_world1` switch back on.

- **Sector 3315 post**, `levels/w1_c_3` (around 2280, 2655, some 600 m from Mike's bar), 55
  `_layout.xml` ops: `remove[missions\_disableformission\pgp_ai]` drops the sector's part of the layer, whose six
  entities are all deleted - `Red_Faction.Assault_Caucasian_44`, `_45`, `_46`, the fuel piles
  `PickupPiles.FuelPile_6` and `PGP.FuelPile_160`, and the quad `Land.DLC_Vehicle1_DLC1_0` - and 48
  `main`-layer entities of the post are deleted with them: `Buildings.ShantySm01Dest_27`-`_29`,
  `MountedWeapons.M249_tripod_12` and `_14`, `Infrastructures.FenceWoodWire03_BK_17`-`_20`,
  `CoverObjects.LivestockCage_26`-`_30` and `MetalBarrel01_85`-`_87`, crates, beer boxes, chairs, a
  `Rest.SitOnChair` and a `Rest.SitOnChairAndStandConvo` point, two water bottles and a syrette,
  six `StaticObject_*` and eight `VisualObject_*`. The post's road signs, its other rest and
  mounted-weapon smart points and its special event points stay.
- **Sector 2756 post** (around 2310, 2215, some 300 m from Mike's bar), 3 `delete` ops:
  `Red_Faction.Assault_Caucasian_47` (in `pgp_ai`) and `_48`, `_49` (in `main`, so present even during the opening). The post's huts,
  tripod, Datsun and smart points stay, unmanned.

The quad of the sector 3315 post is moved to Mike's bar by `world-pala-quad`. The sectors' other
objects come back re-encoded (`noise-world-sector-descriptor-rewrap`).

## Depends on

Nothing. The scripts that toggle `Missions/_DisableForMission/PGP_AI` (taxi ride, tutorial,
`master_world1`) keep working on the layer, which still exists elsewhere.

## Compared with other mods

Realism Plus and Scubrah's Patch move these guards into a Functional Outposts respawn group
(`w1_c_3\zXmOLgx`, [`outposts-functional`](../../realism-plus/features/outposts-functional.md));
this mod removes them.

## Uncertain

- The reason is not stated. These are the hostile posts nearest the ceasefire town and the start of
  the game; with the mod's quicker provocation and grenade odds they would meet the player first
  (inference).
- Whether the deleted `main`-layer objects are all the post's own is inferred from their names and
  positions.
