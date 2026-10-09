---
slug: /cli-formats
sidebar_position: 4
title: Convert game files on the command line
description: Pull files out of Far Cry 2's archives and convert models, textures, music, sound banks, game data, menus and animations with jackall-cli's format commands
---

# Convert game files on the command line

Most of `jackall-cli` turns one of Far Cry 2's file formats into something you can edit, and back.
The usual chain is: pull a file out of an archive, decode it, change it, encode it again, and put
the result in a mod layer at the file's own game path under `mods\`. Then build the layer like any
other mod ([Manage mods from the command line](/jackall/cli-mods)).

The format commands work on loose files, so only the ones that take `--game` need the game
installed. Below, `game\` holds files pulled out of the install with the first command, and the
output is what each command printed.

:::note[Nothing to confirm in game]
These commands convert files. What a changed file does in game depends on the change.
:::

## Archives

The game's files sit in `.fat`/`.dat` pairs under `Data_Win32`, such as `common.fat`,
`worlds\worlds.fat` and `sound.fat`. `archive extract` unpacks one. `--names` turns the archive's
hashes back into paths, and `--filter` keeps only the entries whose path contains a piece of text:

```
> jackall-cli archive extract "C:\Games\Far Cry 2\Data_Win32\worlds\worlds.fat" --names --filter ak47.xbg --out-dir game
Extracted 1 file(s) into game
```

The Files tab finds a file across every archive at once; see [Finding any file](/jackall/finding-files).

## Models

`xbg export` writes a model's geometry as a Wavefront `.obj`, the most detailed LOD unless you pass
`--lod` or `--all-lods`:

```
> jackall-cli xbg export game\graphics\weapons\primary\ak47\ak47.xbg --out ak47.obj
Wrote ak47.obj
```

To change a model, use a `.fc2model` pack and the Blender add-on; see
[Edit a model in Blender](/jackall/blender). `fc2model export` makes a pack from a game path, and
`fc2model inspect` lists what a pack holds and what has been changed in it:

```
> jackall-cli fc2model inspect ak47-longer-mag.fc2model
graphics\weapons\primary\ak47\ak47.xbg
┌──────────┬────────┬─────────┬────────────────────────────────────────────────┐
│ kind     │ role   │ changed │ path                                           │
├──────────┼────────┼─────────┼────────────────────────────────────────────────┤
│ mesh     │ owned  │ yes     │ graphics\weapons\primary\ak47\ak47.xbg         │
│ material │ shared │         │ GRAPHICS\_MATERIALS\SDORE2-M-2007073066669286. │
│          │        │         │ xbm                                            │
│ texture  │ shared │         │ graphics\weapons\special\m249saw\m249_bullet_m │
│          │        │         │ .xbt                                           │
…
│ rig      │ owned  │         │ graphics\weapons\primary\ak47\ak47_ref.skeleto │
│          │        │         │ n                                              │
└──────────┴────────┴─────────┴────────────────────────────────────────────────┘
```

`fc2model extract` writes the changed files out as a mod layer:

```
> jackall-cli fc2model extract ak47-longer-mag.fc2model --out ak47-longer-mag
Wrote 1 file(s) under ak47-longer-mag
```

## Textures

`xbt extract` splits a texture into a `.dds` image and an `.xml` header; `xbt build` puts an edited
`.dds` back together with that header:

```
> jackall-cli xbt extract game\graphics\weapons\primary\ak47\ak47_state01_m.xbt --out-dir texture
Wrote texture\ak47_state01_m.dds
Wrote texture\ak47_state01_m.xml

> jackall-cli xbt build texture\ak47_state01_m.dds --out ak47_state01_m.xbt
Wrote ak47_state01_m.xbt
```

Without a header next to the `.dds`, `xbt build` writes a new one, and the `.dds` must then carry
its whole mip chain. See [Replace a texture](/jackall/textures) and
[Texturing a weapon](/docs/modding/texturing-a-weapon).

## Music and sound banks

Music and speech are `.sbao` files around an Ogg Vorbis stream. `sbao build` doesn't convert audio,
so the replacement must already be `.ogg`:

```
> jackall-cli sbao extract game\soundbinary\004b177b.sbao --out-dir music
Wrote music\004b177b.ogg
Wrote music\004b177b.sbaoheader

