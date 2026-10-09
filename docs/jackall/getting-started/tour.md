---
slug: /tour
sidebar_position: 2
title: A tour of the window
description: What is where in JackAll's window, which tab does what, and which files JackAll keeps beside itself and in the game folder
---

# A tour of the window

How to get JackAll and point it at the game is on [Installing mods](/jackall/installing-mods). This
page shows what's in the window and what JackAll keeps where.

:::note[Nothing to confirm in game]
This page only looks around.
:::

## The first start

The first time you start it, JackAll asks where the game is. Pick the folder that has
`bin\FarCry2.exe` and `Data_Win32` in it. The dialog opens on the usual Steam folder.

![The folder question on the first start](/img/jackall/tour/01-first-run.png)

JackAll then compares the game's archives with a clean Steam 1.03 game, and warns if they differ.
A GOG install passes this check. Then it reads the archives. That takes a while the first time and
is faster after, because JackAll keeps an index of them.

## The window

![JackAll's window on the Mods tab](/img/jackall/tour/03-window.png)

- **The tabs** (1), starting with **Mods**. The table below says what each is for.
- **The status line** (2) says what JackAll is doing or has just done. Once the game is loaded, it
  counts the files in its archives and how many of them have no known name; the Files tab lists
  those under `_unknown\` (see [Finding any file](/jackall/finding-files)).
- **The theme** (3): System, Light or Dark. System follows the Windows app mode.

## The tabs

| Tab | What it's for | Start with |
|---|---|---|
| **Mods** | The mods you use, their order, and **Deploy mods** | [Installing mods](/jackall/installing-mods), [Load order, conflicts and plugins](/jackall/managing-mods) |
| **Saves** | Your savegames | [Savegames](/jackall/saves) |
| **Files** | Every file of the game: find, look inside, export, replace | [Finding any file](/jackall/finding-files), [Looking inside any file](/jackall/previews) |
| **Map** | The worlds in 3D: look around, move, add and delete objects | [Explore a world](/jackall/map-viewer), [Move, add and delete objects](/jackall/map-editing) |
| **Archetypes** | The game's object definitions: weapons, vehicles, characters and the rest | [Your first mod](/jackall/first-mod), [Which copy does the game read?](/jackall/archetypes) |
| **Animations** | Which animation plays when | [Change which animation plays](/jackall/animations) |
| **AI** | How soldiers see, shoot and behave | [Tune the AI](/jackall/ai) |

Some files open in a tab of their own next to these: the value editor for game data, the MGB editor
for the HUD and menus, and the Domino viewer for missions.

## What JackAll keeps where

Beside `JackAll.exe`:

- `config.ini`: the game folder, your mods in their order, and the theme. A `!` in front of a mod's
  path turns it off without removing it.
- `workspace\`: your own changes, laid out like a mod: game files under `mods\`, plugins under
  `plugins\`.
- `data\`: the `ffmpeg.exe` JackAll converts audio with, and the prefabs you save on the Map tab.

In the game folder:

- `Data_Win32\patch.dat.vanilla` and `patch.fat.vanilla`: a copy of the game's own patch, made by
  the first **Deploy mods**. Every build starts from it.
- `.jackallcache`: the index of the game's archives that makes the next start faster.
- `bin\plugins\`: the FCSE plugins your mods bring.

Mod zips you import stay where they are. JackAll remembers their path, so keep them there.
