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
legacy patch, compiling the final `patch.dat` — goes to `jackall-cli`. Deciding *what kind* of mod an
archive is happens first, though, and deliberately doesn't: it's three plain, string-only checks (see
"What gets recognised" below), not a call out to JackAll. There's nothing to hash or score once an
archive either does or doesn't have a literal `Data_Win32\` folder.

## How a mod reaches the game

1. Vortex installs the mod into its own staging folder (`%AppData%\Vortex\farcry2\mods`), as usual.
2. Deployment puts each mod in its own folder under `<game>\vortex-staging\` — one folder per mod,
   which is exactly JackAll's notion of a **layer**. At this point the game still can't see any of
   it.
3. `did-deploy` runs `jackall-cli mod build`, compiling those layers into `patch.dat`.
4. Purging runs `jackall-cli mod restore`, putting the pristine archive pair back.

`vortex-staging\` sits at the game root and not under `Data_Win32` on purpose: JackAll finds the
game's archives by globbing `*.fat` recursively under `Data_Win32`, so a legacy mod's `patch.fat`
deployed there would be mounted as if it were a shipped game archive.

### Ordering

Layers apply top to bottom and **the bottom one wins** — identical to JackAll's own `config.ini`
rule, and literally the order of `--layer` arguments passed to the build. Two mods editing
*different parts of the same* `.fcb` container are 3-way merged rather than fought over, so ordering
only decides genuine conflicts: two mods editing the *exact same* field differently. For those, the
lower mod in your load order wins outright — same rule as a whole-file override — and Vortex shows a
warning notification naming the fragment and the mods involved, since a headless build has no
interactive way to ask you which edit you meant to keep. Reorder the mods if the loser should have
won, or resolve it by hand in JackAll.App (which shows the same collision as a conflict row instead
of picking a side automatically).

### What gets recognised

An archive is exactly one of three buckets, checked in this order:

| # | Archive shape | Handling |
| --- | --- | --- |
| 1 | A `patch.dat`/`patch.fat` pair, anywhere in the archive | **Legacy mod.** Converted at install time via `mod import-legacy` into an ordinary layer. **This is how most existing Far Cry 2 mods are distributed** — see [Mods Survey](./mods-survey.md). We can't force any structure on these (they predate this extension), so the pair is all that's recognized — everything else in the archive (readmes, screenshots, alternate versions) is not part of the conversion, and a confirmation dialog says so before install proceeds. |
| 2 | A `.dll` under a `plugins\` folder | **FCSE plugin.** Deployed to `bin\plugins\`; nothing to do with the archive pipeline patch.dat is built from. Extra files alongside it (its own data/config) are normal and not warned about. |
| 3 | Files rooted under a literal `Data_Win32\` folder | **Asset mod.** Everything up to and including that folder is stripped, and what's left (`worlds\…`, `generated\…`, `_hash\<crc32>.<ext>`, `<container>.fcb\NN_Name.xml`, …) is staged as an ordinary JackAll layer. Deliberately strict: a wrapper folder above `Data_Win32\` (`MyMod v1.2\Data_Win32\…`) is fine, but there's no fuzzy root-guessing beyond that — a mod either uses this convention or it doesn't. |

`FCSE.exe` itself (the loader/host program, not a plugin) is recognized separately and deployed to
`bin\`.

None of this calls `jackall-cli` — it's plain string matching over the file list. Only bucket 1 ever
reaches into JackAll (`mod import-legacy`), since converting a legacy patch genuinely requires
diffing against the game's own archives; buckets 2 and 3 don't need the game discovered at all.

## The one destructive failure mode

Every build regenerates `patch.dat` from `patch.dat.vanilla` and never from what's currently on
disk. That's what makes builds idempotent and disabling a mod a true removal — but it also means
whatever gets captured as "vanilla" is baked into every future build. Capture *someone else's mod*
as the baseline and there is no way back short of reinstalling the game.

`jackall-cli mod status` reports exactly this state as `needsVanillaConfirmation`, and both the
extension and the CLI refuse to build while it holds. The fix is to restore the original files
(Steam: right-click the game → *Verify integrity of game files*) before modding.

:::warning
`PatchBuilder.Build` does **not** guard against this by itself. `EnsureVanillaBackup()` only refuses
when it's given a confirmation callback that returns false, so with no callback — which is every
headless caller — it backs the modded patch up regardless. The JackAll app never hits it because it
always passes a callback; `jackall-cli mod build` has its own check for the same reason.
:::

## `jackall-cli mod` reference

Every command takes `--json`: exactly one object on stdout, progress on stderr, non-zero exit with
`{"ok":false,"error":"…"}` on failure. That's what makes the CLI drivable by a program rather than
just a person.

### `mod status --game <dir>`

Whether a folder is a usable install, and what state its patch archive is in. Reports an invalid
folder as data (`valid: false` plus a reason), not as an error.

```console
$ jackall-cli mod status --game "C:\Games\Far Cry 2" --json
{"ok":true,"gamePath":"…","valid":true,"hasVanillaBackup":true,"looksModded":false,
 "patchEntries":216,"needsVanillaConfirmation":false}
```

A stock 1.03 `patch.fat` has **216 entries**; `looksModded` is a heuristic on that count.

### `mod inspect <path> [--game <dir>]`

What a folder or zip actually is, and where its tree starts.

```console
$ jackall-cli mod inspect coolmod.zip --game "C:\Games\Far Cry 2" --json
{"ok":true,"kind":"layer","root":"mycoolmod v1.2","wholeFileOverrides":14,
 "fragmentOverrides":2,"hashAddressed":1,"unknownEntries":0,…}
```

`kind` is `layer`, `legacy-patch` or `unknown`. Root detection scores every candidate prefix by how
many files below it hash to entries the game really has, so it can't silently pick a wrapper folder
that makes the mod apply nothing. **Pass `--game`** — without it there's nothing to score against
and the tree is reported as-is.

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
$ jackall-cli mod build --game "C:\Games\Far Cry 2" --layer mods\a --layer mods\b --json
{"ok":true,"totalEntries":231,"vanillaEntries":210,"overriddenEntries":6,"addedEntries":15,
 "outputBytes":10402118,"layers":[…]}
```

Building with **no** layers is meaningful: it reproduces the vanilla patch byte for byte. The
archives are only mounted when some layer stages an `.fcb` fragment override (which needs the
vanilla ancestor to merge against) — a whole-file-only build skips that entirely and takes about a
second.

`--force` overrides the vanilla-baseline refusal above. Only pass it when you know the current patch
is stock.

### `mod restore --game <dir>`

Copies `patch.dat.vanilla` / `patch.fat.vanilla` back over the live pair. Errors if no backup
exists, rather than pretending to succeed.

## Getting it

The extension is published as `vortex-farcry2-<version>.zip`. In Vortex, go to **Extensions**, drop
the zip on the "drag a file here" area, and restart. It bundles `jackall-cli.exe` (self-contained —
no .NET runtime needed), so there's nothing else to install.
