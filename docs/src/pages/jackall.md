---
title: JackAll
description: Introduce JackAll, the Far Cry 2 mod installer, file explorer and editor
---

# JackAll

Far Cry 2 keeps everything inside 13 packed archives. That's around 215,000 files, and the game
finds them by a hash of the filename instead of the name itself. The only archive a mod is allowed
to touch is `patch.dat`, so installing a mod means rebuilding that archive.

JackAll opens all of it. It shows every archive as one big file tree, decodes the files into
something you can actually read and edit, and packs your changes back into a `patch.dat` that the
normal, unmodified game loads. No DLL, no injection, nothing running inside the game process.

The name is for the Jackal, and for "jack of all trades". One tool for the whole job instead of six
converters and a batch file, which is what we had before.

**[Download the latest release](https://github.com/jbebe/farcry-sdk/releases?q=jackall&expanded=true)**.
`jackall-<version>.zip` is the app, `jackall-cli-<version>.zip` is the command line version. Both are
a single exe. You don't need .NET installed and there is nothing to set up.

Source code, if you want to check what it does:
[`tools/JackAll`](https://github.com/jbebe/farcry-sdk/tree/main/tools/JackAll).

## What it can do

### Mods

![JackAll's Mods tab](/img/screenshots/jackall-mods-tab.png)

This is your load order. Mods apply from top to bottom, and if two of them change the same file the
lower one wins. Every mod has a checkbox, so you can turn one off without deleting it. A mod here is
just a folder or a zip with game paths inside it (`worlds\world1\generated\…`), which is exactly what
you get when you unpack an archive, so old community mods work as they are. If a mod is the old kind
that ships a full replacement `patch.dat` and `patch.fat`, and most FC2 mods are, use **Import legacy
mod**: it compares that mod to a clean game and keeps only the files it really changed.

**Deploy mods** builds everything into the game, **Revert to original** puts the clean archive back.
You can't wreck your install by clicking too much. JackAll saves your original `patch.dat` once as
`patch.dat.vanilla`, and every build starts from that backup again, never from the file that happens
to be there. So building twice gives you the exact same bytes, turning a mod off really removes it,
and a failed build leaves the game untouched. If two mods change different parts of the same file
they get merged instead of one of them winning silently. **Check for dead edits** finds edits that a
later entity library overrides, which is the kind of change that looks fine in the tool and does
nothing in game. A mod can also carry a `plugins\` folder, and that one goes to `bin\plugins\` for
[FCSE](/fcse) instead of into the archive.

### Files

![JackAll's Files tab](/img/screenshots/jackall-files-tab.png)

All 13 archives merged into one tree, resolved in the same order the game resolves them. Over
150,000 files, and anything a mod changed is highlighted, so you can see what you actually touched
instead of guessing. Click a file to preview it, export it, replace it or revert it. A modded file
can be diffed against the original, and **Export original** always gets you the clean version back.
The filter box matches any word, and you can narrow it with `ext:`, `arch:` and `hash:`.

More than 50,000 files in the game have no known name, only a hash. You can still mod those. JackAll
guesses the type from the first bytes, puts them under `_unknown\`, and saves your edit as
`_hash\<crc32>.<ext>`, which the builder writes back at exactly that hash. Every file also gets a
references panel: what points at this file, and what this file points at, both clickable. For a file
nobody can decode, that is usually the only real information you get about it.

### The value editor

![The FCB value editor](/img/screenshots/jackall-fcb-editor.png)

Almost everything people want to mod (weapons, AI, prices, vehicles, patrols) sits in `.fcb` files.
JackAll decodes them into a normal property tree with real field names and types, so you edit values
and not hex, and not XML either. Big files like an entity library are split into one piece per
entity, so opening a weapon opens that weapon instead of a huge document. Nothing is written until
you press save.

When you save, only that one entity piece lands in your workspace, as a small file. That is the
thing that lets two mods live together, and it's explained in [How to use](#how-to-use) below. The
same editor also opens the `PersistenceDB` inside a savegame, because a save is the same kind of
file.

### Textures, audio and models

![Texture, audio and mesh previews](/img/screenshots/jackall-asset-previews.png)

Most file types get a real viewer instead of a hex dump. `.xbt` textures show a preview and split
into a `.dds` plus a small XML header, and you can build that pair back into a working `.xbt`. `.xbm`
materials show the shader, every texture slot and every parameter. `.xbg` models get a 3D preview
where you pick the LOD and the damage state, and RealTree species draw as actual trees. `.sbao` audio
plays right there, exports as ogg or mp3, and imports basically any audio file you have: ffmpeg comes
with the app and converts it to the 48 kHz stereo ogg the engine wants.

`.spk` banks are grouped, one row per sound with its settings under it, and you can play, export and
replace the audio in each record. The game uses two codecs in there (ogg vorbis and IMA ADPCM) and
JackAll figures out which one it's looking at. Everything you change here goes into your workspace
first. Nothing reaches the game until you press Deploy.

### Model export for Blender

![Exporting a model as .fc2model](/img/screenshots/jackall-fc2model-export.png)

**Export as .fc2model** on any `.xbg` collects the model, its materials, its textures, the skeleton
next to it and, if you want, every animation bank that uses it. All of that goes into one file with
zero Dunia formats inside: JSON and plain float arrays for the mesh, JSON for the materials, PNG for
the textures. **Apply .fc2model** takes the file back from your 3D editor and stages the changed game
files into your workspace, and it shows you the list of files it will touch before it does anything.

The split is on purpose. JackAll knows the byte layouts, and the Blender add-on
([`tools/BlenderFC2`](https://github.com/jbebe/farcry-sdk/tree/main/tools/BlenderFC2)) knows what a
scene should look like. The add-on has no format code in it at all. In practice this means the art
half of a custom weapon is one file and one plugin, instead of twenty small scripts. See
[Adding a weapon](/docs/modding/adding-a-weapon) and [`.fc2model`](/docs/file-formats/fc2model).

### Map

![The Map tab](/img/screenshots/jackall-map-tab.png)

The Map tab loads a world and lets you fly around it in 3D. On the side you get the list of layers a
FC2 map is built from, and you can toggle each one. The terrain draws with its real textures and its
baked lighting, plus water, roads, rivers, footpaths, the drawn zone outlines, and the vegetation of
every sector with each plant as its own model. Placed objects draw as real models or as markers, you
can filter them by mission layer, search them, and click them either in the list or in the viewport.

Everything without a model gets its own symbol so you can still find it: lights in their own color,
trigger boxes as wireframe, AI cover and guard spots, door and window hints for the AI, particle and
sound emitters, and the navmesh nodes the AI walks on. Right now it's a viewer. You can select
things, read their values and drag a move gizmo, but the gizmo only lives until you close the app.
Nothing gets saved yet.

### Library

![The Library tab](/img/screenshots/jackall-library-tab.png)

Every object placed in the world is a small difference on top of an archetype, and archetypes are
declared in several libraries that override each other in a fixed order. The Library tab resolves
that for you, so the definition you edit is the one the game really reads. If a later library
overrides it, you see straight away that it's dead instead of losing an hour on it. The whole
override chain is listed next to the archetype and you can open each layer on its own. Editing opens
the normal value editor on the piece where the winning definition lives.

### Saves

![The Saves tab](/img/screenshots/jackall-saves-tab.png)

The Saves tab finds your `.sav` files and shows the in game screenshot, the world, your character
name, the save time, the DLC it uses and how many objects it stores. The `PersistenceDB` inside opens
in the same value editor as everything else and writes straight back into the `.sav`, so a value that
got stuck is fixable without starting the campaign over.

It's also here to explain something that has confused FC2 modders for about fifteen years: a save
stores values that overlap with almost every entity library, and an `.fcb` mod cannot override what
the save already has. That's why a data mod usually needs a new game. Being able to see what your
save is holding beats guessing.

### Menus and mission logic

![The .mgb editor and the Domino graph viewer](/img/screenshots/jackall-mgb-domino.png)

`.mgb` files are the game's own UI format. JackAll decodes one into a full tree with a property grid,
so every widget, action, state and color is reachable. The "Add" list only offers the classes the
engine itself accepts in that spot, and saving rewrites the whole package, so adding an element or
declaring a new class is a normal edit instead of an impossible one. This is what makes a custom
options page inside the game possible at all. See [Magma UI](/docs/category/magma-ui).

Domino is the mission scripting system, and it ships as generated Lua. JackAll rebuilds a script from
`domino\user\` into the box and wire graph it was made in, lays it out for you, takes the pin names
from the node scripts and the original names from the `*.debug.lua` file next to it. It's read only
for now: you can see what a mission does, you just can't change it here yet.

## Format support

Everything below is JackAll's own code, one implementation per format, tested against the files the
game actually ships. The format notes are under [File Formats](/docs/category/file-formats).

| Format | What it is | What JackAll does |
|---|---|---|
| `.fat` / `.dat` | The archive pair, LZO compressed, indexed by hash | Reads every entry, rebuilds `patch.dat`/`patch.fat`. Every shipped `.fat` comes back byte for byte identical |
| `.fcb` | Binary object trees: entities, weapons, AI, world sectors | Decodes and encodes, splits into one piece per entity, property tree editor, merges edits coming from different mods |
| `.rml` | Binary XML, used for manifests and the translated text table | Decodes and encodes, text editor with a diff against the original |
| `.mgb` (+ `.mgb.desc`) | Magma UI packages: menus and HUD | Full decode and encode plus a real editor. `verify` checks that a package only uses names it declares |
| `.xbt` (+ `.dds`) | Textures, block compressed, with a streaming header | Splits and rebuilds, decodes BCn to pixels for previews and model packs, encodes back with the same compression |
| `.xbm` | Materials: shader, texture slots, parameters | Fully decoded and shown, written back through a model pack |
| `.xbg` (+ `.xbgmip`) | Models, with LODs, damage states and skinning | Decodes and encodes, 3D preview, `.obj` export, editable through a model pack |
| `.skeleton` | Skeletons | Reads and writes, byte identical on every shipped file |
| `.mab` | Animation banks | Reads them, writes one clip back and leaves the rest of the bank untouched |
| `.rtx` | RealTree, the procedural trees | Decoded into branches and leaves, drawn in the map and model viewers |
| `.fc2model` | JackAll's own model pack: JSON and PNG, no Dunia bytes | Written and read. The only thing the Blender add-on ever sees |
| `.sbao` | Streamed audio: music and dialogue | Splits into header and ogg, plays it, exports ogg or mp3, imports anything ffmpeg reads |
| `.spk` | Sound banks | Lists and groups the records, extracts and imports ogg vorbis and IMA ADPCM |
| `.sdat` | Terrain per sector: heights, surface types, water, baked light | Read only. It's what the map viewport draws, plus a grayscale height preview per file |
| `.nvm` | AI navmesh | Read only: the walkable nodes and the normal their slope is checked against |
| `.sav` | Savegames | Reads the header, screenshot and DLC list, edits and writes the `PersistenceDB` |
| `depload.dat` | Dependency list per asset | Parsed. Feeds the reference panel and has its own view |
| Domino `.lua` (+ `.debug.lua`) | Generated mission scripts | Parsed back into a node graph, with pin names and the original editor names |
| `.png` | Not a game format | Own encoder and decoder, used for model pack textures |
| `.xml`, `.lua`, `.desc` | Plain text files | Highlighted view, with a diff against the original when modded |

These are only recognized, not decoded. You can still browse, export and replace them, they just stay
raw bytes: `.hkx` (Havok collision), `.bik` (video), `.feu` (old UI), `.wem`, shader binaries, `.srl`
and `.zsr` (streaming data), `.luab` and `.luac` (compiled Lua), `.loc`, `.material.bin`, `.sctr`,
`.tree`, `.cbatch`, `.terrainnode.bdl` and `.dpax`.

## Where it's going

JackAll started as a mod installer, and that part has moved out of the way. The install pipeline is
now a **[Vortex extension](https://www.nexusmods.com/site/mods/2143)**, so FC2 mods install from
Nexus like they do for any other game. The extension is only a front end, not a second tool: Vortex
does the downloading, staging, enabling and load order, and every step that needs the real game
archives calls JackAll's command line. That way the mod rules exist in one place and can't drift
apart. If you only want to install mods, use Vortex. JackAll is what you want when you're making one.
The pipeline is written up on the [Vortex page](/docs/modding/vortex).

The rest grew way past "file manager". There's a real 3D map editor forming over the actual worlds, a
real model exporter with its own file format and a Blender add-on on the other side of it, a visual
editor for Domino missions, an archetype browser that resolves the override chains, and a savegame
editor. A few of those don't exist anywhere else for this game, in any form. Some are still viewers,
and the plan is that each one gets its write path, in the order the [roadmap](/todos/roadmap) argues
for.

None of this is really specific to Far Cry 2. Dunia is Ubisoft's engine across a decade of games and
the formats are close relatives: an `.fcb` is an `.fcb`, an archive is an archive, a mesh is more or
less the same mesh. Nothing stops someone from teaching JackAll Far Cry 3, 4, 5, 6 or whatever number
they're on when you read this. That's more work than one person can do, which is exactly why the
source is public.

## How to use

**First run.** Point JackAll at the folder that has `bin\FarCry2.exe` in it. It checks your archives
against the hashes of a clean 1.03 install and tells you if something is already different. It still
works in that case, but then "original" means whatever you gave it. The tool writes three things next
to the exe: `config.ini` (your game folder and mod list, you can edit it by hand), `data\`
(dictionaries and caches, don't touch it), and `workspace\`.

**The workspace is where your edits live.** Everything you change inside JackAll, a texture, an
entity, an applied model, gets written into `workspace\` as a normal file on its real game path. It's
always last in the mod list, so your own work wins over every mod under it, and you can switch it off
with the checkbox like any other mod when you want to see the game without it. There is no export or
package step: **the workspace is the mod**. Zip what's inside it and that zip is a mod anyone can
drop into JackAll or install through Vortex.

**Edits are saved per entity, not per file.** This is where JackAll works differently from the old
Gibbed way. With Gibbed's tools you convert a whole `.fcb` to XML, edit it, and ship the whole file
back, which means two mods that both touch the entity library always conflict, even when one changed
a rifle and the other changed a jeep. JackAll splits the file into one piece per entity and saves
only that piece, on a path like `entitylibrary.fcb\vehicle\land\jeep.xml`. Two mods on different
entities never meet. Two mods on different fields of the same entity get merged against the original
version. Only a real conflict, same field with two different values, needs you, and then JackAll
shows it as a conflict instead of quietly picking one.

**Two editors are read only right now.** The Map tab draws the world and lets you select things and
read their values, and the move gizmo only lasts until you close the app. Nothing goes back into the
files yet. The Domino editor rebuilds a mission graph so you can read it, and there's no way back to
the Lua. Both are still worth having for understanding the game, and they say viewer on purpose, so
nobody spends an evening editing and then loses it.

**One thing worth repeating.** A savegame keeps values that overlap almost every entity library, and
an `.fcb` mod cannot override what the save already stored. If your data mod looks like it does
nothing, start a new game before you decide the mod is broken.

## CLI

`jackall-cli.exe` is the same thing without a window. It's a separate download, most of the format
commands don't even need the game installed, and every `mod` command takes `--json`: one JSON object
on stdout, progress on stderr, so your script never has to read text meant for humans.

### Mod commands

The same code the Mods tab runs.

| Command | What it does |
|---|---|
| `mod status --game <dir>` | Tells you if a folder is a Far Cry 2 install and what state its patch archive is in |
| `mod inspect <folder or zip> --game <dir>` | Tells you if an archive is a normal mod layer or an old full patch mod, and where its file tree starts |
| `mod import-legacy --game <dir> --from <zip> --out <dir>` | Turns an old style full `patch.dat`/`patch.fat` mod into a normal layer folder |
| `mod lint --game <dir> --layer <dir>` | Lists archetype edits that a later entity library overrides, so they do nothing in game |
| `mod build --game <dir> --layer <a> --layer <b>…` | Builds the clean patch plus your layers into the game's archive pair. Order matters, later layers win |
| `mod restore --game <dir>` | Puts the clean `patch.dat`/`patch.fat` back and undoes every build |

```
jackall-cli mod build --game "C:\Games\Far Cry 2" --layer mods\bettersights --layer workspace
```

### Format commands

| Command | What it does |
|---|---|
| `archive extract <file.fat> [--names] [--filter <s>]` | Unpacks and decompresses an archive. `--names` turns hashes back into real paths |
| `fcb decode` / `fcb encode` | An `.fcb` object tree to XML and back |
| `rml decode` / `rml encode` | A binary `.rml` to plain XML and back |
| `mgb decode` / `mgb encode` / `mgb verify` | A Magma UI package to editable XML and back. `verify` checks it only uses names it declares |
| `xbt extract` / `xbt build` | Splits an `.xbt` into `.dds` plus header XML, and puts it back together |
| `xbg export <mesh.xbg>` | Converts a model's geometry to a Wavefront `.obj` |
| `fc2model export` / `extract` / `inspect` | Builds a model pack, writes a changed pack back out as game files laid out as a mod layer, or lists what's in a pack |
| `sbao extract` / `sbao build` | Splits an `.sbao` into `.ogg` plus header, and puts it back together |
| `spk list` / `spk extract` / `spk import` | Lists a sound bank, pulls one record out as `.ogg`/`.wav`, or replaces one |
| `xref build` / `xref to` / `xref from` | Indexes every hash reference in the game, then answers what points at a file and what a file points at |

```
jackall-cli fc2model export graphics/weapons/primary/ak47/ak47.xbg -g "C:\Games\Far Cry 2" --clips
jackall-cli xref to "graphics\_common\weapons\ak47\ak47_d.xbt" --game "C:\Games\Far Cry 2"
```

### Who it's for

The CLI is for people who want Far Cry 2 in their own toolchain: a build script that rebuilds your
mod from source assets, a CI job that checks a model still round trips, a batch run over a few
thousand textures, or a completely different mod manager. The JSON is the stable part. stdout is the
result, stderr is progress, and a failure is `{"ok":false,"error":"…"}` with exit code 1, never a
random line on the wrong stream.

Vortex is the proof that it works. It drives `jackall-mi.exe`, a smaller build with only the four
commands an installer really needs, and its JSON is identical to `jackall-cli`'s `mod` commands. So
the extension can use either one, and neither of them is a rewrite of the other.

## A word on intellectual property

JackAll's `.fcb`, `.rml` and `.fat` decoding stands on ten years of community reverse engineering:
Gibbed's original Dunia tools, wobatt's better hash decoding, and every forum post and Discord thread
that ever pasted a working piece of format knowledge into the void.

So, to everyone who ever uploaded code, a hex dump, or a "here's how the header works" post that this
tool's understanding of the format goes back to: thank you for your service.
