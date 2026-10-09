---
slug: /finding-files
sidebar_position: 1
title: Finding any file
description: Search the game's archives in JackAll's Files tab by name, type, archive or hash, and tell modded and unused files apart
---

# Finding any file

The **Files** tab shows every file of the game's 13 archives as one folder tree, the way the game
itself sees them: when two archives have the same file, you see the one the game loads. Most modding
starts here, with a question like "where is the AK-47's texture?".

:::note[Nothing to confirm in game]
This page only looks at files.
:::

## Search

Type into **Filter** (1). Every word you type has to appear in the name, and three prefixes narrow
it down:

| Word | Keeps | Example |
|---|---|---|
| `ext:<type>` | files of one type | `ext:xbt` for textures |
| `arch:<archive>` | files from one archive, as the **Source** column (3) names it | `arch:patch` |
| `hash:<hex>` | the file with this hash | `hash:585ACE93` |

`ext:xbt ak47` finds the AK-47's textures (2), across every folder.

![The Files tab filtered to the AK-47's textures](/img/jackall/finding-files/01-filter.png)

Or browse: the tree on the left is the folder structure, and clicking a folder lists its files.

## What a file's details tell you

Click a file. On the right you get its size, the archive it comes from, and two things worth
copying: its **hash** (1) and its **path** (2). The game finds files by the hash of their path, so
when a forum post, a crash log or another file mentions a hash, this is how you match it up. Below
are the buttons to export or replace the file, and a preview (3) for most file types.

![The details of the AK-47's texture, with its preview](/img/jackall/finding-files/02-details.png)

Searching by `hash:` (1) goes the other way: it finds the one file (2) behind a hash you have.

![Finding a file by its hash](/img/jackall/finding-files/03-hash.png)

## Only what mods change

Tick **Show only mod files** (1) and the tree (3) and the list keep only what your enabled mods and
your workspace change. The **Source** column then says which mod each file comes from (2). Here,
with VSS Vintorez enabled, it's the two icons the mod replaces.

![Show only mod files with VSS Vintorez enabled](/img/jackall/finding-files/04-only-mods.png)

Without the checkbox, changed files still stand out: they're drawn in full white, while files no mod
touches are grey.

## Files the game never reads

Not everything in the archives is used. There are leftovers from the PlayStation 3 and Xbox 360
versions, files only Ubisoft's editor used, and an unfinished development world. JackAll knows
which ones the PC game never opens: they're in *italics* (1), and selecting one shows a warning
saying why (2). Editing one of them does nothing in game, however long you spend on it.

![An unused console preset in italics, and its warning](/img/jackall/finding-files/05-unused.png)

Tick **Hide unused game files** (3) to leave them out of the tree and the list altogether. Your own
edits are never hidden.
