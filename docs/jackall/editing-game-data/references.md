---
slug: /references
sidebar_position: 2
title: What uses this file?
description: Use JackAll's reference panel to see which files point at a file and which files it points at, and follow them
---

# What uses this file?

Before you replace a model or a texture, you want to know who else uses it. Is it only the AK-47, or
also the golden AK, the pickup and three multiplayer maps? JackAll indexes every reference between
the game's files, so every file you select shows both directions: what points at it, and what it
points at.

:::note[Nothing to confirm in game]
This page only looks at files.
:::

## The reference panel

Select a file on the **Files** tab, here the AK-47's model `graphics\weapons\primary\ak47\ak47.xbg`.
Under the preview there are two lists:

- **Referenced by** (1): the files that name this one. For the AK-47 model that's 130 places: every
  world's entity library, the dependency lists that make sure it's loaded, and so on.
- **References** (2): what this file names in turn. A model names its materials (3).

![The AK-47 model's references in both directions](/img/jackall/references/01-panel.png)

The first time you start JackAll it builds this index in the background, which takes a few minutes.
Until it's done, the lists say they're still indexing.

Each row also says *where* in the file the reference is (**site**, for example the `text_objModel`
field of an archetype) and what kind of value holds it. A row in grey points at something JackAll
can't open as a file, so it can't be followed. The three materials above are like that.

## Follow a reference

Click a row, here world1's entity library (1), and press Enter, or double-click it.

![World1's entity library among the AK-47 model's users](/img/jackall/references/02-user.png)

The details switch to that file (1), with its full path (2). Its own references are under it, so you
can keep going.

![The details of the entity library the reference led to](/img/jackall/references/04-followed.png)

**Alt+Left** takes you back to where you came from, one jump at a time, and **Alt+Right** forward
again, like a browser.

## Search for a referenced hash

Right-click a row for two more options. **Copy hash** puts the reference's hash on the clipboard.
**Filter files by this hash** (1) types `hash:<that hash>` into the filter box, which finds the
file even when it sits somewhere you wouldn't have looked.

![The menu of a reference row](/img/jackall/references/03-menu.png)

The same index is available on the command line as `jackall-cli xref to` and `xref from`.
