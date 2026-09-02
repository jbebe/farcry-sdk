---
sidebar_position: 15
---

# Asset reachability — which shipped files the engine can actually read

:::info[Verified via reverse engineering]
The root set is extracted from **`Dunia.dll`** (retail Steam build): every hardcoded asset path and
every `printf`-style filename template it contains. The closure over those roots is computed by
`jackall-cli xref reach` against a vanilla install. Counts on this page come from that run.
:::

Far Cry 2 ships 214,097 archive entries. Some of them the engine cannot reach by any code path —
authoring twins, console leftovers, development slots that were never stripped. Those files are
noise for anyone reading the data: worse, the largest of them *look* load-bearing. A 5.3 MB
`world1_depload.xml` sitting beside the world's other cooked data reads as the manifest that drives
the level. It is not. The engine loads the `.dat` beside it and only falls back to the XML when the
binary is missing, which never happens in a shipped install.

This page describes how the engine names files, and what falls out when you follow only the names
it can actually produce.

## How a file becomes reachable

Every load ultimately goes through one CRC32 lookup into the mounted archives (see
[fat/dat archives](../file-formats/archives-fat-dat.md)), so the real question is where the string
that gets hashed comes from. There are four sources, and only the third is visible in the data
itself.

**Hardcoded literals.** `Dunia.dll` spells out 153 asset paths — `config\alwaysloaded.xml`,
`graphics\Sky\dome\moon.xbt`, `generated\EntityLibraryPatchOverride.fcb`, the six sky-dome
textures, the two debug console fonts. These are the entry points; nothing in the data references
them.

**Composed names.** Another 49 strings are templates the engine fills in at runtime. These name
the bulk of the game's data and appear nowhere as a stored reference:

| Template | Names |
| --- | --- |
| `%s%s_depload.dat` | the per-world dependency manifests |
| `%sworldsector%d.data.fcb`, `%ssector%d.desc.fcb`, `.preload_x.fcb` | per-sector entity data |
| `%ssd%d.sdat`, `%ssd%d_%s.xbt` | terrain heightfields, atlases and shadow maps |
| `%ssector%d.srl`, `%szonesector%d.zsr` | sector streaming and zone lists |
| `%s%08x.spk`, `%08x.sbao` | sound banks and streams, keyed by resource id |
| `%snv\nv%d_%d.nvm` | navmesh satellites |
| `%s\%s\oasisstrings.rml` | localized string tables |

A file named this way can never be proven dead by a reference search, because no reference exists
to find. The analysis matches these patterns against the shipped tree instead of expanding the
templates — a root only matters if the file shipped, and matching sidesteps re-deriving each
template's iteration domain (how many sectors, which languages).

A template is also what settles whether a twin is a fallback or dead weight. `_depload` has both a
`.dat` and a `.xml` string in the binary, so the readable copy is a real fallback the engine would
read if the binary vanished. `_deploadnewparticles` and `oasisstrings` have only their `.rml`
templates — no `.xml` anywhere in the binary — so their XML sources cannot be loaded under any
condition. Same-looking pair, different verdicts, and only the string table tells them apart.

**Stored references.** Paths and path hashes inside the data: `.fcb` string members, `.xbm` texture
slots, `.mgb.desc` dependency lists, `depload.dat` manifests, an `.xbt` header naming its `_mip0`
companion, a MOVE graph's clip hashes, an `.rtx` species' material slots. This is the only category
a tool can follow, and it is what JackAll's reference index holds.

**Id spaces with no path at all.** Shaders are addressed by permutation id; ~93% of
`shadersobj.fat` has no recoverable name. `.sbao` streams *are* their id — the filename is the hex
resource number. Nothing in this category can be shown unused, so it never is.

## Reading the verdicts

Each file gets a verdict and a flag set. The flags say which shipped mode reaches it; the verdict
summarizes them.

| Verdict | Meaning | Count |
| --- | --- | ---: |
| `used` | reachable from both campaign and multiplayer roots, or from a global one | 84,120 |
| `used-sp-only` | only the single-player worlds reach it | 111,619 |
| `used-mp-only` | only multiplayer maps reach it | 15,454 |
| `unknown` | cannot be decided — see below | 1,819 |
| `unused` | no engine code path names it | 1,085 |

An editor-only file counts as `used` with an `EDITOR` flag rather than as dead: the in-game map
editor ships with the PC game, so its data is genuinely loadable. The flag preserves the
distinction for anyone who wants to trim it anyway.

`unknown` is a deliberate third state, not a rounding error. A file lands there when nothing
*could* have referenced it in a way the tools can read — an unnamed archive entry (nothing can
spell its path), a Havok rig picked by archetype logic that has never been traced, a Domino node
whose graph builds paths from bare names. Silence from a parser that does not exist is not evidence
of death, and collapsing those into `unused` would be the one error that actually costs something.