> jackall-cli sbao build music\004b177b.ogg --out 004b177b.sbao
Wrote 004b177b.sbao
```

Sound effects are `.spk` banks. `spk list` shows what a bank holds, `spk decode` writes it as XML
with its audio beside it, `spk encode` builds the XML back into a bank, and `spk verify` checks
that a bank rebuilds unchanged:

```
> jackall-cli spk list game\soundbinary\004bf5e9.spk
004bf5e9.spk — 3 record(s)
  0x004bf5e9  Sound event  -> 0x004bf5f1
  0x004bf5f1  Sample  -> audio 0x004bf5f3 - 44100 Hz
  0x004bf5f3  Audio  Stereo - 44100 Hz - IMA-ADPCM - 9.5 KB

> jackall-cli spk decode game\soundbinary\004bf5e9.spk --out bank
Wrote bank\004bf5e9.xml

> jackall-cli spk encode bank\004bf5e9.xml --out 004bf5e9.spk
Wrote 004bf5e9.spk

> jackall-cli spk verify 004bf5e9.spk
OK - 004bf5e9.spk rebuilds byte for byte
```

`spk import` replaces one record's audio in place, and `spk new` starts a bank from your own
`.wav` or `.ogg` files. See [Replace music and speech](/jackall/music),
[Replace a sound effect](/jackall/sound-effects) and
[Editing sound banks](/docs/modding/editing-sound-banks).

## Game data and text

`.fcb` files hold the game's objects and values: entity libraries, world sectors, and more.
`fcb decode` writes one as XML and `fcb encode` builds it back:

```
> jackall-cli fcb decode game\levels\w1_c_3\generated\worldsectors\worldsector3160.data.fcb --out worldsector3160.xml
Wrote worldsector3160.xml

> jackall-cli fcb encode worldsector3160.xml --out worldsector3160.data.fcb
Wrote worldsector3160.data.fcb
```

A whole `.fcb` in a mod outranks every other mod that changes the same file instead of merging with
them. The app saves a change as a fragment of the file, which merges; see
[The value editor in depth](/jackall/value-editor).

`rml decode` and `rml encode` do the same for `.rml` documents, such as the game's text in
`languages\<language>\oasisstrings.rml`:

```
> jackall-cli rml decode game\languages\english\oasisstrings.rml --out oasisstrings.xml
Wrote oasisstrings.xml
```

:::warning[`rml fragments` can't be run]
JackAll has a command, `rml fragments`, that turns an edited `oasisstrings.rml` into the few strings
a mod needs. It sits in a second command group also named `rml`, which the first one hides:

```
> jackall-cli rml fragments oasisstrings.rml

Error: Unknown command 'fragments'.

       rml fragments oasisstrings.rml
           ^^^^^^^^^ No such command
```

Until that's fixed, write the strings file by hand as
[Rename a weapon](/jackall/renaming) shows.
:::

## Menus

`.mgb` files are the HUD and menus. `mgb decode` writes one as XML and `mgb encode` builds it back.
`mgb verify` checks a package, and `mgb fragments` writes only the parts that differ from the
game's own, for a mod to ship:

```
> jackall-cli mgb decode game\ui\localized\pc\eng\ui\hud.mgb --out hud.xml
Wrote hud.xml

> jackall-cli mgb verify hud.xml
hud.xml: MAGMA v0x1EAB90, 166 type-table entries, page 1024x768, 69 material(s), 0/0/0 font records, 107 area(s), 190,532 bytes
OK - 699 local reference(s) resolve

> jackall-cli mgb fragments hud.xml --base game\ui\localized\pc\eng\ui\hud.mgb --list
hud.xml: 110 units, 0 differ from vanilla (0 new)
  nothing to stage - this file matches the base
