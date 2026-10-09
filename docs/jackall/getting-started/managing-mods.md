---
slug: /managing-mods
sidebar_position: 4
title: Load order, conflicts and plugins
description: How JackAll combines several mods, what happens when two of them change the same value, and how plugins are installed
---

# Load order, conflicts and plugins

With one mod there's nothing to manage. With five, you start asking which one wins, what happens
when two of them touch the same weapon, and why a mod you just installed does nothing. This page
answers those, using two tiny test mods that both change the AK-47's magazine: one to 40 rounds,
one to 50.

:::info[Same build as the mods confirmed in game]
**Deploy mods** runs the same patch builder that installed VSS Vintorez, Flashlight and Sky Overhaul
for their in-game tests. The 40 and 50 round test mods themselves haven't been played.
:::

## The mod list

![The Mods tab with three mods and the workspace](/img/jackall/managing-mods/01-mods.png)

- **Order.** Mods apply from top to bottom. Select a mod and its arrows appear (1): move it up or
  down. The **workspace**, your own edits, is always last, so your work beats every mod.
- **Active** (2) switches a mod off without forgetting it. Untick it, deploy, and the game is as if
  the mod was never there.
- **Open location** (3) opens the folder the mod's zip is in. For the workspace row, it opens the
  workspace folder itself.
- **Remove** (4) takes the mod off the list. The zip stays where it is on your disk.
- **Rescan mods** (5) reads every zip and the workspace folder again. Use it after you replaced a
  zip or edited a file in the workspace with another program.

Nothing on this tab reaches the game until you click **Deploy mods**.

## How two mods combine

It depends on what they change.

**A whole file**, such as a texture or a sound, is either one mod's or the other's. The lower mod
in the list wins, and the higher one's version of that file is simply not used.

**A value inside an entity**, like the magazine above, is kept per entity. JackAll compares each
mod with the original game and combines the differences:

- Two mods that change **different entities** never meet.
- Two mods that change **different fields of the same entity**, say one changes the AK-47's
  magazine and the other its damage, are merged. You get both changes.
- Two mods that change **the same field to different values** are a real conflict. JackAll won't
  guess which one you meant.

Entities aren't the only files kept in pieces. Which files are, and how to make your mod work next to
others, is on [Containers and fragments](/jackall/fragments).

## When two mods conflict

Click **Deploy mods** with both test mods ticked, and the build stops. The message names the mod,
the entity file and the exact field that clash, here `…/Ammo/iAmmoInClip`.

![The build fails and names the field both mods change](/img/jackall/managing-mods/02-conflict.png)

Your game is not touched when this happens. The new archive is built in a temporary file and only
swapped in when it's complete.

To fix it, decide which value you want and untick the other mod (1). Deploy again (2), and the
status line says **Built patch.dat** (3).

![One of the two mods unticked, and the build goes through](/img/jackall/managing-mods/03-one-off.png)

:::caution[The message suggests a fix that doesn't clear the conflict]
The message says you can save your own version of the entity into the workspace and it will win.
In the current version that is not enough: the build still stops at the two mods. Untick one of
them first. While the conflict is there, the **Archetypes** tab can also show far fewer archetypes
for that world than it has. Once you've unticked one of the mods, restart JackAll to get the full
list back.
:::

Moving one of the mods up or down doesn't fix a conflict either. Order decides between whole files,
not between two values of one field.

## Plugins

A mod can bring an FCSE plugin in a `plugins\` folder. **Deploy mods** copies it into the game's
`bin\plugins\`, and removing the mod (or **Revert to original**) takes it out again. JackAll keeps
a list of the plugin files it put there and never deletes anything else, so plugins you copied in by
hand stay where they are.

One case to know about: if you already copied a plugin by hand to the exact path a mod wants to put
its own, JackAll leaves your file alone and skips the mod's. Delete the hand-copied one and deploy
again. Installing FCSE itself is covered in
[Installing mods with JackAll](/jackall/installing-mods#fcse-and-plugins).

## A mod that does nothing

Go through these in order:

1. Is the mod ticked, and did you click **Deploy mods** after the last change?
2. Does a mod lower in the list replace the same file?
3. Does it change weapon or other entity values? Start a new game. A save keeps many of the values
   it was made with.
4. Click **Check for dead edits**. It finds edits to an archetype the game doesn't read, because
   another library declares the same archetype again and wins.
5. Does it need FCSE, and did you start the game with `FCSE.exe`?
