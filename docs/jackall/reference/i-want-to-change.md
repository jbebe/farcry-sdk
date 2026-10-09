---
slug: /i-want-to-change
sidebar_position: 1
title: I want to change…
description: From a thing in Far Cry 2 you want to change to where it lives, which part of JackAll changes it, and the page that shows how
---

# I want to change…

Find what you want to change, and the page that shows how. Every change ends up in your workspace;
**Deploy mods** on the **Mods** tab puts it in the game.

## Weapons, vehicles and other objects

| I want to change… | Where it lives | Page |
|---|---|---|
| A weapon's magazine, damage, accuracy or reliability | The `WeaponProperties.*` archetype, in each world's entity library | [Your first mod](/jackall/first-mod) |
| Any value of a vehicle, a character or another object | Its archetype | [Your first mod](/jackall/first-mod), [The value editor in depth](/jackall/value-editor) |
| A DLC vehicle | The DLC's own entity library, not the world's | [Which copy does the game read?](/jackall/archetypes) |
| One object placed in the world, not every copy of it | The object in its sector | [The value editor in depth](/jackall/value-editor#objects-placed-in-a-world) |
| A weapon's name | `sDisplayName` in its archetype, and the game's text table | [Rename a weapon](/jackall/renaming) |

## Text, pictures, models and sound

| I want to change… | Where it lives | Page |
|---|---|---|
| Text the game shows: shop, manuals, item list, challenges, subtitles | `languages\<language>\oasisstrings.rml` | [Rename a weapon](/jackall/renaming) |
| An icon or another texture | An `.xbt` file | [Replace a texture](/jackall/textures) |
| A model's shape, textures or animations | The `.xbg` model, through a `.fc2model` pack | [Edit a model in Blender](/jackall/blender) |
| A gunshot, footstep or other sound effect | An `.spk` sound bank under `soundbinary\` | [Replace a sound effect](/jackall/sound-effects) |
| Music or a spoken line | An `.sbao` file | [Replace music and speech](/jackall/music) |
| The HUD or a menu | An `.mgb` package under `ui\localized\` | [Edit the HUD and menus](/jackall/hud-and-menus) |

## The world, missions and AI

| I want to change… | Where it lives | Page |
|---|---|---|
| Where an object stands, or add or delete one | The world's sector files | [Move, add and delete objects](/jackall/map-editing) |
| A trigger zone, a light, which object tells which to act | The objects in the world | [Links, triggers, lights and prefabs](/jackall/map-links-prefabs) |
| What happens in a mission | The mission's Lua in `domino\user\`, edited by hand | [Read a mission](/jackall/missions) |
| Which animation plays, or how fast | `graphics\move\movemgr.bin` | [Change which animation plays](/jackall/animations) |
| How far soldiers see, how fast they react, how well they shoot | The soldier and weapon archetypes, and the curves they name | [Tune the AI](/jackall/ai) |
| How often soldiers use a tactic | `engine\gamemodes\gamemodesconfig.xml` | [Tune the AI](/jackall/ai#behaviour-odds) |
| A step in a soldier's decisions | The brain, `scripts\game\newbrains\mercbrain.ai.rml` | [Tune the AI](/jackall/ai#brains) |

## Anything else

| I want to… | Page |
|---|---|
| Find a file first | [Finding any file](/jackall/finding-files) |
| Know what uses a file, before I change it | [What uses this file?](/jackall/references) |
| Replace a whole file of any kind | [Export, replace, mirror and revert](/jackall/export-replace-revert) |
| Change a value an old save keeps | [Savegames](/jackall/saves) |
| Keep part of an old mod that ships its own `patch.dat` | [Import an old patch.dat mod](/jackall/legacy-import), [Take features out of an old mod](/jackall/cli-legacy) |
| Change what the engine itself does | A plugin for FCSE; see [FCSE and plugins](/jackall/installing-mods#fcse-and-plugins) |
