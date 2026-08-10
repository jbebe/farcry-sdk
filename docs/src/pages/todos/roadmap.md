---
title: Tooling roadmap
description: Assessment of what JackAll already covers, what's missing, and which direction needs the most implementation.
---

# Tooling roadmap

An assessment of where the remaining leverage is across the tooling: what's already covered by
parsers and editors, what matters, what doesn't, and which direction needs the most implementation.
Companion to the [Todos](/todos) list — this is the reasoning behind several of its entries.

The short version: **JackAll has finished the codec job and hit a composition wall.** Everything
carrying tunable data round-trips byte-exact. The level-design category has no tooling anywhere, in
any tool. The fix is to unlock and feed Ubisoft's own map editor — the level team built the campaign
with an advanced build of the same tool — with JackAll as the bridge/compiler and FCSE as the
in-process patcher if patching proves necessary.

## Coverage assessment

| Tier | Formats | State |
|---|---|---|
| Solved end-to-end | `.fat`/`.dat`, `.fcb`, `.rml`, `.mgb`, `.xbt`, `.sbao`, `.spk`, `.sav`, `depload` | Round-trip + editor + mod pipeline. Nothing more needed. |
| Reads, no write path | Domino graphs, `.sdat`, `.xbg`, `.xbm` | Writers exist for Domino and `.sdat` but are called only from tests. |
| Container only | `.srl` (14,964 files), `.zsr` (14,964), `.nvm` (5,144) | Spatial data — roadmap dependencies, not standalone work. |
| Sniffed, never parsed | `.hkx`, `.mab`, `.skeleton`, `.rtx`, shader bins, `.bik`, `.feu`, `.wem` | See [Not doing](#not-doing-and-why). |
| Parseable but unusable | `worldsector<N>.data.fcb` (5,230), `world1.mapsdata.fcb` | These *are* FCB — decoded today into a wall of `hidPos`/`hidAngles` floats with no spatial meaning. This is the wall. |

All the gameplay data modders actually want — weapons, AI, economy, vehicles, patrols, missions — is
already decoded. The remaining value is not more parsers.

## Four findings that change the roadmap

**1. The 8×8 sector cap is not an engine limit, and is not blocking.** The grid is data-driven:
`<world>.game.xml` carries a `Grids` → `GridWorldMaps` block with `SectorCountX`/`SectorCountY`,
`OffsetX`/`OffsetY`, `SectorOffsetX`/`SectorOffsetY`, parsed at `0x102A3DC0` → `0x102A1FE0` /
`0x102A16E0`, written by `CFCXEditorDocument::ExportWorld` @ `0x107E28B0`. Shipped MP maps are
10×10; campaign worlds are 80×80 (25 levels × 16×16, row stride 80). **80 / 8 = a 10×10 grid of
editor-sized windows per campaign world** — campaign editing ships with zero binary patching.
`BattlefieldSize` moves the playable zone only; every MP map is 100 sectors regardless, so
`Size.ExtraLarge` is a dead hypothesis.

**2. The four `ige\` files are not a research problem.** Their reader and writer are ~3.1 KB of
already-located code: `CFCXEditorDocument::DoSave` @ `0x107E4990` (strings `%sheightmap.raw`,
`%smap.xml`, `%stexture.mask`, root element `FarCry2.Editor.Map`, sections `Properties` /
`TerrainManager` / `ObjectManager` / `SplineManager` / `CollectionManager`), `DoLoad` @ `0x107E5370`
(still `FUN_107E5370` — name it first), and the `collection.mask` writer/reader pair at `0x107E7250`
/ `0x107E7290`.

**3. Wilderness is a dead end as an import channel, but a good de-risking harness.** Map handles only
ever come from `Generate*`; `GenerateFromMap` takes a handle, not a file; the managed callback is
read-only; and it touches no objects or splines. It *is* the cheapest possible proof that a bulk
non-brush mutation survives Save + Export. Separately, the 7 biome scripts ship as real `.lua` under
`ingameeditor\wilderness\` — that closes a documented Unknown in
[Wilderness Script](/docs/engine-internals/wilderness-script-language).

**4. Stale docs that would misdirect roadmap spend**, to fix as encountered:

- [.sdat](/docs/file-formats/sdat) and [.srl/.zsr](/docs/file-formats/srl-zsr) claim 8×8 everywhere —
  true only for editor output.
- [File manifest](/docs/modding/file-manifest) says `world1.mapdata.fcb`; the shipped name is
  `world1.mapsdata.fcb`.
- [.xbm/.xbg](/docs/file-formats/xbm-xbg) calls the Blender importer broken, while the vendored
  copy's README now claims skeleton export, HKX collision export and full material export.
- JackAll's `CHANGELOG.md` names `JackAll.Core.Tests` as the live suite — that's an empty leftover
  directory; the real one is `JackAll.Tests`.

## Track 1 — Campaign levels in Ubisoft's editor

**Top priority.** The editor pipeline is strictly one-way: `ige\` (4 files) → Save/Export →
`.sdat` / `.srl` / `.zsr` / `.fcb` / `.xbt`. Campaign data lives entirely on the right side, and of
the 338 [`FCE_*` exports](/docs/engine-internals/editor-api-surface) there is exactly one ingest
door, `FCE_Document_Load`. So the work is a reverse compiler: campaign game-side files → `ige\`
document.

Each rung ships something demonstrable even if the next never happens.

### M0 — Archive truth pass

*~0.5 day. No game run, no RE, no risk.* Everything here is free via JackAll's read-only archive
mount (`JackAll.Core/Vfs/GameVfs.cs`):

- Decode and diff the `Grids`/`GridWorldMaps` block across `ige_map`, `tmpla`, `world1` and
  `mp_11_l_savanna`. Expect 8/8, 8/8, 80/80, 10/10.
- Round-trip `levels\ige_map\generated\sdat\sd0.sdat` through `JackAll.Tools/Sdat/SdatSector.cs` —
  **the first real `.sdat` sample the project has ever had.** Confirm or correct the provisional
  `MetersPerUnit = 1f/128f`.
- Diff that sector's 572-byte `SSectorDataChunk` header against campaign
  `levels\w1_a_1\...\sd5120.sdat`. A structural difference kills the track on day one (K3).
- Set-overlap `ingameeditor\object_inventory.xml` against `world1` `entitylibrary.fcb` — answers the
  object-inventory gate (K5) with pure offline arithmetic.
- Recover the 7 Wilderness `.lua` biome scripts.

### M1 — ige corpus and differential decode

*1–2 days. The editor runs once, ~40 minutes of clicking.* Use `Data_Win32\FC2Editor.exe` — present
in the retail install, with `SandBar.dll` and `SandDock.dll` shipping alongside it. **Double-click it
before anything else**: it is the only hard external dependency in the whole track (K6). Fallback is
the in-game editor via `Multi_LaunchMapEditor` in a process FCSE already hosts.

Save a defaults-only baseline, then one save per single change: two height dabs one grid unit apart;
texture slot 0 then slot 1 at the same spot; collection slot 0, slot 1, then clear; water +1.0 m
exactly; one object at a known position and rotation; a 2-point road. Unpack each through Gibbed's
`Gibbed.FarCry2.FileFormats/MapFile.cs` (`CCustomMapGameFile` v11).

Layout hypotheses — for three of the four, a directory listing discriminates:

| File | Hypothesis | Confirmation |
|---|---|---|
| `heightmap.raw` | 513×513 u16 LE, headerless = **526,338 bytes** | File size; then the two height dabs give element width, stride and origin directly |
| `texture.mask` | 1 byte/cell dominant slot index, vs. 4 bytes/cell weights | Sizes collide across resolutions — use the slot-0-vs-slot-1 pair (same bytes change ⇒ index, different bytes ⇒ planes). Cross-check `sd0_mask.xbt`, the compiled output of this data |
| `collection.mask` | 1 byte/cell with `8` or `0xFF` empty — 9 states (8 slots + `EmptyCollectionId`) don't fit 3 bits | The three collection samples |
| `map.xml` | Plain text XML | `DoSave`'s strings *are* the element names and its callees are `sprintf`-dominated, with no RML or FCB writer. First 64 bytes settle it |

Deliverable: `jackall ige decode`. This is the last undocumented format in the editor pipeline.

### M2 — Round-trip write

*1–2 days.* Byte-identical re-encode, then a hand-authored mutation the stock editor opens.
Demonstrable: **import an external heightmap into an FC2 map via Ubisoft's own editor** —
community-shippable on its own. This is also the detector for K4, which is why it must precede M3.

### M3 — Campaign → ige window

*2–4 days.* `jackall campaign extract-window --world world1 --level w1_a_1 --origin 0,0 --size 8x8`,
producing an `ige\` set plus a `.fc2map`. Terrain, textures, collections and water only — **objects
deliberately excluded from `map.xml` on the first pass**, so this rung cannot be blocked by the
inventory gate. Demonstrable: fly around a real slice of the Far Cry 2 campaign inside the shipped
map editor.

### M4 — Export merge-back

*3–5 days.* `jackall campaign apply-window`: take the editor's exported
`levels\ige_map\generated\**`, remap sector indices into the campaign level, and merge edited
terrain/texture/collection while preserving the original entity FCBs byte-exact. Demonstrable: edit
campaign terrain in the editor, play the change in the campaign. **This is the actual goal.**

### M5 — Objects both ways

*1–2 weeks.* Extend `ingameeditor\object_inventory.xml` — a data file, patchable through JackAll's
existing mod layer — to cover campaign archetypes, then carry entities in `map.xml` and merge on
export. Watch `FCE_BudgetManager_*` and validation rejecting campaign densities.

### M6 — Lift the 8×8 cap

*Optional, last.* Only if window seams prove intolerable. Candidate layers in likelihood order: the
`.game.xml` `Grids` data (free to test in M0); the document's terrain allocation at
`CFCXEditorDocument::InitInternal` @ `0x107E46A0` and `::Reset` @ `0x107E52F0`; the export loop
bounds in `ExportWorldSectors` @ `0x107E2E20`.

If it needs patching, do it as an **FCSE plugin**, not a static `Dunia.dll` patch — FCSE already has
MinHook, a VirtualProtect-safe `Patch` tier, per-build address resolution via `fcse_relocation.h`,
and a crash logger, whereas a static patch breaks FC2MPPatcher compatibility with no toggle. Start
with the in-game editor, which lives in the same `Dunia.dll` FCSE already hosts; targeting
`FC2Editor.exe` needs a small `FCSEEditor.exe` launcher (`CREATE_SUSPENDED` + inject +
`LdrRegisterDllNotification`).

### Risks

**Biggest waste risk: doing M6 early.** Second: repairing the C# editor shell before M2 — M0–M4 need
zero shell changes, and the whole `FCE_*` surface is reachable from a 200-line P/Invoke harness.

| | Kill criterion | Cheapest detector |
|---|---|---|
| K1 | `map.xml` names objects by editor-inventory entry, not archetype hash ⇒ campaign entities unrepresentable | The one-object sample, first hour of M1 |
| K2 | `DoLoad` hard-validates 8×8 dimensions | Feed a deliberately 9×9 `heightmap.raw` in M2 |
| K3 | Editor-produced vs. campaign `.sdat`/`.fcb` differ structurally | M0 header diff |
| K4 | The editor only loads ige sets it produced itself | M2's hand-written mutation |
| K5 | Object-inventory gate silently drops campaign archetypes | M0 set-overlap |
| K6 | `FC2Editor.exe` won't launch (DRM / .NET 3.5 / D3D9 on Win11) | Double-click it. Fallback: the in-game editor under FCSE |
| K7 | Redistributing extracted campaign bytes | Architectural: build the ige set locally from the user's own install, ship code never map data — the existing Vortex/mod-installer pattern |

The Ghidra MCP bridge was down while this was written; most addresses above came instead from
`tools/FCSE/tools/addrlib/cache/fc2_103_uplay.functions.jsonl` (63,571 functions with per-function
string and callee tables), which is faster for this kind of lookup.

## Track 2 — Semantic FCB content index

Today a modder has to already know that weapon stats live in
`libraries/world1/41_WeaponProperties.xml`, inside `entitylibrary.fcb`, inside `worlds.fat`.
[Data recipes](/docs/modding/data-recipes) is essentially a hand-written substitute for a query
engine.

The existing xref index is the wrong index but the right template:
`JackAll.Core/Xrefs/FcbReferenceExtractor.cs` indexes only `String`, `Hash`, `HashArray` and `Rml`
*values* — no numerics — and `ReferenceIndexer.cs` skips fragments entirely, so
`41_WeaponProperties.xml` isn't even a unit it knows about.

Build a second extractor and index keyed on `(classHash, memberHash, typedValue) → fragment`, with
numerics kept as sortable scalars and fragment-granular source ids. Reuse `ReferenceIndexer`'s
two-pass base/overlay split and `ReferenceIndex`'s single-`byte[]` `MemoryMarshal` storage pattern
verbatim. The vocabulary already exists: `binary_classes.xml`, 2,116 typed classes. Surface it as an
`fcb:` filter token in the Files tab plus a `jackall fcb query` CLI command.

Purely additive, no format RE risk. Medium effort, best value-to-effort ratio of the non-world work.

## Track 3 — The already-90%-done cluster

The cheapest wins in the tool.

- **Wire `UserGraphWriter` into the app.** It exists, round-trips, is corpus-tested, and is called
  only from its own test file; `DominoTabViewModel` discards the writable `UserGraph` as a
  constructor local. Retain it, plumb `replaceContent` through `BuildDominoHandler` /
  `OpenDominoEditorTab` in `JackAll.App/FileHandlers/FileHandlerCatalog.cs`, and reuse the
  dirty-tracking header the FCB/XML editors already have. Scope to **save and parameter editing, not
  topology** — control edges are handler synthesis, data edges are a resolver's inference, and
  `GraphBuilder`'s projection has no inverse. De-risk first with an identity round-trip → pack →
  play, since nobody has yet shown an edited mission *topology* running (the 2011/2016 community
  contradiction in [Gotchas](/docs/modding/gotchas) is still unresolved). Small.
- **UVs and materials in the `.xbg` OBJ export.** The `Uv0` flag is already in `XbgModel`'s stride
  table, just untracked, and the reference implementation is local in the vendored Blender importer.
  Then `MaterialName` → `.xbm` → `.xbt` → `.mtl` reuses chains JackAll already resolves. Small.
- **Batch operations.** Multi-select in the file grid drives only a count/size readout — no batch
  extract, replace or folder export — and the CLI has no glob/recursive mode. Small.

## Track 4 — `.xbm` writer, then textured preview

`JackAll.Tools/Xbm/XbmMaterial.cs` is parse-only and byte-scans for `LTMD` rather than walking the
chunk list. Material tweaks — glow values, tiling, swapping a texture slot — are a real modding use
case, the format is small and documented, and a round-trip-validated reference exists. Medium
effort, high value.

That in turn unlocks the best demo feature in the tool: a **textured 3D preview that honours the mod
stack**. Every viewer today renders the shipped asset; nothing renders the modded result in context.
Large, but it folds in Track 3's UV work.

## Track 5 — Localization / string-table editor

`OasisStringTable` parses ~11,500 entries and `.rml` round-trips, but there is no editor — you
hand-edit raw XML. All the hard domain complexity (string keys rather than numbers, non-unique across
sections, three spellings, patch `.rml` superseding a stale `.xml`) is already solved and documented
in that class's remarks. A two-column searchable key/value grid with section scoping makes
retranslations, weapon renames and dialogue edits a one-click job. Medium.

## Track 6 — App test seam

**Gate for Tracks 1 and 4.** `JackAll.App` is 11,823 lines of C# plus 2,205 of XAML with **zero
tests** — no test project references it — while the byte-level code beneath it is rigorously covered.
`XbgModel` has no test file at all despite a catch-and-return-null fallback in `TryParseDnks`, and
it's about to become load-bearing.

26 of the 63 App files have no `System.Windows` dependency and are testable today with nothing but a
csproj reference: `SugiyamaLayout`, `FcbEditorTabViewModel`, `PropertyRow`, `ScalarField`,
`FcbFieldFormat`, `SaveGameXmlRenderer`, `OasisStringTable`, `DiffTextBuilder` and others. The one
snag is that `MainViewModel` pulls in `System.Windows.Media.Imaging` for exactly one line
(`Int32Rect`, save thumbnails) — extract that and the app's central view model becomes testable.

Stand up `JackAll.App.Tests` before either large App feature lands, and delete the empty
`src/JackAll.Core.Tests/` leftover.

## Not doing, and why

- **A native `.xbg` writer.** Large, and it duplicates an actively-developed Blender add-on that per
  its own vendored README now covers skeleton export, HKX collision export and full material export.
  Integrate — a send-to/receive-from-Blender staging path — rather than reimplement. Re-verify those
  claims first, since our docs contradict them.
- **Swapping the Domino graph package.** The Nodify 7.3.0 complaints are real but cosmetic; the
  actual problem is 20,228 wire crossings *after* four Sugiyama sweeps on `a1bu00_storymission`. A
  new package buys styling and virtualization, not readability. If revisited, the order is: UI
  virtualization → Brandes-Köpf x-coordinate assignment and edge routing → semantic collapsing →
  persist node positions.
- **Shaders** (permutation IDs, a documented hard ceiling), **`.bik`** (third-party), **`.hkx`** (deep
  niche), **`.mab`/`.skeleton`** (blocked behind the `.xbg` skinning problem anyway).
- **`.srl`, `.zsr` and `.nvm` as standalone tracks.** They are Track 1 dependencies. Decoded in
  isolation they're hex dumps; decoded alongside a map they're editable overlays. `.nvm` in
  particular only matters once you can place geometry — and regenerating a Recast graph wrong breaks
  AI pathing silently.
