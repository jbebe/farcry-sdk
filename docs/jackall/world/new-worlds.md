---
slug: /new-worlds
sidebar_position: 3.5
title: Make a new world
description: "Make a Far Cry 2 world of your own with JackAll: New map on the Map tab, edit it like any world, then build it for the game with world pack"
---

# Make a new world

A new world starts as a copy of `tmpla`, the editor's flat template, under a name of your own. You
edit it on the **Map** tab like any other world, then **pack** it: one command builds the files the
game needs from your edits, so installing the world is only copying files.

:::caution[Not yet confirmed in game]
An earlier build of `testrange`, made with the same steps by hand, loaded in game with its walls and
soldiers fighting on its navmesh. Nothing made with New map and `world pack` has been played yet.
:::

## Create

On the **Map** tab, click **New map…**, give the world a name, pick a size, and **Create**.

- The name is lowercase letters, digits and underscores, starting with a letter.
- The size is one 64 m sector, or 8 × 8 sectors (512 m). Sizes in between would need every sector
  renumbered, and that is not done yet.

JackAll writes the world's files into your workspace and opens it. They are real game files, copied
from `tmpla` as the game ships it and renamed:

- the world's descriptor, managers, map data and sector list;
- each sector's terrain, textures and sound regions;
- one spawn point, `SpawnPoint_0`, standing on the ground in the middle.

The same from the command line:

```
jackall-cli world new myworld --side 8 --game "C:\Games\Far Cry 2" --out myworld-layer
```

## Edit

Everything on [Move, add and delete objects](/jackall/map-editing) works here. Every sector takes
objects, though none has a sector file yet. **New ▸ Archetype…** offers the whole archetype set, not
only what one world uses. **Save** writes your edits into the workspace as small files, the way it
does in any world.

## Pack

The game needs whole files where the workspace has pieces. `world pack` builds them:

```
jackall-cli world pack <workspace> --game "C:\Games\Far Cry 2" --out myworld-packed
```

For every new world in the layer it writes:

- **each sector's file**, every sector of the grid, empty ones too;
- **the entity library**, cut from `tmpla`'s full set of 5,735 archetypes, the way the campaign
  worlds' libraries are: every archetype your objects name, everything those name in turn, and the
  groups the game asks for by name wherever it is played (the player, weapons and their data,
  hand-held props, cameras, soldiers, vehicles and a few more);
- **the sector list**, with every sector flagged so the game loads its files and its navmesh;
- **the navmesh**, over the terrain and every object's collision, with cover points along walls and props.

Everything else in the layer is copied as it is. Point `--out` at a new or empty folder.

## Deploy and visit

Deploy the packed layer like any mod:

```
jackall-cli mod build --game "C:\Games\Far Cry 2" --layer myworld-packed
```

**Deploy mods** on the **Mods** tab leaves an unpacked new world's pieces out, and says so; the
rest of the workspace deploys as usual.

The game has no menu for a new world. Load any save, press `~`, and travel to the spawn point:

```
#GameChangeWorld("myworld", "2054037628277228842")
```

## Change another mod's world

A mod that changes a world another mod made works like a mod of a campaign world: its edits are
pieces, merged onto that mod's packed files when both are deployed. Pack it with the other mod as
its base, so it gets the navmesh of the sectors it changes:

```
jackall-cli world pack <workspace> --base myworld-packed --game "C:\Games\Far Cry 2" --out changes-packed
```

A sector's navmesh does not merge. If two mods change the same sector, the one deployed later wins,
and the other's objects are missing from that sector's navmesh.

## Limits

- The navmesh drops ground off the terrain that nothing could reach, such as a rooftop or the top of
  a rock. Place a soldier, an animal or a smart-terrain point on it to keep it.
- Terrain, roads, rivers and vegetation are copied from `tmpla` and cannot be edited yet.
- The files JackAll writes are larger than the game's own, because it does not share repeated
  values the way the game's tools did. The game reads them all the same.
