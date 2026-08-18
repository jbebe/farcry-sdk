---
sidebar_position: 2
---

# Mod Manager (Vortex)

:::info[Tooling in this repo]
This page documents tools built here — [JackAll](https://github.com/jbebe/farcry-sdk)'s `mod`
commands and the Vortex game extension that drives them. Everything on it is verified by running
against a real Far Cry 2 install, not community-reported.
:::

Far Cry 2 can't have an ordinary mod manager. The engine reads every asset out of packed archives
and the only one a mod may touch is `Data_Win32\patch.dat` / `patch.fat` — there is no loose-file
override, no plugin list, no `~mods` folder. "Installing a mod" means **recompiling that archive
pair** (see [Getting Started](./getting-started.md) for why
`generated\entitylibrarypatchoverride.fcb` is the hook every mod exploits).

So the Vortex extension is a front-end, not an implementation: Vortex handles downloading, staging,
enabling and ordering, and everything that actually needs the game's own archives — converting a
legacy patch, compiling the final `patch.dat` — goes to `jackall-mi`. Deciding *what kind* of mod an
archive is happens first, though, and deliberately doesn't: it's plain string-only checks (see
"What gets recognised" below), not a call out to JackAll. There's nothing to hash or score once an
archive either does or doesn't have a literal `mods\` or `plugins\` folder.

## How a mod reaches the game

1. Vortex installs the mod into its own staging folder (`%AppData%\Vortex\farcry2\mods`), as usual.
2. Deployment puts each mod in its own folder under `<game>\vortex-staging\` — one folder per mod,
   which is exactly JackAll's notion of a **layer**. At this point the game still can't see any of
   it.
3. `did-deploy` runs `jackall-mi mod build`, compiling those layers' game content into `patch.dat`
   and mirroring their `plugins\` payloads into `bin\plugins\` (see "What gets recognised").
4. Purging runs `jackall-mi mod restore`, putting the pristine archive pair back and removing the
   plugin files the build deployed.

`vortex-staging\` sits at the game root and not under `Data_Win32` on purpose: JackAll finds the
game's archives by globbing `*.fat` recursively under `Data_Win32`, so a legacy mod's `patch.fat`
deployed there would be mounted as if it were a shipped game archive.

### Ordering

Layers apply top to bottom and **the bottom one wins** — identical to JackAll's own `config.ini`
rule, and literally the order of `--layer` arguments passed to the build. Two mods overriding
*different fragments* of the same `.fcb` container (different archetypes of an entity library,
different placed entities of a worldsector — see the id space below) never meet at all, and two mods
editing *different parts of the same fragment* are 3-way merged rather than fought over, so ordering
only decides genuine conflicts: two mods editing the *exact same* field differently. For those, the
lower mod in your load order wins outright — same rule as a whole-file override — and Vortex shows a
warning notification naming the fragment and the mods involved, since a headless build has no
interactive way to ask you which edit you meant to keep. Reorder the mods if the loser should have
won, or resolve it by hand in JackAll.App (which shows the same collision as a conflict row instead
of picking a side automatically).

### What gets recognised

Checked in this order:

| # | Archive shape | Handling |
| --- | --- | --- |
| 1 | A `patch.dat`, anywhere in the archive | **Legacy mod.** Its `patch.fat` has to sit beside it — a lone `patch.dat` is rejected with the reason. Converted at install time via `mod import-legacy` into an ordinary layer. **This is how most existing Far Cry 2 mods are distributed** — see [Mods Survey](./mods-survey.md). We can't force any structure on these (they predate this extension), so the pair is all that's recognized — everything else in the archive (readmes, screenshots, alternate versions) is not part of the conversion, and a confirmation dialog says so before install proceeds. |
| 2 | `FCSE.exe`, anywhere | **The FCSE loader/host program itself** (not a plugin). Deployed to `bin\`. |
| 3 | A `plugins\` folder and/or a `mods\` folder | **Mod layer.** See the packaging convention below. The archive shape is staged as-is; the reserved folders are read natively by JackAll at build time — `mods\` compiles into `patch.dat`, `plugins\` mirrors into `bin\plugins\`. |

Anything else is rejected. **Breaking change:** the old `Data_Win32\`-rooted convention is gone —
repack by renaming that folder to `mods`.

None of this calls `jackall-mi` — it's plain string matching over the file list. Only bucket 1 ever
reaches into JackAll (`mod import-legacy`), since converting a legacy patch genuinely requires
diffing against the game's own archives; the others don't need the game discovered at all.

### Packaging a mod

One archive can carry an asset mod, an FCSE plugin, or both, through two reserved top-level folders
(a single wrapper folder above them is fine, and they don't have to share one):

```
MyMod.zip
├─ plugins\                the FCSE plugin payload, deployed to bin\plugins\
│  └─ my_mod\
│     ├─ my_mod.dll        at least one .dll or .lua, at any depth (FCSE loads both)
│     └─ config.ini        everything else under plugins\ ships verbatim alongside it
└─ mods\                   game files, compiled into patch.dat
   ├─ worlds\…
   ├─ generated\entitylibrary.fcb\vehicle\Land\Jeep.xml
   └─ _hash\4A724578.xbt
```

A `plugins\` folder with no `.dll` or `.lua` anywhere inside is not a plugin payload and gets
dropped. The same zip works dropped straight into the JackAll app — the reserved folders are part of
JackAll's layer format, not a Vortex-only convention. They are also the *whole* format: content
anywhere else in a layer, its root included, is ignored. JackAll itself stages workspace edits and
legacy imports under `mods\` for the same reason.

Plugin deployment is manifest-tracked (`bin\plugins\.jackall-plugins.json`): the build makes
`bin\plugins` match the enabled layers — later layer wins a shared path, same as whole-file
overrides — and never overwrites or deletes a file it didn't put there (a hand-installed plugin
survives; the skipped path is reported as a collision warning instead). Disabling the mod and
rebuilding, or restoring, removes exactly the files the build deployed.

### `.fcb` fragment ids

A path whose non-final segment ends in `.fcb` overrides one **fragment** of that container instead
of replacing the whole file. The fragment id — everything after the `.fcb` segment — names one
override unit:

| Container | Unit | Id | Example |
| --- | --- | --- | --- |
| Entity library (`entitylibrary*.fcb`) | one archetype | its `hidName`, dots as folders | `entitylibrary.fcb\vehicle\Land\DLC_Vehicle1_DLC1.xml` |
| World sector (`worldsector*.data.fcb`) | one placed entity | `<hidName>.<disEntityId>.xml` | `worldsector17.data.fcb\StaticObject_201.2058514756624450165.xml` |

For an entity override the **trailing numeric `disEntityId` is authoritative and the name prefix
cosmetic** — an override staged under a since-renamed entity still matches, and `2058514756624450165.xml`
alone works too. An id matching nothing in the vanilla container *adds* that content instead: a new
archetype joins the library's last group, a new entity joins the sector's `main` mission layer. The
pre-per-archetype group ids (`entitylibrary.fcb\NN_Name.xml`) are **rejected outright**: one sitting
in a container folder's root names no fragment, so rather than silently appending a phantom group the
build fails and names the file. Re-export the archetype you meant to change.
Containers whose children carry no name/id (`mapsdata`, `managers`, …) don't split — only a
whole-file override can touch those.

## The one destructive failure mode

Every build regenerates `patch.dat` from `patch.dat.vanilla` and never from what's currently on
disk. That's what makes builds idempotent and disabling a mod a true removal — but it also means
whatever gets captured as "vanilla" is baked into every future build. Capture *someone else's mod*
as the baseline and there is no way back short of reinstalling the game.

`jackall-mi mod status` reports exactly this state as `needsVanillaConfirmation`, and both the
extension and the CLI refuse to build while it holds. The fix is to restore the original files
(Steam: right-click the game → *Verify integrity of game files*) before modding.

:::warning
`PatchBuilder.Build` does **not** guard against this by itself. `EnsureVanillaBackup()` only refuses
when it's given a confirmation callback that returns false, so with no callback — which is every
headless caller — it backs the modded patch up regardless. The JackAll app never hits it because it
always passes a callback; `jackall-mi mod build` has its own check for the same reason.
:::

## `jackall-mi mod` reference

Every command takes `--json`: exactly one object on stdout, progress on stderr, non-zero exit with
`{"ok":false,"error":"…"}` on failure. That's what makes the CLI drivable by a program rather than
just a person.

`jackall-mi` carries the four commands the extension actually drives — `status`, `build`,
`import-legacy`, `restore` — and nothing else. That's what lets it publish trimmed at ~12 MB instead
of `jackall-cli`'s ~37 MB, which matters because users download it. `jackall-cli` still accepts all
four with identical output, plus `mod inspect` and every asset-format command.

### `mod status --game <dir>`

Whether a folder is a usable install, and what state its patch archive is in. Reports an invalid
folder as data (`valid: false` plus a reason), not as an error.

```console
$ jackall-mi mod status --game "C:\Games\Far Cry 2" --json
{"ok":true,"gamePath":"…","valid":true,"hasVanillaBackup":true,"looksModded":false,
 "patchEntries":216,"needsVanillaConfirmation":false}
```

A stock 1.03 `patch.fat` has **216 entries**; `looksModded` is a heuristic on that count.

### `mod inspect <path> [--game <dir>]`

What a folder or zip actually is, and where its tree starts. **`jackall-cli` only** — the extension
classifies archives itself (see "What gets recognised"), so `jackall-mi` leaves this out.

```console
$ jackall-cli mod inspect coolmod.zip --game "C:\Games\Far Cry 2" --json
{"ok":true,"kind":"layer","root":"mycoolmod v1.2","wholeFileOverrides":14,
 "fragmentOverrides":2,"hashAddressed":1,"unknownEntries":0,"pluginFiles":1,…}
```

`kind` is `layer`, `legacy-patch` or `unknown` — a plugins-only tree counts as `layer`, since it
still deploys. `pluginFiles` counts the reserved `plugins\` payload. Root detection scores every
candidate prefix by how many files below it hash to entries the game really has (plugin files count
too), so it can't silently pick a wrapper folder that makes the mod apply nothing. **Pass `--game`**
— without it there's nothing to score against and the tree is reported as-is.

### `mod import-legacy --game <dir> --from <zip|dir> --out <dir>`

Converts a replacement-patch mod into a layer. A legacy patch is ~200,000 entries of which a handful
are the mod, so every entry is diffed against the game's own original and only real differences are
kept — an entity-library `.fcb` one fragment at a time, other `.fcb`s by decoded shape, everything
else byte for byte.

Needs the game's archives mounted, so it isn't cheap; expect tens of seconds.

### `mod build --game <dir> [--layer <dir|zip>]… [--force]`

The deploy. `--layer` is repeatable and **order-significant — later wins**. Nothing reorders or
deduplicates it.

```console
$ jackall-mi mod build --game "C:\Games\Far Cry 2" --layer mods\a --layer mods\b --json
{"ok":true,"totalEntries":231,"vanillaEntries":210,"overriddenEntries":6,"addedEntries":15,
 "outputBytes":10402118,"pluginsDeployed":1,"pluginsRemoved":0,"pluginCollisions":[],"layers":[…]}
```

After writing the archive pair, the build syncs each layer's reserved `plugins\` folder into
`bin\plugins\` (see "Packaging a mod"): `pluginsDeployed`/`pluginsRemoved` count that,
`pluginCollisions` lists paths left untouched because an untracked file already sits there (also
warned on stderr), and each layer entry reports its own `pluginFiles`.

Building with **no** layers is meaningful: it reproduces the vanilla patch byte for byte and
removes every previously deployed plugin file. The archives are only mounted when some layer stages
an `.fcb` fragment override (which needs the vanilla ancestor to merge against) — a whole-file-only
build skips that entirely and takes about a second.

`--force` overrides the vanilla-baseline refusal above. Only pass it when you know the current patch
is stock.

### `mod restore --game <dir>`

Copies `patch.dat.vanilla` / `patch.fat.vanilla` back over the live pair and removes every plugin
file the build deployed (`pluginsRemoved` in the JSON; hand-installed plugins are never touched).
Errors if no backup exists, rather than pretending to succeed.

## Getting it

The extension is published as `vortex-farcry2-<version>.zip`. In Vortex, go to **Extensions**, drop
the zip on the "drag a file here" area, and restart. It bundles `jackall-mi.exe` (self-contained —
no .NET runtime needed), so there's nothing else to install.
