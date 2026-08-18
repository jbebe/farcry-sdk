# Far Cry 2 extension for Vortex

Adds Far Cry 2 support to [Vortex](https://www.nexusmods.com/about/vortex/). Install mods from
Nexus, enable and order them, deploy, purge — the normal Vortex loop, for a game whose engine
doesn't load loose files at all.

## Why it isn't a normal game extension

Far Cry 2 reads every asset out of packed archives, and the only one a mod may touch is
`Data_Win32\patch.dat` / `patch.fat`. There is no loose-file override, no plugin list, no `~mods`
folder. "Installing a mod" means **recompiling that archive pair** with the mod's files spliced in.

[JackAll](../JackAll) already does that, correctly and reversibly. So this extension doesn't
reimplement any of it — anything that actually needs the game's own archives shells out to
`jackall-mi`, and confines itself to the parts Vortex is actually good at. Classifying *what kind*
of mod an archive is happens first, though, and is deliberately plain string matching, not a call to
JackAll - see "What it installs" below.

```
Vortex                                  jackall-mi
──────                                  ───────────
download, extract, stage      ──────>   (classified locally - no CLI call)
install a legacy patch mod    ──────>   mod import-legacy diff it against the base game
deploy layer folders          ──────>   mod build        compile layers into patch.dat
purge                         ──────>   mod restore      put the pristine patch.dat back
```

Each mod deploys into its own folder under `<game>\vortex-staging\` — one folder per mod, which is
exactly JackAll's notion of a *layer*. Deployment on its own changes nothing the game can see; the
`did-deploy` handler is what produces the real artifact.

`vortex-staging\` deliberately sits at the game root rather than under `Data_Win32`: JackAll finds
the game's archives by globbing `*.fat` recursively under that folder, so a legacy mod's `patch.fat`
deployed there would be mounted as if it were a shipped game archive.

## Safety

Every build regenerates `patch.dat` from `patch.dat.vanilla`, never from what's currently on disk.
That single rule buys a lot:

- deploying twice produces identical bytes,
- disabling a mod and redeploying genuinely removes it,
- purge is a true restore, not an unwind,
- a failed build leaves the game untouched (JackAll writes a temp file and swaps it in at the end).

The one situation that can't be undone is capturing an *already modded* `patch.dat` as the vanilla
baseline. The extension checks for it when the game is activated and asks, rather than guessing, and
refuses to build until it's resolved.

## Load order

Layers apply top to bottom and **the bottom one wins** — the same rule as JackAll's own mod list.
Two mods overriding different fragments of the same `.fcb` container (different archetypes, different
placed entities) never meet at all, and two editing different parts of the same fragment are merged
rather than fought over, so order only decides genuine conflicts.

Entries aren't individually toggleable: Vortex's own enable/disable already decides what's applied.

## What it installs

An archive is exactly one of three buckets, checked in this order - plain string matching over the
file list, no `jackall-mi` round trip involved:

| # | Archive shape | Handling |
| --- | --- | --- |
| 1 | A `patch.dat`/`patch.fat` pair, anywhere | **Legacy mod.** Converted at install time via `mod import-legacy`, keeping only what genuinely differs from the base game. This is how most existing Far Cry 2 mods are distributed. We can't force any structure on these, so the pair is all that's recognized - anything else in the archive is not part of the conversion, and a dialog says so before install proceeds. |
| 2 | A `.dll` under a `plugins\` folder | **FCSE plugin.** Deployed to `bin\plugins\`. Nothing to do with the archive pipeline — FCSE is a runtime DLL loader. Extra files alongside it are normal, not warned about. |
| 3 | Files rooted under a literal `Data_Win32\` folder | **Asset mod.** That prefix (plus any wrapper folder above it) is stripped, and what's left (`worlds\…`, `generated\…`, `_hash\<crc32>.<ext>`, `<container>.fcb\<fragment id>` such as `generated\entitylibrary.fcb\vehicle\Land\Jeep.xml`, …) is staged as an ordinary layer. Deliberately strict: no fuzzy root-guessing beyond the literal folder name. |

`FCSE.exe` itself (the loader, not a plugin) is recognized separately and deployed to `bin\`.

Only bucket 1 ever calls `jackall-mi` (`mod import-legacy`) - converting a legacy patch genuinely
needs the game's archives to diff against. Buckets 2 and 3 don't need the game discovered at all.

## Building

```powershell
./build.ps1              # publishes jackall-mi into dist\bin, then bundles the extension
./build.ps1 -SkipCli     # JS only - much faster while iterating
npm run typecheck
npm test                 # loads the built bundle against a stubbed vortex-api
```

`dist\` is the extension folder: drop it (or a zip of its *contents*) into Vortex via
**Extensions → drag a file here**, then restart Vortex.

Set `JACKALL_MI` to an absolute path to point a running Vortex at a local `dotnet build` of the CLI
instead of the bundled copy — the fast loop when you're changing JackAll and the extension together.

## Layout

```
info.json          extension manifest Vortex reads
index.js           the whole extension, bundled (webpack)
gameart.jpg        640x360 tile - placeholder; replace with real key art before publishing
bin\
  jackall-mi.exe  self-contained, no .NET runtime needed
  .itemhashes      hash -> filename dictionary
  .fcbclasses      .fcb class/member name-and-type config
```

## Source

| File | |
| --- | --- |
| `src/index.ts` | wiring: what gets registered |
| `src/game.ts` | discovery, `requiredFiles`, setup, the vanilla-baseline dialog |
| `src/jackall.ts` | the only place that runs `jackall-mi` and parses its `--json` output |
| `src/installers.ts` | classifying an archive and turning it into install instructions |
| `src/loadOrder.ts` | the load order page, and the ordered layer list the build consumes |
| `src/deploy.ts` | `did-deploy` → build, `did-purge` → restore |
| `src/ui.ts` | notification/dialog wrappers |
