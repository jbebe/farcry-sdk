---
name: legacy-mod
description: Use when asked to analyze, reverse, document or layer a legacy (community) Far Cry 2 mod, or to take features from an analyzed mod into a new mod
---

# Legacy mods: analyze, document, pick

A legacy mod is taken apart into **changes** (every difference from the base game, at the finest
grain each format allows) and documented as **features** in `legacy/<mod>/`, so any feature can be
picked into a new layer on its own. `legacy/README.md` defines the page format, the address syntax
and the `systems` list - read it first. `legacy/scubrahs-patch/` is the worked example.

Rules that hold throughout:

- `legacy/` is metadata. Never copy a mod's files or paste their contents into it; describe them,
  quote identifiers and values, and name assets by their path in the mod's archive.
- The mod's archive stays in `tmp/mods/` and the analysis in `tmp/legacy/<mod>/`; both are
  gitignored. Never modify `tmp/gamefiles/`.
- Never launch the game. In-game checks are the user's: hand them the exact steps, then ask.
- A legacy mod goes into the game only as its imported layer (`jackall-cli mod import-legacy`,
  Vortex, or JackAll's Import legacy mod). Never copy or offer to copy its `Dunia.dll`, exe, ReShade
  files or any other binary; say which features they carried and which plugin covers them, if any.
- Commit `legacy/<mod>/` (and any tooling fix) straight to main after each major step, by explicit
  path, short subject line only.

`jackall-cli` is `tools/JackAll/src/JackAll.Cli/bin/Release/net10.0/jackall-cli.exe`; build it with
`dotnet build tools/JackAll/src/JackAll.Cli -c Release` if it is missing or stale. Installs: Steam at
`C:\Program Files (x86)\Steam\steamapps\common\Far Cry 2`, GOG at `C:\Games\Far Cry 2`. Each
analysis writes JackAll's `.jackallcache` into the install root it reads.

## Analyze a mod

1. **Slug and inputs.** Pick a kebab-case slug from the mod's name. Find the feature list: the user's
   message, a readme inside the archive (the analysis lists it as `doc`), or the mod's Nexus/ModDB
   page. With no list at all, the list is written in step 5 from the changes.

2. **Baseline.** The base game must be the build the mod was made against, or base-game differences
   between builds read as the mod's changes.
   - A mod that ships `Dunia.dll` was made against the build that DLL patches: `analyze` reports a
     `File` change hinted "a different build of this binary" when the install is the other build.
     Steam's `Dunia.dll` is 20,183,176 bytes, GOG's 19,412,104.
   - Otherwise analyze against Steam. If the changes include files the mod has no reason to touch,
     analyze against GOG into a second work folder and keep whichever lists fewer changes. The two
     builds' vanilla `patch.dat` differ (10.26 MB and 9.31 MB).

3. **Run the analysis.**
   ```
   jackall-cli legacy analyze --game <install> --from "tmp/mods/<archive>" --work tmp/legacy/<slug>
   ```
   It extracts zip/7z/rar, sorts every file into a role (`archive`, `data`, `binary`, `loose`, `doc`,
   `unsupported`), imports the patch archive and any loose archive files, then compares every unit
   field by field. Read every `note`. A mod shipping more than one `patch.fat` (variants, optional
   parts) is refused: analyze each variant folder as its own mod. `unsupported` means archives other
   than `patch` - say so in `mod.md`; no build can carry them.

4. **Survey.** `jackall-cli legacy changes --work tmp/legacy/<slug> --group --limit 0` counts changes by
   shape. Drill in with `--match "<glob>"` (repeatable) and plain listing. What to read:
   - **Hints.** Each change carries what its unit calls itself: an archetype name, an entity name, a
     script's first comment. Mods built with a script generator often say which feature a file is
     for (`-- Generated for "Functional Outposts" v1.0`).
   - **Whole units.** `New` and `File` changes can only be taken whole; their content is in
     `tmp/legacy/<slug>/layer/mods/<unit>`. Read new scripts there to see what they do.
   - **Copies.** A hint ending `redeclares <library>` marks an archetype the mod copied into another
     library to change a few values; its changes are those values against the copy it overrides, so
     each can go to its own feature.
   - **Strings.** New `oasisstrings` entries name new features (actions, popups, shop items).
   - **Binaries.** `install/bin/dunia.dll@0x…` byte runs need the engine: step 6.
   - **Dead edits.** `(dead: the game reads …)` marks a change to an archetype copy a later library
     overrides. Attribute it like any other, and say on the page that it does nothing; `legacy check
     --features` counts them per page.

5. **Write `legacy/<slug>/mod.md`.** Frontmatter: `name`, `version`, `author`, `url`, `archive` (the file
   name in `tmp/mods/`), `sha256` (from `analysis.json`), `baseline` (`steam` or `gog`), `analyzed`
   (date), `source` (where the feature list came from, or `none - inferred from the changes`). Body:
   how the mod ships (archive, binaries, loose files, what the player must do - "start a new game"),
   the bundles, and a `## Published feature list` section holding the list verbatim as `- ` bullets
   (`###` subheadings allowed). With no published list, write one bullet per feature you identify and
   say so in `source`.

6. **Write the feature pages.** One `features/<id>.md` per claim (a component), one bundle per
   headline feature that groups several claims, and noise pages for changes that do nothing. Work
   container by container:
   - Write the rules, then confirm what they catch:
     `legacy changes --work … --mod legacy/<slug> --feature <id> --group`.
   - Prefer rules over a unit's fields (`**/entitylibrary*.fcb/vehicle/**#**/fHealth`) to lists of
     units. Use `exclude` to carve a case out instead of contorting `match`.
   - A whole unit (`New`, `File`, binary file), and a text hunk, cannot be split. When several
     features live in one, give it a `kind: shared` page (`<topic>-shared` or a name for what it is)
     and have each of those features `requires` it; a feature with nothing else is then `located`
     with no rules of its own. Say in each body which part of the shared unit is its own.
   - A sector `_layout.xml` change is one entity placed in one layer
     (`layer[main]/entity[<fragment label>]`), one layer removed or one fragment deleted: claim them
     with the entities they place.
   - Several archives carry the same archetype (`worlds/world1`, `worlds/world2`,
     `generated/entitylibrarypatchoverride.fcb`, `downloadcontent/dlc1`): match them all, or say in
     the body why one is left out. An override copy of a world archetype is already addressed in the
     world libraries; DLC copies and files the game never reads (`worlds/tmpla`) are left out.
   - Binary patches: trace each site in Ghidra through GhidraMCP (`Dunia.dll-Steam` and
     `Dunia.dll-GOG` in the `fc2` project): the function, what the original code does, what the patch
     makes it do, and the same site in the other build found by a masked byte pattern. Record both
     builds' addresses and the pattern in a `## Dunia.dll` table on the page (see
     `legacy/scubrahs-patch/features/reputation-kept.md`), ready for an FCSE plugin
     (`mods/UFCP/CLAUDE.md` says how a fix is built). Work read-only unless the user asks for
     annotations, and pass `dry_run=true` to `disassemble_bytes`, which otherwise writes to the program.
     A patch that rewrites a pushed pointer or a jump-table entry is an absolute address: say so, since
     a plugin computes it from the module base.
   - Look up whether this repo already does the feature (`mods/UFCP/`, `mods/DevTools/`, the other
     plugins' READMEs, `docs/docs/`) and set `existing`.
   - Body: what it does in game, how (units, values, script logic), what it depends on, what is
     uncertain. Short. A claim with nothing behind it is `no-artifact` with the reason; a real claim
     whose changes you cannot find is `unresolved` with what you searched.

   A large mod (thousands of changes, a hundred claims) is best split across agents by domain -
   patrols and world placement, missions and scripts, economy, player and weapons, graphics, the
   binary. First write `mod.md` and the bundle pages with every claim's page id, so the agents share
   one id scheme; give each agent its ids and the containers it owns, and the rule to claim only
   precise addresses outside them. Then integrate: run the check, settle contested changes, give the
   unclaimed ones an owner, and merge the binary trace into the pages that need it.

7. **Check until clean.**
   ```
   jackall-cli legacy check --mod legacy/<slug> --work tmp/legacy/<slug> --features
   jackall-cli legacy changes --work tmp/legacy/<slug> --mod legacy/<slug> --unclaimed --group
   ```
   Clean means every change claimed exactly once, every published claim covered, every page valid.
   Then fill `mod.md`'s coverage line (pages, changes, what stays unresolved) and commit.

8. **What is worth knowing beyond the mod.** A mechanism the mod reveals about the engine (a value's
   real effect, a script hook) belongs in `docs/docs/`; add it only when the user asks, and link the
   feature page to it.

## Pick features into a new mod

1. Find candidates: grep `legacy/*/features/*.md` for the system (`systems: [...]`), title or claim.
   Read the pages; check `requires` and the bundle's `includes`.
2. The analysis must exist and match: rerun step 3 of the analysis if `tmp/legacy/<slug>/` is missing
   (the archive must still be in `tmp/mods/` - its sha256 is in `mod.md`).
3. Build the layer:
   ```
   jackall-cli legacy pick --game <install> --mod legacy/<slug> --work tmp/legacy/<slug> \
     --feature <id> [--feature <id> ...] --out <layer folder>
   ```
   Bundles expand to their components and `requires` is pulled in. Each unit is rebuilt from the
   base game with only the picked changes; a unit that can only be taken whole is copied and listed.
4. Read what it lists `by hand`: binary patches become FCSE plugin code (the page has both builds'
   addresses and a pattern), loose files go beside the game. Nothing in a layer can carry either.
5. Lint and stack: `jackall-cli mod lint --game <install> --layer <layer>`; layers from several mods
   merge per fragment in `mod build`, which reports what two layers both change.
6. Deploy and test per the user's usual routine; the in-game check is theirs.