```

See [Edit the HUD and menus](/jackall/hud-and-menus).

## Animations

`graphics\move\movemgr.bin` decides which animation plays when. `move clips` lists the clips one
weapon plays, here the AK-47 (weapon 2), and `--shared-only` keeps the ones another weapon plays
too, which a change would also reach:

```
> jackall-cli move clips game\graphics\move\movemgr.bin --weapon 2 --shared-only
game\graphics\move\movemgr.bin: weapon 2 plays 63 clips (60 exclusive, 3 shared)
  0E1937D3  17x  graphics\characters\_common\animations\weapons\primary\ak47\3rdge_fulb_jamcycle_nodir_prak4_i1.mab  shared with 5, 7, 8, 15, 16, 18, 19, 21, 23, 25, 26, 27, 29, 30, 39, 40
  76C825BC  3x  graphics\characters\_common\animations\weapons\primary\ak47\3rdge_uppb_reload_nodir_prak4_i1.mab  shared with 12
  A4E84FA5  2x  graphics\characters\_common\animations\weapons\primary\ak47\1stge_uppb_reload_+000fw_prak4_i1.mab  shared with 12
```

`move decode` and `move encode` turn the graph into XML and back, `move repoint` points one weapon
at other clips, and `move fragments` writes a changed graph out as the per-situation pieces the app
saves. See [Change which animation plays](/jackall/animations) and [MOVE](/docs/file-formats/move).

## Dependencies, brains and missions

`depload validate` checks a world's dependency list, the index of what to load with what:

```
> jackall-cli depload validate game\worlds\world1\generated\world1_depload.dat
9718 parents, 29723 children.
OK
```

`depload add` registers a new resource in it; see [depload](/docs/file-formats/depload).

`ai lint` lists parameters a brain sets that the game never reads:

```
> jackall-cli ai lint game\scripts\game\newbrains\mercbrain.ai.rml
13,583 nodes; 799 parameter values the engine never reads:
     799  CTaskUpdateBlackboard.UpdateWho (the engine reads updateWho)
```

`ai unpack` writes a brain's source out as XML, and `ai verify` checks that brains recompile
unchanged. See [Tune the AI](/jackall/ai).

`domino check` looks for what would break a mission script in game:

```
> jackall-cli domino check game\domino\user\a1sm01_townescape.a1sm01_mission.lua
┌────────────────────┬──────────┬──────────┐
│ Rule               │ Severity │ Findings │
├────────────────────┼──────────┼──────────┤
│ unfired-out-anchor │ Info     │ 6        │
└────────────────────┴──────────┴──────────┘
1 graph(s); twin: 1/1 clean, 288/288 traced fires matched (23 named by the twin)
box IDs: Slot 126, Twin 23, Wire 111
No errors
```

See [Read a mission](/jackall/missions).

## References

`xref build` indexes every reference between the game's files, once:

```
> jackall-cli xref build --game "C:\Games\Far Cry 2"
Indexed 213,881 files in 14.7s
  edges            : 2,985,738
  definitions      : 39,369
  file refs resolve: 1,005,622/1,162,609 (86.5%)
  written to       : …\.xrefs (58.6 MB)
  215 file(s) come from patch.dat/mods and are indexed per session, not persisted
8 file(s) failed to decode
…
```

Then `xref from` lists what a file uses, here the AK-47's main material and its textures:

```
> jackall-cli xref from graphics\_materials\sdore2-m-2008050631030648.xbm --game "C:\Games\Far Cry 2"
  references                                    site                kind
──────────────────────────────────────────────────────────────────────────────
  graphics\weapons\primary\ak47\ak47_state01_   MaskTexture1        XbmTexture
  m.xbt
  graphics\weapons\primary\ak47\ak47_state02_   MaskTextureBroken   XbmTexture
  m.xbt
  graphics\_textures\diffuse\metal\dirtrust_0   DiffuseTexture2     XbmTexture
  3_d.xbt
  graphics\_textures\specular\infra_bridges_m   SpecularTexture1    XbmTexture
  etal_s.xbt
  graphics\_textures\diffuse\metal\metalbrush   DiffuseTexture1     XbmTexture
  ed_d.xbt
```

`xref to` lists everything that uses a file, and `xref reach` sorts every file into used and unused.
`--index` pointed at JackAll's own `data\.xrefs` shares one index with the app. See
[What uses this file?](/jackall/references).

## More

- `shader extract`, `shader build` and `shader index`: see
  [Replacing a shader](/docs/modding/replacing-a-shader).
- `sav list` and `sav clean`: the command-line side of [Savegames](/jackall/saves).
- `depload decode` and `depload encode`, `move verify`, `move assemble` and `move names`: see the
  format pages for [depload](/docs/file-formats/depload) and [MOVE](/docs/file-formats/move).

Every command has `--help` with its options and an example.
