---
title: JackAll
description: Introduce JackAll, the Far Cry 2 mod installer, file explorer and editor
---

# JackAll

Far Cry 2 keeps everything it is inside thirteen sealed archives — roughly 215,000 entries, indexed
by a hash of a filename Ubisoft never shipped. There is no loose-file override, no plugin list and
no `~mods` folder: the only archive a mod may touch is `patch.dat`, and "installing a mod" means
recompiling it.

JackAll is one program that opens all of that. It mounts the whole set as a single browsable
filesystem, decodes what it finds into something a person can read and change, and compiles the
result back into an archive pair the **stock, unmodified engine loads** — no DLL, no injection,
nothing running inside the game process. The name is for the Jackal, and for *jack of all trades*:
one tool that covers the whole job instead of the half-dozen single-purpose converters and a `.bat`
file the community has had to string together until now.

**[Download the latest release](https://github.com/jbebe/farcry-sdk/releases?q=jackall&expanded=true)**
— `jackall-<version>.zip` is the app, `jackall-cli-<version>.zip` the command-line half. Each is a
single self-contained executable: no .NET runtime to install, no setup wizard, nothing loose beside
it.

Source, for the curious and the suspicious:
[`tools/JackAll`](https://github.com/jbebe/farcry-sdk/tree/main/tools/JackAll).

## What it can do

### Mods — the stack, and the build

![JackAll's Mods tab](/img/screenshots/jackall-mods-tab.png)

The Mods tab is an ordered list of mod folders and zips, applied top to bottom with the lower entry
winning, each one switchable with a checkbox. A mod is just a tree of relative paths
(`worlds\world1\generated\…`) — exactly what unpacking an archive already gives you — so existing
community mods drop straight in, and a legacy whole-archive mod (a replacement `patch.dat`/`patch.fat`,
which is how most Far Cry 2 mods are distributed) is converted into an ordinary layer by **Import
legacy mod**, which diffs it against true vanilla and keeps only what it actually changed. **Deploy
mods** compiles the stack into the game's archive pair; **Revert to original** puts the pristine one
back.

Builds are safe by construction: `patch.dat` is backed up once to `patch.dat.vanilla` and *every*
build regenerates from that backup rather than from whatever is on disk, so building twice produces
identical bytes, disabling a mod genuinely removes it, and a failed build leaves the game untouched.
Two mods editing different parts of the same file are merged three-way instead of one silently
clobbering the other, and **Check for dead edits** reports edits that a later entity library
overrides — the change that looks applied but does nothing in game. A mod may also carry a
`plugins\` folder, which is mirrored into `bin\plugins\` for [FCSE](/fcse) rather than into the
archive.

### Files — every archive as one filesystem

![JackAll's Files tab](/img/screenshots/jackall-files-tab.png)

The Files tab is the merged view of all thirteen archives, resolved the way the engine resolves them:
one tree, over 150,000 entries, with anything a mod supplies highlighted so "what have I actually
changed" is answerable at a glance. Select a file to preview it, export it, replace it or revert it;
a modded file can be diffed against its vanilla version, and **Export original** always gets the
untouched one back. The filter box matches any word and narrows with `ext:`, `arch:` and `hash:`.

More than 50,000 of the game's entries have no recovered filename, and they are not second-class: each
gets a type sniffed from its header, appears under `_unknown\`, and is edited as `_hash\<crc32>.<ext>`,
which the builder writes at that literal hash. Every selected file also gets a cross-reference panel —
what points at this file, and what this file points at, both double-clickable to jump — which is
often the only thing that can be said about an entry whose type nothing can decode.

### The value editor — `.fcb` without the hex

![The FCB value editor](/img/screenshots/jackall-fcb-editor.png)

Nearly all of the gameplay data modders actually want — weapons, AI, economy, vehicles, patrols —
lives in `.fcb` object trees, and JackAll decodes them into a typed component/field tree rather than
XML you have to hand-edit and re-encode. Big containers such as an entity library are split one
fragment per entity, so opening a weapon means opening the weapon, not a multi-megabyte document.
Fields are edited natively, with their real names and types, and nothing is written until you save.

Saving stages the changed fragment into your workspace as a small file at an entity-shaped path,
which is what makes conflict resolution between mods tractable — see [How to use](#how-to-use)
below. The same editor opens a savegame's `PersistenceDB` tree, because a save *is* an FCB object
tree.

### Textures, audio, models — previewed and converted in place

![Texture, audio and mesh previews](/img/screenshots/jackall-asset-previews.png)

Most asset types get a real view rather than a hex dump. `.xbt` textures preview and split into a
`.dds` payload plus their header XML, with the pair rebuildable back into a valid `.xbt`; `.xbm`
materials show their shader template, every texture slot and every parameter; `.xbg` meshes get a 3D
preview with LOD and damage-state selection, and RealTree species draw as their own geometry. `.sbao`
audio plays inline, exports as Ogg or mp3, and imports anything ffmpeg can read — transcoded to the
48 kHz stereo Ogg Vorbis the engine requires, with ffmpeg bundled so it works out of the box.

`.spk` sound banks are grouped into one row per sound with its parameters nested underneath, and each
record's audio plays, exports and re-imports — both the Ogg Vorbis and the IMA-ADPCM variants, told
apart automatically. Every one of these edits stages into the workspace; nothing reaches the game
until you deploy.

### Model export — `.fc2model` and Blender

![Exporting a model as .fc2model](/img/screenshots/jackall-fc2model-export.png)

**Export as .fc2model** on any `.xbg` collects the model, its materials, its textures, the rig beside
it and — optionally — every animation bank that names it into a single decoded file with no Dunia
format inside it at all: JSON and flat float arrays for the mesh, JSON for the materials, PNG for the
textures. **Apply .fc2model** takes what an editor changed and stages the affected game files back
into the workspace, listing every file it will touch first.

That split is the whole design: JackAll owns every byte layout, and
[`tools/BlenderFC2`](https://github.com/jbebe/farcry-sdk/tree/main/tools/BlenderFC2) — the Blender
add-on that reads the pack — owns what a scene looks like, holding no format code of its own. The
result is that the art half of a custom weapon is one file and one plugin instead of twenty one-off
scripts; see [Adding a weapon](/docs/modding/adding-a-weapon) and
[`.fc2model`](/docs/file-formats/fc2model).

### Map — the world in 3D

![The Map tab](/img/screenshots/jackall-map-tab.png)

The Map tab loads a world's sectors into one heightfield and flies a camera over it, with the layers
a Far Cry 2 map is actually built from listed beside the viewport and individually toggleable. The
terrain draws its real blended textures and baked lighting, water surfaces, roads, rivers and foot
paths as splines, authored polylines, and the per-sector vegetation scatter with each species as its
own geometry. Placed entities draw as their real models or as markers, filtered by mission layer,
searchable, and selectable from either the list or the viewport.

Everything mesh-less that no other layer would show gets its own glyph — lights in their own colour,
trigger volumes as wireframes, AI cover and guard points, building entrances, particle and sound
emitters, and the navmesh nodes the AI walks on. It is a viewer today: selection, inspection, and a
drag gizmo that lives for the session. Nothing it shows is written back yet.

### Library — the archetype namespace

![The Library tab](/img/screenshots/jackall-library-tab.png)

Every placed entity in the game is a delta over an archetype, and those archetypes are declared
across several libraries that override each other in a fixed order. The Library tab resolves that
namespace the way the engine does, so the definition the game actually reads is the one you edit —
and a definition some later library overrides is visibly dead rather than looking editable. The
override chain for the selected archetype is listed beside it, each layer inspectable on its own, and
editing hands off to the ordinary value editor against the fragment the winning declaration lives in.

### Saves — what a savegame has already decided

![The Saves tab](/img/screenshots/jackall-saves-tab.png)

The Saves tab finds the player's `.sav` files and shows each one with its in-game thumbnail, world,
player name, save time, DLC set and persisted-object count. A save's `PersistenceDB` tree opens in
the same value editor as everything else and writes straight back to the `.sav` file, so a stuck
value is fixable without starting a new campaign.

It also exists to explain a trap that has confused Far Cry 2 modders for fifteen years: a save
persists values overlapping almost every entity library, so an `.fcb` mod cannot override what a
savegame already stores. That is why installing a data mod usually means starting a new game — and
being able to see exactly what your save has pinned is the difference between knowing that and
guessing.

### Magma UI and Domino — menus and mission logic

![The .mgb editor and the Domino graph viewer](/img/screenshots/jackall-mgb-domino.png)

`.mgb` packages are the game's own UI format, and JackAll decodes one into a full tree with a
generated property grid: every widget, action, state and colour is reachable, the "Add" picker offers
exactly the classes the engine's own factories accept in the selected position, and saving
reserialises the whole package — so adding an element or declaring a new class is an ordinary edit
rather than the impossible case it used to be. This is what makes a custom in-game options page
buildable at all; see [Magma UI](/docs/category/magma-ui).

Domino is the mission-scripting system, shipped as generated Lua. JackAll reconstructs a
`domino\user\` script back into the box-and-wire graph it was authored as, laid out automatically,
with pin signatures pulled from the node type scripts and the original editor names recovered from
the `*.debug.lua` twin beside it. Read-only for now: it is how you find out what a mission does, not
yet how you change it.

## Format support

Everything below is JackAll's own code — one implementation per format, with a corpus gate beside it
holding the codec to the bytes Ubisoft shipped. The reference notes for each live under
[File Formats](/docs/category/file-formats).

| Format | What it is | What JackAll does |
|---|---|---|
| `.fat` / `.dat` | The engine's archive pair, LZO-compressed and hash-indexed | Reads every entry; rebuilds `patch.dat`/`patch.fat`. Every shipped `.fat` re-serialises byte-for-byte |
| `.fcb` | Binary object trees — entities, weapons, AI, world sectors | Decodes and re-encodes; splits into per-entity fragments; typed tree editor; three-way merge across mods |
| `.rml` | Binary XML — manifests, and the localised string table | Decodes and re-encodes; text editor with a diff against vanilla |
| `.mgb` (+ `.mgb.desc`) | Magma UI packages — the game's menus and HUD | Full decode/encode and a structural editor; `verify` checks a package references only names it declares |
| `.xbt` (+ `.dds`) | Textures, block-compressed, with a streaming header | Splits and rebuilds; decodes BCn to pixels for previews and model packs; re-encodes to the original compression |
| `.xbm` | Materials — shader template, texture slots, parameters | Decoded and displayed in full; written back through a model pack |
| `.xbg` (+ `.xbgmip`) | Meshes, with LODs, damage states and skinning | Decoded and re-encoded; 3D preview; Wavefront `.obj` export; edited through a model pack |
| `.skeleton` | Rigs | Read and written, byte-identical over the retail set |
| `.mab` | Animation banks | Read; one clip rewritten in place with the rest of the bank carried verbatim |
| `.rtx` | RealTree — procedural tree species | Decoded to branch skeleton and leaves; drawn in the map and mesh viewers |
| `.fc2model` | JackAll's own decoded model pack (JSON + PNG, no Dunia bytes) | Written and read; the only thing the Blender add-on ever sees |
| `.sbao` | Streamed audio — music, dialogue | Splits into header + Ogg Vorbis; plays; exports Ogg/mp3; imports anything ffmpeg reads |
| `.spk` | Sound banks | Lists, decodes and groups records; extracts and imports Ogg Vorbis and IMA-ADPCM |
| `.sdat` | Per-sector terrain — heights, surface types, water, baked light | Read: the map viewport's terrain, and a grayscale heightmap preview per file |
| `.nvm` | AI navigation mesh | Read: walkable nodes, with the normal their slope is tested against |
| `.sav` | Savegames | Header, thumbnail and DLC set read; `PersistenceDB` tree edited and written back |
| `depload.dat` | Per-asset dependency lists | Parsed; drives the cross-reference graph, and gets its own viewer |
| Domino `.lua` (+ `.debug.lua`) | Generated mission-graph scripts | Parsed back into a node graph, with pin signatures and the original editor names |
| `.png` | — | Own encoder and decoder, for model-pack textures |
| `.xml`, `.lua`, `.desc` | Plain-text assets | Syntax-highlighted view, with a diff against vanilla when modded |

Identified but deliberately not decoded — sniffed for a type so they browse, export and mod as
opaque bytes: `.hkx` (Havok collision), `.bik` (video), `.feu` (legacy UI), `.wem`, shader binaries,
`.srl` / `.zsr` (spatial streaming), `.luab` / `.luac` (compiled Lua), `.loc`, `.material.bin`,
`.sctr`, `.tree`, `.cbatch`, `.terrainnode.bdl` and `.dpax`.

## Where it's going

JackAll started as a mod installer, and that part has since moved out of the way: the install
pipeline was branched out into a **[Vortex extension](https://www.nexusmods.com/site/mods/2143)**, so
Far Cry 2 mods now install from Nexus the way they do for any other game. The extension is a
front-end, not a second implementation — Vortex handles downloading, staging, enabling and load
order, and every step that needs the game's own archives calls back into JackAll's command line, so
the mod semantics live in exactly one place and cannot drift. If all you want is to *install* mods,
use Vortex; JackAll is what you want when you intend to *make* one. See the
[Vortex page](/docs/modding/vortex) for the pipeline.

What is left has grown well past a file manager. There is a real 3D map editor taking shape over the
worlds themselves, a real model exporter with its own interchange format and a Blender add-on on the
other end of it, a visual editor for Domino mission graphs, an archetype browser that resolves the
game's override chains, and a savegame editor — several of which are the only tool of their kind for
this game, in any form. Some are still viewers; the direction of travel is that each one grows its
write path, in the order the [roadmap](/todos/roadmap) argues for.

None of this is structurally specific to Far Cry 2. Dunia is Ubisoft's engine across a decade of
games and the formats mostly rhyme: an `.fcb` is an `.fcb`, an archive is an archive, a mesh is
recognisably the same mesh. Nothing in the architecture prevents Far Cry 3, 4, 5, 6 — or whatever
number Ubisoft is on by the time anyone reads this — from being taught to it as another game
profile. That is a community-sized job rather than a one-person one, which is exactly why the source
is public.

## How to use

**First run.** Point JackAll at the folder holding `bin\FarCry2.exe`. It checks the archives against
the hashes of a clean, patched 1.03 install and says so if something already differs — it will still
work, but its idea of "vanilla" is only as good as the files it was given. Everything the tool writes
lands in three places beside the executable: `config.ini` (your game folder and mod list,
hand-editable), `data\` (shipped dictionaries and caches, never hand-edited), and `workspace\`.

**The workspace is where your edits live.** Every change you make inside JackAll — a replaced
texture, an edited entity, an applied model pack — is written into `workspace\` as a plain file at
its game-relative path. It is pinned last in the mod list, so your own work always wins over the mods
below it, and it can be switched off with a checkbox like any other mod when you want to see the game
without it. There is no separate "package" step: **the workspace *is* the mod**. Zip its contents,
and what comes out is a mod anyone else can drop into JackAll or install through Vortex.

**Edits are staged by entity, not by file.** This is the one place JackAll deliberately departs from
the Gibbed-era workflow. Gibbed's tools convert a whole `.fcb` to XML, you edit that, and you ship
the whole re-encoded container — which puts two mods that both touch an entity library in conflict by
construction, even when one changed a rifle and the other changed a jeep. JackAll splits a container
into one fragment per entity and stages the fragment alone, at a path like
`entitylibrary.fcb\vehicle\land\jeep.xml`. Two mods touching different entities therefore never meet;
two mods touching different fields of the *same* entity are merged three-way against the vanilla
version; and only a genuine collision — the same field, changed to two different values — needs a
human, at which point JackAll shows it as a conflict instead of quietly picking a side.

**Two editors are read-only, on purpose.** The Map tab renders a world, lets you select and inspect
entities, and has a translate gizmo that survives exactly as long as the session — nothing it shows
is written back yet. The Domino editor reconstructs a mission graph so you can read it; there is no
path from the graph back to the Lua. Both are load-bearing for understanding the game today, and both
are stated as viewers rather than half-editors so nobody loses work assuming otherwise.

**One warning worth repeating.** A savegame persists values that overlap almost every entity library,
and an `.fcb` mod cannot override what a save already stores. If a data mod appears to do nothing,
start a new game before assuming the mod is broken.

## CLI

`jackall-cli.exe` is the headless half. It ships as its own download, needs no game install for the
pure format conversions, and every `mod` command takes `--json`: exactly one object on stdout,
progress on stderr, so a caller never has to scrape human-readable text.

### Mods

The whole surface a mod manager needs, running the same code as the app's Mods tab.

| Command | What it does |
|---|---|
| `mod status --game <dir>` | Reports whether a folder is a Far Cry 2 install, and what state its patch archive is in |
| `mod inspect <folder or zip> --game <dir>` | Says whether an archive is a mod layer or a legacy full-patch mod, and where its tree starts |
| `mod import-legacy --game <dir> --from <zip> --out <dir>` | Converts a legacy replacement `patch.dat`/`patch.fat` mod into an ordinary layer folder |
| `mod lint --game <dir> --layer <dir>` | Reports archetype edits that a later entity library overrides, so they change nothing in game |
| `mod build --game <dir> --layer <a> --layer <b>…` | Compiles the vanilla patch plus the given layers into the game's archive pair. Order matters — later layers win |
| `mod restore --game <dir>` | Puts the pristine `patch.dat`/`patch.fat` back, undoing every build |

```
jackall-cli mod build --game "C:\Games\Far Cry 2" --layer mods\bettersights --layer workspace
```

### Formats

| Command | What it does |
|---|---|
| `archive extract <file.fat> [--names] [--filter <s>]` | Extracts and decompresses an archive's entries; `--names` resolves hashes to real paths |
| `fcb decode` / `fcb encode` | An `.fcb` object tree to XML, and back |
| `rml decode` / `rml encode` | A binary `.rml` to plain XML, and back |
| `mgb decode` / `mgb encode` / `mgb verify` | A Magma UI package to editable XML and back; `verify` checks it references only names it declares |
| `xbt extract` / `xbt build` | Splits an `.xbt` into `.dds` + header XML, and reassembles it |
| `xbg export <mesh.xbg>` | Converts a mesh's geometry to a Wavefront `.obj` |
| `fc2model export` / `extract` / `inspect` | Builds a model pack, writes a changed pack back out as game files laid out as a mod layer, or lists what a pack holds |
| `sbao extract` / `sbao build` | Splits an `.sbao` into `.ogg` + header, and reassembles it |
| `spk list` / `spk extract` / `spk import` | Lists a sound bank's records, extracts one as `.ogg`/`.wav`, or replaces one |
| `xref build` / `xref to` / `xref from` | Indexes every hash reference in the game's archives, then answers what references a file and what a file references |

```
jackall-cli fc2model export graphics/weapons/primary/ak47/ak47.xbg -g "C:\Games\Far Cry 2" --clips
jackall-cli xref to "graphics\_common\weapons\ak47\ak47_d.xbt" --game "C:\Games\Far Cry 2"
```

### Why it exists separately

The CLI is aimed at anyone wiring Far Cry 2 into their own toolset: a build script that regenerates a
mod from source assets, a CI job checking that a pack still round-trips, a batch conversion over a few
thousand textures, or another mod manager entirely. The JSON contract is the stable interface — stdout
is the document, stderr is progress, and a failure is `{"ok":false,"error":"…"}` plus exit 1, never a
bare message on the wrong stream.

The Vortex extension is the proof that this works: it drives `jackall-mi.exe`, a trimmed build
carrying only the four commands an installer actually needs, with a JSON contract identical to
`jackall-cli`'s `mod` branch — so the extension can point at either, and neither one is a
reimplementation of the other.

## A word on intellectual property

JackAll's `.fcb`/`.rml`/`.fat` decoding stands on the shoulders of a decade of community
reverse-engineering — Gibbed's original Dunia tools, wobatt's hash-decoding improvements, and every
forum post and Discord thread that ever pasted a working snippet of format knowledge into the void.
So: to everyone who ever uploaded code, a hex dump, or a "here's how the header works" post that
this tool's understanding of the format ultimately traces back to — thank you for your service, and
also, deepest apologies, your IP has been thoroughly appropriated. Rest assured it was done with
love, a decompiler, and the sincerest form of flattery a modding community can offer.