## Decoys — the files worth knowing about

An unused texture is trivia. An unused 6 MB entity library is a trap: it sits in the world's
`generated\` folder, it is bigger than the file the engine really loads, and it will absorb an
afternoon before you notice nothing reads it. 540 files are flagged `DECOY` — unused *and*
large or full of outgoing references.

The worst offenders, all confirmed dead:

| File | Size | Names | Why it is dead |
| --- | ---: | ---: | --- |
| `worlds\*\generated\entitylibrary_full.fcb` (24×) | 6.2 MB each | ~31,000 | The full archetype set. Single-player takes the suffix-less branch; the flag that selects this one is never set. |
| `*_deploadnewparticles.xml` (24×), `oasisstrings.xml` (10×) | 183 MB total | — | Every RML document ships twice: the `.rml` the engine composes, and the `.xml` it was authored from. The binary holds no `.xml` string for either family — only `%s%s_deploadnewparticles.rml` and `%s\%s\oasisstrings.rml` — so unlike `_depload` these are not even fallbacks. `world2_deploadnewparticles.xml`, at 9.9 MB, is the largest dead file in the game. `patch.dat` ships the same language files **without** their XML sources, so the packaging was tightened later — the base archives were simply never rebuilt. |
| `worlds\*\generated\*_depload.xml` (24×) | up to 5.3 MB | ~1,000 | Readable twin of `_depload.dat`. Only read if the binary is missing. |
| `worlds\tmpla\**` (381 files) | 202 KB manifest + 6.2 MB library | 36,158 | The un-stripped development world slot, reachable only via `-benchmark`. |
| `graphics\move\movemgrnamed.bin`, `dlc1named.bin` | 5.9 MB together | — | Authoring copies of the MOVE graphs with names retained. Listed in `DefaultEngineConfig.xml` under a slot retail code never consumes. |
| `domino\**\*.debug.lua` (411 files) | up to 640 KB | — | Every Domino graph ships twice; the debug twin is a topology oracle. |

Beyond those, 43 console leftovers (`config\presets\{ps3,xenon}\`, `ui\textures\360\`) are
referenced by PC-reachable files — the `.mgb.desc` prompt attributes point straight at the Xbox
button icons — but no PC code path selects them. They are marked `console-only` rather than
`unreachable`, because the distinction is the useful part.

The remaining 161 genuinely orphaned files are leaf assets: unused character clothing textures
(`c_cm_shirt_print*.xbt`), a few editor water materials, some post-FX textures. Cut content, not
traps.

Sizes throughout are uncompressed, the way the archive index and every JackAll view report them.
The archives store these entries compressed, and this text compresses far better than the game's
binary data — `world2_deploadnewparticles.xml` is 9.9 MB expanded but roughly half a megabyte
packed. The whole 183 MB of XML sources costs on the order of 11 MB on disc, which is a fair part
of the answer to why nobody stripped them.

## Running it

```bash
jackall-cli xref build --game "C:\Games\Far Cry 2"
jackall-cli xref reach --game "C:\Games\Far Cry 2" --json
```

`xref reach` prints the decoy table first, then the counts, then a set of ground-truth checks — every
claim on this page is asserted against the run, and the command exits non-zero if one fails. Use
`--explain <path>` to see how a specific file was reached, and `--audit 50` to sample the `unused`
set for manual tracing.

The checked-in result is `tools/JackAll/assets/fc2.unused.tsv` —
the `unused` and `unknown` rows only, since the full classification is 22 MB of TSV that regenerates
in seconds. The roots it starts from are in `assets/engine-roots.tsv`, one line per Dunia.dll string
with the source address in a comment.

## What would change these numbers

The analysis is only as complete as the extractor set: a format nobody parses contributes no edges,
and everything it alone references falls to `unknown`. The current gaps are `.hkx`, `.bank`,
`.apm`, `.ambx`, the facial `.lfe`/`.pfe` pairs, and the Domino node graphs. Closing any of those
would move files out of `unknown` — it cannot move anything into `unused`, which is the direction
that matters.

Two rules are deliberately biased toward false `used`. Paths scraped from reachable text files
propagate even though most are noise, and an `.fcb` name-hash that numerically matches a real path
hash is treated as reaching that file (this is how landmark vegetation resource lists work — see
[terrain and vegetation](terrain-and-vegetation.md)). Both can mark a dead file live; neither can
mark a live file dead.

One hash, `4A724578`, is a real CRC32 collision in the game's own filelist —
`levels\ige_map\generated\sdat\sd10_shadow.xbt` and `scripts\game\barkdata\1436645.bank` hash
identically. A hash-keyed verdict cannot tell which file it describes, so that entry is never
allowed to say `unused`.
