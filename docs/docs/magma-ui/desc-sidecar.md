---
sidebar_position: 3
---

# The `.mgb.desc` sidecar

:::info[Plain XML — no tooling needed]
Structure and vocabulary are read off all 50 shipped `ui\localized\pcwidescreen\eng\ui\*.mgb.desc`
files; the load behaviour is traced from `CMagmaConfigUIResource::LoadResourceInMagma`
(`FarCry2_server 0x096077a0`, `Dunia.dll 0x10554a40`). Frequencies are counts over those 50 files.

This corrects the [Almost Complete Guide](../modding/guide/file-management.md), which says `.mgb`
and `.desc` "can only be edited with a hex editor". That is true of `.mgb` only — a `.desc` is
well-formed XML you can edit in Notepad.
:::

Every `.mgb` ships beside a `.mgb.desc`, and **the `.desc` is the entry point**: the engine loads
the sidecar, and the binary is the last thing that sidecar pulls in. It carries two independent
halves that happen to share a file:

```xml
<package>
  <configuration>   <!-- per-page settings the owning C++ class reads  (15 of 50 files) -->
  <dependencies>    <!-- the resource manifest and load order          (50 of 50 files) -->
</package>
```

## `<dependencies>` — the manifest

Exactly one root `<CMagmaConfigUIResource>` per file, describing the `.desc` itself, with a flat
list of children:

```xml
<dependencies>
  <CMagmaConfigUIResource ID="ui\localized\pc\eng\ui\options.mgb.desc" crc_ID="1766041805" version="2">
    <CMagmaUIResource     ID="ui\localized\pc\eng\ui\options.mgb"      crc_ID="3136939932" />
    <CMagmaConfigUIResource ID="ui\localized\pc\eng\ui\fonts.mgb.desc" crc_ID="615711406" />
    <CMagmaConfigUIResource ID="ui\localized\pc\eng\ui\common.mgb.desc" crc_ID="3065158306" />
    <CTextureResource ID="ui\textures\common\option_sketch.xbt" crc_ID="1106929129" />
  </CMagmaConfigUIResource>
</dependencies>
```

`LoadResourceInMagma` walks that child list, **recursing only into nested
`CMagmaConfigUIResource` entries** (depth-first), and then as its final step loads its own paired
`.mgb` through `CMagmaUIResource::LoadPackageInMagma`. So the list is simultaneously the manifest and
the load order, and the guarantee it buys you is: **every package your screen depends on is already
loaded and registered before your own binary is read.** That is why a page can instance
`common.mgb` areas and resolve `\fonts.mgb` font families without doing anything itself.

Resource kinds across the corpus:

| Element | × | Attributes |
|---|---|---|
| `CTextureResource` | 1,005 | `ID`, `crc_ID` — an `.xbt` |
| `CMagmaConfigUIResource` | 79 nested (+50 roots) | `ID`, `crc_ID`, `version="2"` on roots only |
| `CSoundResource` | 67 | `Type`, `crc_Type`, `ID`, `crc_ID`, `IsFilename`, `Size`, `nbChildren` — a `.spk` |
| `CMagmaUIResource` | 52 | `ID`, `crc_ID` — the paired `.mgb` |
| `CBinkResource` | 4 | `ID`, `crc_ID` — a `.bik`, for `BINKFILENAME` areas |

`ID` paths are backslash-separated and relative to the data root; the loader prepends a literal
`"UI\"` when it builds the `.mgb`'s filename, so what you write here must match the archive layout.

**`crc_ID` is unresolved.** It is not a plain CRC32 of the `ID` string (several variants tried), and
nothing on the `.mgb` load path verifies it — most likely a build-time cache key from Magma's asset
pipeline. Copy it from a sibling entry or leave a shipped value in place; nothing observed reads it.

## `<configuration>` — per-page settings

Present in 15 of 50 files. Its children are **page names** — the same namespace as the
`GenericObjectTable` keys your `.mgb` exports and the string a native page passes to
`CUIPageBase::Init`:

```xml
<configuration>
  <MAINMENU_OPTIONGAME_PAGE_PC>
    <navbar name="p_prompts_navbar">
      <default>
        <b_prompt2 show="1" text="Generic;APPLY" />
        <b_prompt3 show="2" text="Generic;DEFAULT" />
      </default>
    </navbar>
  </MAINMENU_OPTIONGAME_PAGE_PC>
</configuration>
```

Each page's block holds whatever its owning C++ class asks for. There is no general schema — this is
a bag of per-screen config, and an unrecognised block is simply never read. `hud.mgb.desc` configures
16 pages, `mp_menus` 14, `common` 13.

### `navbar` — the one general-purpose block

89 of the corpus's configuration blocks are a `navbar`, and it is the only one that is not
screen-specific.

