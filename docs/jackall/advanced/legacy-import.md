---
slug: /legacy-import
sidebar_position: 1
title: Import an old patch.dat mod
description: Turn a Far Cry 2 mod that ships its own patch.dat and patch.fat into workspace edits with JackAll's Import legacy mod, and trim it to the mod's own changes
---

# Import an old patch.dat mod

Most Far Cry 2 mods on Nexus ship a whole `patch.dat` and `patch.fat`, to copy over the game's own.
That replaces every other mod's patch too, so two of them can't be used together. **Import legacy
mod** compares such a mod with your install's base game and keeps only what differs, as edits in
your workspace that merge with other mods.

The example is scubrah's Skip Intro from Nexus. A new game starts at the hotel in Pala, without the
taxi ride.

:::caution[Not yet confirmed in game]
No mod imported this way has been played yet.
:::

## Import

On the **Mods** tab, click **Import legacy mod** (1) and pick the mod's `.zip`. It doesn't matter
where in the zip `patch.dat` and `patch.fat` sit. The import doesn't add a row to the list; it
stages the changes into your **workspace** (2) and lists them on the right (3). The status line
(4) counts them.

![Skip Intro imported into the workspace](/img/jackall/legacy-import/01-imported.png)

A file that splits into pieces, like an entity library or an input map, comes in as only the pieces
the mod changed. A file the import can only take whole is listed in a notice, because a whole file
outranks every other mod that changes it instead of merging with them:

![The notice about a file taken whole](/img/jackall/legacy-import/02-whole-file.png)

Two other notices can follow. One lists changes a piece can't express, which are left out. The
other lists content the game never reads, such as a declaration that a later one of the same name
replaces, which is left behind.

## Keep only the mod's changes

:::warning[A mod built on another version of the game]
A legacy `patch.dat` is the whole patch of the game the author had, with the mod's changes in it.
If your install's patch is a different one, the differences between the two patches come along as
if the mod had made them. Skip Intro was built on the Steam version. On the GOG install these
screenshots use, it imported 57 files and 1,725 pieces, 1,782 files in all, for a mod that changes 3
files. Deployed as they are, they would replace the GOG version of every one of them.
:::

So read through the workspace before you deploy. Skip Intro's own changes are the two mission
scripts in `domino\user\` and the mouse filter in five action maps of `config\inputactionmapcommon.xml`.
Everything in `generated`, `languages`, `ui` and `worlds` came from the other patch.

**Open location** (1) opens the workspace folder. Delete what isn't the mod's, then click **Rescan
mods** (2). Seven files are left (3).

![The workspace trimmed to Skip Intro's own files](/img/jackall/legacy-import/03-trimmed.png)

A mod's description usually says what it changes. To see every change with its exact place, use
the command line's `legacy analyze`.

## Look at a change

On the **Files** tab, tick **Show only mod files** (1) and pick `master_world1.world1.lua` (2), the
graph that starts a new game. The details show only the changed lines, with a little context.

![What Skip Intro changes in the world 1 master graph](/img/jackall/legacy-import/04-diff.png)

On a new game, the vanilla graph blacks the screen out with `blackscreenfx` and goes on to the taxi
ride, which fades it back in. Skip Intro misspells the effect as `xlackscreenfx`, so the screen
stays clear, and sends the graph straight to the town escape mission (`f_12_Out`). **Open in
Domino Editor…** shows the same script as a graph; see [Read a mission](/jackall/missions).

Then **Deploy mods**. To pass the result on, zip the workspace the way
[Share a mod](/jackall/sharing-a-mod) shows.
