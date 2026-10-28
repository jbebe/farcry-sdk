# Changelog

Notable changes to JackAll, loosely following [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [1.7.0] - 2026-10-28

### Added
- **Make a font**, on the Mods tab, draws a font the game can use from any TrueType or OpenType
  file: pick one of the fonts the file holds, set a variable font's weight or other axes, the size
  and the name, and see sample text set as the game sets it before staging it. It is staged in every
  `fonts.mgb` and every UI dependency list, so a UI text can name it as its font family.
- **`.mft` fonts in the Files tab** show what they hold and sample text set from their own pages.
- **`jackall-cli mft decode`, `encode`, `faces` and `build`** do the same on the command line.
- **The UI dependency lists merge as fragments.** A mod ships only the entries of
  `ui\localized\<aspect>\<lang>\ui\_depload.xml` it changes, one per package, and mods adding to the
  same package all land; two mods shipping the list whole used to replace each other.
- **A UI package's fonts and font families are fragments** (`_fonts.xml`, `_fontfamilies.xml`), so
  mods adding fonts to `fonts.mgb` work together.
- **Materials are edited in the Files tab.** An `.xbm`'s preview shows every property its shader
  takes, grouped the way the game's own shader descriptors group them, with colour swatches and
  texture thumbnails, **Pick…** and **Go to** for each texture. An edit goes into your workspace a
  moment after you make it; clearing a value lets the shader's default apply. Anything the game
  would ignore without saying so - a misspelt property, one the shader doesn't take, a texture that
  isn't there - is called out under it. On a weapon or a vehicle, setting a colour also sets its
  Clean twin, which is what the game shows.
- **New material…** makes a material at a new path, as a copy of the selected one or empty on a
  shader of your choosing.
- **`jackall-cli xbm show`, `new`, `set` and `lint`** do the same on the command line.
- **`jackall-cli fc2model add-material`** adds a material to a model pack, and **`assign-material`**
  draws a part with it at every level of detail; `fc2model inspect --materials` lists a model's
  materials and the parts drawing with each. A model pack now also carries every shader's property
  list, which the Blender add-on's material editor reads.
- **`jackall-cli ai pack`** compiles a brain's BlackBox.AI source, as `ai unpack` writes it, into an
  `.ai.rml` workspace, so a brain can be edited as text and built into a mod layer.
- **AI brains merge as fragments.** A mod can ship only the brain's plans and tasks it changes,
  staged in a folder named after the brain, such as `mods\scripts\game\newbrains\mercbrain.ai.rml\`,
  instead of the whole brain. The build merges them into the game's own brain and recompiles it, so
  mods that change the same brain work together, and two that change one plan merge. **`jackall-cli
  ai fragments`** writes them from an edited brain or its source, and saving a brain on the **AI**
  tab stages only the plans and tasks you changed.
- **`jackall-cli move clone`** gives a new weapon index a copy of everything another weapon's
  animations do, playing the same clips until you point it at others.
- **A navmesh for any terrain and anything standing on it.** `navmesh build` and `world pack` now
  build the AI's navmesh with Recast from the terrain and every placed object's collision. That
  covers hills, water, floors, roofs, ramps, stairs, bridges and trees. It leaves out slopes over
  45° and steps over 0.3 m, and lays cover points along walls and props. `navmesh build` takes
  `--game` to read the collision of retail objects from the install.

### Fixed
- **Following a reference reaches its file whatever the search box holds.** A jump to a file the
  current search left out used to select nothing; the search is now cleared so the file shows.
- **Tilted objects stand the way the game stands them.** An entity turned about both X and Y was
  rotated in the wrong order on the Map tab, by the gizmo and in the navmesh. The engine applies X,
  then Y, then Z.
- **Collision files with a degenerate hull face open.** The HKX editor and everything reading
  collision failed on six retail files whose hulls list a face of fewer than three corners.
- **Cover records name their fields rightly.** Of a navmesh cover's two numbers, the first is the
  graph id of the node it is taken from and the second its vertex, not the other way round.

## [1.6.1] - 2026-10-17

### Fixed
- **Export as .fc2model with animations now includes the character** — a pack exported from the app
  with its animation banks left the body out, so the Blender add-on showed the weapon floating alone
  and its reloads posed no hands. It now carries the same protagonist `jackall-cli fc2model export`
  always did.

## [1.6.0] - 2026-10-16

### Added
- **Animations can change length in an `.fc2model` pack** — a pack now carries each animation bank's
  tag windows and event times beside their bytes, and applying a pack writes edited ones back. That
  is what lets the Blender add-on's Retime Animation stretch a whole bank, sounds and moving parts
  included. Export the pack with this version; one from an older JackAll has neither.

### Changed
- **Legacy import moves archetype copies out of the patch override** — an old mod's copy of an
  archetype in `entitylibrarypatchoverride.fcb` comes in as the values it changed, in the world
  libraries the game would otherwise read, so it merges with mods that edit them there. A copy of a
  DLC archetype is left behind, since the game never read it, and an archetype no other library
  declares stays in the override. What a copying tool rewrote on its own, such as a float with fewer
  digits, doesn't count as a change.
- **Legacy import keeps a mod's deletions from the patch override** instead of leaving them out, and
  leaves behind files no engine code path loads, such as a world's `entitylibrary_full.fcb`.

## [1.5.0] - 2026-10-15

### Added
- **HKX editor for Havok collision** — **Open in HKX Editor…** on an `.hkx` opens it in its own tab:
  the collision drawn over the model filed beside it, the bodies and shapes as a tree, and the
  selected object's values beside them. Type over a number, flag, vector or named choice; drag a
  shape's arrows or rings to move or turn it, or a box's face knob to resize it; Undo, Redo and Save
  (Ctrl+S) as in the other editors. The Files tab previews an `.hkx` as text, with a diff when a mod
  changes it.
- **Saving collision keeps Havok's saved values in step** — the MOPP index is rebuilt from the
  shapes, and the list's bounds, the body's radius and a wrapper's radius are refreshed, so the game
  sees the new collision.
- **`hkx` commands** — `verify` and `dump`; `decode` writes a file as editable XML and `encode`
  builds it back; `tree` numbers the shapes, and `move`, `turn`, `resize`, `add-box` and `remove`
  edit them by number. `refresh` brings an edited file's saved values in line with its shapes, and
  `check` reports what would break in game: a body after the first `HIGH_` or `NAV_` one, stale
  bounds, a MOPP that lost a shape.
- **Replacing a file with the wrong kind of file warns first** — the Files tab's Replace… compares
  the picked file's header with the one it replaces, and asks before putting, say, a WAV where a
  sound bank belongs, naming the button that imports audio instead.
- **A texture builds from a bare `.dds`** — `xbt build`, the Files tab's Import… on an `.xbt` and an
  `.fc2model` pack no longer need the `.xml` header; without one a fresh header is written, and the
  `.dds` must carry its whole mip chain.
- **Saves show their progress** — the Saves tab and `sav list` show the act, how much of the game is
  complete, and the difficulty.
- **Deploy says what it decided on its own** — the app now lists each file where one mod's whole
  copy replaced another's, and each plugin left alone because `bin\plugins` already held a copy
  JackAll didn't deploy, as `mod build` does.

### Changed
- **Your workspace settles a conflict between mods** — when two enabled mods set one value
  differently, setting it in the workspace decides it and Deploy goes ahead. Browsing shows the later
  mod's value instead of failing, and Deploy's message names both mods and says how to settle it:
  untick one, or set the value in your workspace.
- **Campaign DLC archives rank above the base game** — where a DLC the campaign mounts carries its
  own copy of a file, JackAll reads that copy, as the game does. Only the folders the game mounts
  archives from are read, so a stray `.fat` elsewhere under `Data_Win32` no longer shows up.
- **A mesh's materials are references you can follow** — an `.xbg` links to the `.xbm` files it
  names, so following a material opens it, and `xref to` on an `.xbm` lists the meshes using it. The
  reference index rebuilds once.
- **The AI tab shows what the game reads** — one set of vision cones, since the game reads only the
  DesertFOV ones in every terrain; no light weight, which nothing reads; five adaptive behaviours
  marked as read by no shipped brain, and two vehicle ones described as the game uses them. The
  Curves note says every weapon but the mounted M249 shares the one `DistanceAccuracy` curve.
- **Document tabs close from their × or Ctrl+W** — a middle-click no longer closes one.

### Fixed
- **Sound imported into an IMA-ADPCM bank plays in step** — the encoder writes the 10 raw PCM frames
  and the odd sample the game expects, so the game no longer starts decoding out of step.
- **Unnamed `.sbao` files are recognised as `.sbao`**, from a corrected signature that matches the
  retail files.
- **An edit lands only in the object you edit** — on the AI tab, in a save and in a whole `.fcb` in
  the value editor, identical parts of a file were one shared object, so changing one changed every
  twin.
- **The value editor warns on an archetype copy the game doesn't read** — opening an entity library
  fragment on the Files tab whose archetype a later library declares again shows the notice the
  Archetypes tab does.
- **The Archetypes tab keeps up** — a save, a revert, a mod ticked or a rescan marks which mods edit
  each archetype without another Load.
- **Following references** — Alt+Left and Alt+Right work while a row has focus; a followed reference
  opens its folder in the file list and selects the file there; Export folder… acts on the folder the
  tree shows.
- **Fragments saved as UTF-8 with a byte order mark are accepted** instead of refused.
- **Revert to original stops warning on GOG** — JackAll knows GOG's clean patch, which differs from
  Steam's.
- **A Steam-built legacy mod imports clean on GOG** — entries the mod left as Steam's patch has
  them are no longer staged as its changes.
- **`.fcb` files JackAll writes carry the right header counts**, the ones the game sizes its object
  pool from.
- **`rml fragments` runs** — a second `rml` command group hid it.
- **Messages name the buttons as they are** — Deploy mods on the Mods tab, and the Domino Viewer.
  Apply .fc2model keeps its Staged message, and a legacy import no longer counts the files and
  fragments it skipped against the archive's entries. The Map tab's Deploy button, which never did
  anything, is gone.

## [1.4.1] - 2026-10-09

### Added
- **Magma UI packages split per area** — one fragment per top-level area of a `.mgb`, plus its
  materials, string table and exported objects, merged element by element, so two mods adding to the
  HUD both keep theirs. A build sets the package's pool counts to what it holds. `mgb fragments`
  writes a package's changes as fragments, and the package editor saves them that way.

## [1.4.0] - 2026-10-08

### Added
- **`mod build` reports every change a layer lost** — two layers shipping different copies of one
  whole file, and a fragment replacing an entry another layer changed in its whole-file copy.
- **The control config files split per section** — one fragment per `<Category>` of
  `defaultusercontrols.xml` and per `<ActionMap>` of an `inputactionmap*.xml`, merged child by child,
  so two mods adding a control or binding to the same section both keep it.
- **`legacy analyze`, `changes`, `check` and `pick`** — list every change an old full-patch mod
  makes, group them into features, and build a layer holding only the features you pick.

### Changed
- **Two mods adding to one `.fcb` entity merge** — a fragment merges as a tree, fields paired by
  name and child objects by type, so two mods that each add a component or an FCSE entity-data key
  to one archetype both keep theirs. A list of same-typed records merges record by record, each
  matched to the original by content, so two mods adding event links to one entity both keep theirs.
- An FCSE entity-data key merges as one value, and a fragment listing one key twice is refused.
- **Every fragment merges as a tree, none line by line** — `depload.dat` packages (two mods adding
  dependencies to one package both keep them), MOVE states (op by op; two mods both restructuring a
  state that holds internal references keep the higher-priority one whole), world-descriptor missions
  (layers matched by name) and string-table entries.
- `mod build` names where inside a fragment two mods conflicted, and `--json` conflicts add `paths`.
- **Breaking:** `mod build --json` conflicts name the losing layers `overruledLayers` (was
  `earlierLayers`) and add a `kind` and a `message`.

## [1.3.0] - 2026-10-02

### Added
- **AI tab** — tune soldier archetypes and how they fire each weapon, set the odds of optional
  behaviours, and edit brain task parameters in a behaviour tree.
- **`.ai.rml` brain workspaces** — read and recompiled on save; `ai unpack`, `verify` and `lint`.
- **`.spk` sound banks as XML** — `spk decode`, `encode`, `verify` and `new`; every retail bank
  rebuilds byte for byte.
- **Sound bank editor** — the `.spk` panel edits a bank as a tree of what plays what.

### Changed
- `spk import` and the App's Import… re-derive a sample's loop length and rate, not only its
  length.
- Staging an edit keeps its file selected, so its preview stays open.
- The `.fcb` and Domino sound previews play a random pick from a random container.
- `spk list` names resource kinds.

### Fixed
- An imported looping sample started each loop from silence instead of where its own tail ends.
- Random, switch and multilayer resources read as a link to their child count in the xref index.

## [1.2.0] - 2026-09-26

### Added
- **The Map tab is a scene editor** — hierarchy, inspector and entity library around the viewport;
  select, move, rotate and place entities, edit their fields and components, with undo and redo.
- **Map tab tools** — trigger and light handles, event links, prefabs, a navmesh layer, and a
  **Check** that lists what would fail silently in game.
- **Animations tab** — edit the MOVE graph as rules, replacing the Move graphs tab.
- **`.mab` player** — plays an animation bank's rigs as stick figures.
- **File picker** over the merged archives for any path, hash or sound field.
- **Component properties from the engine** — names, types and enum dropdowns for every class.
- **Domino checks** — entry pins, dead ends and lint problems in the viewer, and `domino check`.
- **Shader CLI** — `shader extract`, `index` and `build`.
- **Dark theme.**
- `.spk` and `.xbt` previews on `.fcb` fields; editor `text_` fields named and greyed out.

### Changed
- The Map tab and `mod lint` read `entitylibrary.fcb`, the library single-player uses.
- The Library tab shows which mods edit an archetype.
- Map tab characters wear their own outfit, skin and colours.

### Fixed
- Female civilians drew only a head and shoulders in the Map tab.
- Map edits with only moves or deletes stayed pending after a save.
- A moved entity without its own `hidAngles` lost its rotation.
- Some MOVE fragments that reshaped a nested state failed to build.
- The released app showed MOVE states as bare hashes.
- `spk import` now writes the imported sound's length.

## [1.1.0-beta] - 2026-09-04

### Added
- **`oasisstrings.rml` is overridden one string at a time** — a localization mod ships one
  `oasisstrings.fragment.xml` per language holding only the strings it changes, instead of all
  11,394. Two mods renaming different weapons never meet. **Breaking:** a whole-file
  `oasisstrings.rml` override is now refused. `jackall-cli rml fragments` writes the patch document,
  and `mod import-legacy` converts one.
- **`<world>.game.xml` splits per mission and per section** — a mod adding an outpost overrides one
  `<Mission>`, and one that raises the shadow radius overrides `_environment.xml`, instead of the
  whole descriptor. Two mods adding different missions finally merge.
- **`_layout.xml`, a container's override unit for what its fragments don't carry** — a sector's
  entities can be re-filed into another mission layer, or deleted one at a time
  (`<delete id="…"/>`), without claiming the whole file. Entity libraries gained group creation and
  `<delete path="…"/>` for archetypes.
- **A world's `omnis`, `managers` and `mapsdata` split per entity** — the last whole-file fallbacks
  the large community mods had. `mapsdata` nests its layers under a per-cell node, so `_layout.xml`
  gained an `under` key. **The fragment cache is invalidated (v5).**
- **Legacy import splits every container, not just `.fcb`** — the fine-grained path runs through
  `IContainerSplitter`, so MOVE graphs, `depload.dat`, the string table and world descriptors import
  as the fragments that actually differ rather than as silent whole-file overrides.
- **Legacy import ignores an editor's float rounding** — floats compare with an 8 ULP interval, and a
  fragment staged for a real edit gets vanilla's own values back everywhere else, so a mod no longer
  arrives claiming edits it never made. Scubrah's Patch drops from 24,924 staged fragments to 7,434.
- **Whole-file fallbacks are reported** — in the app, the CLI and `--json`, with the reason, as are
  the declarations an import leaves behind. All three large community mods now import with none.
- **MOVE animation graphs** — a read-only Move tab, a `move` CLI (`decode`, `encode`, `verify`,
  `clips`, `repoint`, `validate`, `fragments`, `hash`, `names`), and one fragment per state, so
  retargeting one clip no longer ships the whole 1.8 MB graph. `move clips --weapon N` flags clips
  another weapon plays too; `repoint` rewrites only the sites that weapon governs and fails loudly
  when it cannot finish the job.
- **`depload` CLI and fragments** — `decode`, `encode`, `add` and `validate` for the per-world
  dependency index, stageable one resource at a time (~2 KB in place of a 220 KB binary). Writing it
  is what lets a mod ship an animation clip at a path the game never had.
- **`xref reach`, and unused files hidden in the Files tab** — every file classified `used`,
  `used-sp-only`, `used-mp-only`, `unused` or `unknown` by walking the reference graph out from the
  roots `Dunia.dll` itself names. Dead files can be hidden, and editing one asks first. Verdicts for
  the retail corpus ship as `assets/fc2.unused.tsv`.
- **`.xbt`, MOVE and `.rtx` reference extractors** — each closed a source of files that looked
  unreferenced only because nothing parsed the format naming them.
- **`sav clean`, and a Saves tab button for it** — writes a copy of a save with its persisted
  entities dropped, so a modded entity respawns from the current `entitylibrary.fcb`. Mission
  progress, buddies, tapes and diamonds survive. `sav list` lists the saves it finds.
- **`.fc2model` carries the actor** — a pack holding clips also carries the body those clips pose
  (mesh and rig only, ~740 KB), so a weapon's animation is fully visible and a modeller fits the gun
  to hands that are actually in the scene.

### Changed
- **Container splitting is no longer `.fcb`-only** — a format supplies *recognise / open / extract /
  apply* and inherits the three-way merge, load-order folding and `_hash\` addressing unchanged.
- **`depload.dat` browses like a splitting `.fcb`** — one row per resource, under the id a mod stages
  it at, replacing over half a million synthetic rows and the fake folders they grew. Xrefs answer
  for the resource itself, not just the file it sits in.
- **`.spk` reads as events rather than hex rows** — records are grouped under the audio they chain
  to, and the two composite event kinds list what they dispatch to in other banks.
- **A fragment shows what it lives in** — the mission layer or library group it sits in, which its id
  deliberately does not record. An entity whose mission component disagrees with the layer nesting it
  is flagged: the game spawns it from where it sits, so that edit changes nothing on its own.

### Fixed
- A layer holding an `oasisstrings.fragment.xml` can be built again. An inline fragment had no path,
  so its container fell back to the `_hash\<hash>.fcb` name and the build died on "Not an .fcb file".
- An override identical to the base game says so, instead of showing an empty diff.

## [1.0.0] - 2026-08-23

First release.

### Added
- **Mods tab** — an ordered stack of mod zips, later winning, with your own edits in a `workspace\`
  layer pinned last. A mod zip is just a tree of relative paths, so community mods drop straight in.
- **Files tab** — all 13 game archives merged into one browsable filesystem, as the engine resolves
  it, with anything a mod supplies highlighted and diffable against vanilla.
- **Safe, reproducible builds** — every build regenerates `patch.dat` from a one-time
  `patch.dat.vanilla` backup, never from what is on disk, and never writes the base archives. The
  result loads in the stock engine: no DLL, nothing running in the game process.
- **Legacy mod import** — converts an old-style whole-archive `patch.dat`/`patch.fat` mod by diffing
  it against true vanilla, staging only what it actually changed.
- **Cross-mod merging** — two mods editing different parts of the same `.fcb` entity are merged
  three-way rather than one silently overwriting the other; a genuine collision is an error.
- **`.fcb` editor** — entity libraries split one file per entity, decoded to a component/field tree
  rather than hex.
- **Format editors and viewers** for `.rml`, `.sbao` (audio import/export/preview), `.xbt`, `.xbm`,
  `.xbg` and `.rtx` (3D preview), `.sdat`, `.spk` and `.mgb`, plus a text/XML/Lua editor.
- **`.fc2model` export and apply** — collects a model with its materials, textures, rig and
  animations into one file a 3D editor opens, and stages back what the editor changed. The Blender
  add-on that reads it ships beside JackAll.
- **Map tab** — the layers an FC2 world is built from, over a 3D viewport of its terrain. Read-only.
- **Saves tab** — `.sav` metadata, and hand-editing of a save's `PersistenceDB` tree.
- **CLI** — the mod pipeline (`status`, `inspect`, `import-legacy`, `build`, `restore`) and every
  format converter, each with `--json`. This is what the Vortex extension drives.
- **Files nobody has a name for are still moddable** — about 54,000 of the game's 214,000 entries.
  They are sniffed for a type, listed under `_unknown\`, and edited as `_hash\<crc32>.<ext>`.