```xml
<navbar name="p_prompts_navbar">
  <default>
    <b_prompt1 show="1" show_pc="0" text="PauseMenu;JACKAL_FILES_PLAY_TAPE" />
  </default>
  <notapes>
    <b_prompt1 show="0" />
  </notapes>
</navbar>
```

- **`name`** is the element in your page that hosts the prompt strip — `p_prompts_navbar` in all 47
  that set it, i.e. the `PageInstance` of `common #E58F0F6C`. Omitting it is legal (42 do).
- **The children are named states.** `default` (89) is the resting one; a page may declare others —
  `done`, `notapes`, `weapons` are the three in shipped data — and native code switches between them
  as the screen's state changes. This is the only place in the whole UI data set where *conditional*
  presentation is expressed declaratively.
- **Prompt slots** are the button element names inside that strip: `b_prompt1` (74), `b_prompt2` (40),
  `b_prompt3` (27), `b_prompt4` (17), `b_prompt5` (1), plus `b_previous`/`b_next` (1 each).

Prompt attributes, with corpus counts:

| Attribute | × | Meaning |
|---|---|---|
| `show` | 161 | `1` visible (122), `0` hidden (32), `2` (7) — a third state, meaning unconfirmed |
| `text` | 125 | An OASIS reference in `Section;KEY` form, e.g. `Generic;ACCEPT`, `MultiMenu;GAMER_CARD` |
| `button` | 33 | Which physical button the prompt names (`A`, `B`) |
| `icon_xenon`, `icon_ps3` | 29 each | Per-console icon overrides |
| `show_pc` / `show_PC` / `show_xenon` | 9 / 2 / 11 | Per-platform visibility overrides on top of `show` |
| `text_xenon` | 2 | Per-console text override |

`Section;KEY` is a two-part OASIS lookup, unlike a `Text` widget's bare key — the sections come
straight from `oasisstrings.xml` (`Generic`, `MultiMenu`, `PauseMenu`, …).

The strip is driven by `CNavBarModule` / `CNavBarLayout` / `CNavBarButton` / `CNavBarPageHandler`, a
**separate, non-`magma`-namespaced hierarchy** that bridges into Magma rather than being part of it;
the literals `b_prompt1`–`b_prompt4` and `p_prompts_navbar` exist in the binary as element names.

### The screen-specific blocks

The rest of the vocabulary belongs to one page each. Catalogued here so you recognise them rather
than to imply they generalise:

| Block | Seen in | Shape |
|---|---|---|
| `hudprompt` / `equipmentprompt` | `hud` | `name` plus `<Prompt path="…"/>` and `<IconSet path="…" frame="n"/>` |
| `layout_list` | `controller`, `ige_menus` | `<layout layoutName="LAYOUT_DEFAULT">` → `<control_info controlButton="LT" controlText="IRONSIGHT" secondaryText="REVERSE"/>` (128 entries) |
| `Stats` | `mp_menus` | `<Section>` → `<Stat name="…"/>` (140 entries) |
| `help_menu_topics` | `ige_menus` | `<topic>` → `<subtopic name text/>` (35 entries) |
| `avatar_list` | `sp_avatar` | `<avatar …/>` (9) |
| `infoBox`, `iconMaterial`, `pingIntervals`, `server_operation_types`, `hudsetup` | MP/HUD | single-purpose config |

The `path` attributes are worth noticing on their own: `a_inventory_object/a_inventory_icons_anim/a_inventory_icons`
is a **slash-separated path of readable element names** into the Magma tree. Since the `.mgb` keeps
only CRC32 hashes, these strings are one of the few places real names survive — hash them and you
recover names the binary lost.

## Authoring notes

- **Hand-edit it.** No decode step, no tooling. Keep it well-formed; nothing validates it for you.
- **Adding a texture, sound or Bink file to a screen means adding a `<dependencies>` entry**, or the
  resource is not loaded when your `.mgb` asks for it.
- **Adding a page name to `<configuration>`** only does something if a C++ class reads that block.
  For nav-bar prompts that class already exists, so a `navbar` block on your own page name is the one
  configuration you can add and expect to work — untested, but it is the mechanism every stock page
  uses.
- **A `.desc` is only read on the `CMagmaConfigUIResource` path.** A mod that hands the engine a
  package directly through `CEngineNomad::LoadPackage` — which is what FCSE's file-reader hook does —
  never triggers a `.desc` read, so a sidecar beside it would be ignored. If your back prompt does
  not appear, this is why.

## Unknowns

- **`crc_ID`** — not a CRC32 of `ID`, not verified at load time; actual derivation unknown.
- **`show="2"`** — a third visibility state, used 7 times, meaning unconfirmed.
- **Whether an unrecognised `<configuration>` block is ignored or logged.** Assumed ignored, since the
  blocks are pulled by name by their owning class; not traced.
- **`poufToutDunCoup`** — a single prompt attribute in the corpus, almost certainly leftover dev
  scaffolding, no known effect.
