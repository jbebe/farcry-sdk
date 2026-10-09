---
slug: /glossary
sidebar_position: 3
title: Glossary
description: The words JackAll and these pages use for Far Cry 2's files and for modding them, each with a link to where it's explained
---

# Glossary

**Archetype.** A named definition with all of its settings: a gun, a jeep, a guard. Everything you
see in the game is one, and each campaign world has its own library of them.
[Your first mod](/jackall/first-mod#1-load-a-worlds-archetypes)

**Archive.** The game keeps its files packed in archive pairs, a `.fat` index and a `.dat`, and finds
each file by a hash of its path. The **Files** tab shows them all as one tree.
[Finding any file](/jackall/finding-files)

**Area.** One self-contained piece of a menu or the HUD, such as a button, a page or a panel, with
its elements inside. [Edit the HUD and menus](/jackall/hud-and-menus#2-find-your-element)

**Behaviour odds.** How often soldiers use an optional tactic, in percent, by how far the campaign
has come. [Tune the AI](/jackall/ai#behaviour-odds)

**Brain.** A soldier's decision tree, in `scripts\game\newbrains\`. **mercbrain** drives every
soldier. [Tune the AI](/jackall/ai#brains)

**Component.** One part of an entity with one job: its model, its animation, its sounds, its weapon
behaviour. [The value editor in depth](/jackall/value-editor#the-layout)

**Conflict.** Two mods that set the same field to different values. **Deploy mods** stops and names
it. [When two mods conflict](/jackall/managing-mods#when-two-mods-conflict)

**Container.** A game file JackAll keeps in pieces, such as an entity library, the text table or a
HUD package, so a mod ships only the pieces it changes. [Containers and fragments](/jackall/fragments)

**Curve.** A setting that maps an input to a value, such as hit chance by distance, as points
joined by straight lines. [Tune the AI](/jackall/ai#curves)

**Dead edit.** A change to a copy of an archetype the game never reads, because a later library
declares the same archetype again and wins. **Check for dead edits** finds them.
[Which copy does the game read?](/jackall/archetypes)

**Deploy mods.** Builds your mods and your workspace into the game's `patch.dat`, always starting
from the vanilla backup. Nothing reaches the game until you click it.
[Installing mods](/jackall/installing-mods#4-deploy)

**depload.** A world's list of what every resource needs loaded with it. A new weapon or model has
to be added there, or the game won't load it. [Looking inside any file](/jackall/previews)

**Domino.** The visual scripting tool Far Cry 2's missions were built in. The game ships only the
Lua it generated, in `domino\user\`; JackAll shows it as a graph again.
[Read a mission](/jackall/missions)

**Entity library.** A file of archetype declarations, one per world and more for the DLC. A later
library replaces an earlier one's archetype by name. [Which copy does the game read?](/jackall/archetypes)

**Event link.** One object telling another to act when something happens: an **output** on the
sender and an **event** on the target. [Links, triggers, lights and prefabs](/jackall/map-links-prefabs#triggers-and-links)

**FCSE.** Far Cry Script Extender, a second way to start the game that first loads plugins from
`bin\plugins\`. [FCSE and plugins](/jackall/installing-mods#fcse-and-plugins)

**`.fc2model`.** A model pack: a model with everything it needs, in a form the Blender add-on opens.
[Edit a model in Blender](/jackall/blender)

**File override.** A mod's copy of a whole game file. The lowest mod in the list that has one wins.
[How two mods combine](/jackall/managing-mods#how-two-mods-combine)

**Fragment.** A small file holding one piece of a container, such as one archetype of an entity
library, one area of a menu or one situation of the animation graph. Mods that change different
pieces of the same file merge. [Containers and fragments](/jackall/fragments)

**Hash.** The number the game computes from a file's path, or from a name, and stores instead of the
text. [What a file's details tell you](/jackall/finding-files#what-a-files-details-tell-you)

**Instance.** One object placed in the world. It keeps only the fields where it differs from its
archetype. [Objects placed in a world](/jackall/value-editor#objects-placed-in-a-world)

**Layer.** A mod's zip, or a folder laid out the same way. The workspace is one too.
[Manage mods from the command line](/jackall/cli-mods#build)

**Legacy mod.** An old mod that ships a whole `patch.dat` and `patch.fat` to copy over the game's
own. **Import legacy mod** turns it into workspace edits.
[Import an old patch.dat mod](/jackall/legacy-import)

**Load order.** The order of the **Mods** tab's list. Mods apply from top to bottom.
[The mod list](/jackall/managing-mods#the-mod-list)

**Magma.** The game's UI system. Every screen, from the main menu to the HUD, is a Magma package,
an `.mgb` file under `ui\localized\`. [Edit the HUD and menus](/jackall/hud-and-menus)

**Mission layer.** A group of placed objects. `main` is always there; the others appear only during
a mission. [Mission layers](/jackall/map-editing#mission-layers)

**`mods\` and `plugins\`.** The two folders of a mod: game files on their real paths, and FCSE
plugins. [Open the workspace folder](/jackall/sharing-a-mod#1-open-the-workspace-folder)

**MOVE.** The rule graph in `graphics\move\movemgr.bin` that decides which animation plays: for
every situation, a list of rules. [Change which animation plays](/jackall/animations)

**Navmesh.** Where the AI can walk. [Explore a world](/jackall/map-viewer#choose-whats-drawn)

**oasisstrings.** The game's text table, `languages\<language>\oasisstrings.rml`, with every text
it shows. [Rename a weapon](/jackall/renaming)

**Plugin.** A `.dll` or `.lua` file that changes the running game. Only FCSE loads plugins.
[FCSE and plugins](/jackall/installing-mods#fcse-and-plugins)

**Prefab.** A group of objects that moves, turns, copies and deletes as one.
[Prefabs](/jackall/map-links-prefabs#prefabs)

**Purge.** Writes a copy of a save without its stored entities, so the copy picks up your mods.
[Savegames](/jackall/saves#purge-let-a-save-pick-up-your-mods)

**References.** Which files name a file, and which files it names.
[What uses this file?](/jackall/references)

**Sector.** A piece of a world. Each placed object is filed in one sector's file.
[Move, add and delete objects](/jackall/map-editing)

**Sound bank.** An `.spk` file with short sounds and how they play.
[Replace a sound effect](/jackall/sound-effects)

**Unused files.** Files in the archives the PC game never opens. Editing one does nothing in game.
[Files the game never reads](/jackall/finding-files#files-the-game-never-reads)

**Vanilla backup.** `patch.dat.vanilla` and `patch.fat.vanilla` in `Data_Win32`, the copy of the
game's own patch the first deploy makes. Every build starts from it, so don't delete it.
[Installing mods](/jackall/installing-mods#4-deploy)

**Workspace.** The folder next to JackAll where your own changes live. It's always last in the mod
list, and it is your mod. [Package and share your mod](/jackall/sharing-a-mod)

## File types

| Extension | What it is | Page |
|---|---|---|
| `.fat` / `.dat` | An archive pair | [Finding any file](/jackall/finding-files) |
| `.fcb` | Game data: entity libraries, world sectors and more | [The value editor in depth](/jackall/value-editor) |
| `.rml` | Binary XML, such as the text table | [Rename a weapon](/jackall/renaming) |
| `.xbt` | A texture: a DDS image with a game header | [Replace a texture](/jackall/textures) |
| `.xbg` | A model | [Edit a model in Blender](/jackall/blender) |
| `.xbm` | A material: shader, textures and parameters | [Looking inside any file](/jackall/previews) |
| `.mab` | An animation bank | [Looking inside any file](/jackall/previews) |
| `.sbao` | Streamed music or speech | [Replace music and speech](/jackall/music) |
| `.spk` | A sound bank | [Replace a sound effect](/jackall/sound-effects) |
| `.mgb` | A menu or HUD package | [Edit the HUD and menus](/jackall/hud-and-menus) |
| `.sdat` | A terrain sector | [Looking inside any file](/jackall/previews) |
| `.ai.rml` | A brain | [Tune the AI](/jackall/ai#brains) |
| `.sav` | A savegame | [Savegames](/jackall/saves) |
