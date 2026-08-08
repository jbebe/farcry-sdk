# FCSE assets

## `fcse.mgb` — FCSE's own Magma settings page

A standalone Magma UI package (4,421 bytes) declaring one `Page` area, `FCSE_PAGE`, with a row list
and 20 value-control slots. It is work item 1 of `PLAN-own-page.md` (git history: removed in
`cf13c2b`), and it is the prerequisite for FCSE having a settings page at all: a private page bound
to a *shipped* layout shares that layout's `magma::Page` with the stock class that also binds it, so
the two screens become one screen. A layout nothing else binds is the only way out.

Two variants ship, because the UI does: Far Cry 2 has a `pc` set and a `pcwidescreen` set whose
pages differ in size and geometry (`1024x768` / nav at x=83 versus `1280x800` / nav at x=74).
`page_assets.cpp` picks between them using the engine's own flag — see "Aspect" below.

| File | Role |
|---|---|
| `fcse.mgb.xml`, `fcse_widescreen.mgb.xml` | **The sources.** Reviewable, diffable, and the thing to edit. |
| `fcse.mgb`, `fcse_widescreen.mgb` | The binaries FCSE ships. Built from the XML; do not hand-edit. |
| `build_fcse_mgb.py` | Regenerates one XML from a shipped `options.mgb` export. Run it once per variant. |
| `verify_fcse_mgb.py` | Checks the names line up with what FCSE's native code asks for. |

### Rebuilding

Editing the XML needs no game files:

```
jackall mgb encode fcse.mgb.xml            -o fcse.mgb
jackall mgb encode fcse_widescreen.mgb.xml -o fcse_widescreen.mgb
```

Regenerating the XML from scratch does, because the type table, pool counts and *all geometry* are
measured from a shipped package — run it once per aspect, against that aspect's `options.mgb`:

```
Gibbed.Dunia.Unpack.exe "<install>/Data_Win32/patch.fat" <out>

jackall mgb decode "<out>/ui/localized/pc/eng/ui/options.mgb"            -o options.xml
jackall mgb decode "<out>/ui/localized/pcwidescreen/eng/ui/options.mgb"  -o options_ws.xml

python build_fcse_mgb.py options.xml    -o fcse.mgb.xml
python build_fcse_mgb.py options_ws.xml -o fcse_widescreen.mgb.xml
```

Any language works as the source — only geometry and the type table are taken from it, never text.
The generator prints the page size and measured geometry so a mismatched source is obvious.

**A rebuilt `.mgb` reaches the game by rebuilding `FCSE.exe`, not by being dropped in.** Both
variants are embedded into the exe as `RCDATA` resources (`fcse.rc.in`), and there is deliberately
no loose-file path — the game folder is irrelevant to which layout loads, so a stale file can never
shadow the real one. Editing an `.mgb` does relink the exe on its own, without touching any `.cpp`;
see the `OBJECT_DEPENDS` note in [`../CMakeLists.txt`](../CMakeLists.txt) for why that dependency
needs stating explicitly in CMake.

### Aspect

`CMagmaLocalizationUtil::GetLocalizedPackageName` (`0x10554fc0`) builds `"\pc"`, appends
`"widescreen"` when `*(char*)(FUN_1032d910() + 1)` is non-zero, then the language folder. FCSE reads
that same byte, so its page can never disagree with the rest of the menu about which aspect the game
is running. If the read faults, it falls back to the 4:3 layout — slightly wrong decoration geometry
beats no page at all.

`MgbXmlTests.The_committed_fcse_page_package_builds_from_its_committed_xml` fails the build if the
two committed artifacts ever disagree, so a forgotten `mgb encode` cannot ship.

### What the page is made of

Nothing of its own — no materials, fonts or textures. Every visual is an instance of a `common.mgb`
area, and `common.mgb` is always loaded by the time the Options screen appears:

- **`p_menu_nav`** → common `36150990`, the row list and title bar. `CUIPageBase::FetchMagmaElements`
  requires this exact name and requires `l_menu_nav_list` to live *inside* it rather than be a direct
  child. `a_title_bar`/`t_page_title` are inside that same template, so `CMenuPage::SetTitle` works
  with nothing authored here.
- **`p_slot_01` … `p_slot_20`** → common `652FD37C`, the value-list cell. One per settings row.
- **`p_prompts_navbar`** → common `E58F0F6C`, the B/Back prompt strip.
- Five decorative elements carried verbatim from the stock pages. They appear on all four shipped
  settings pages (Game, Display, Sound, Network), so they are options-screen chrome rather than
  Game-specific, and they link only into `common.mgb` — including the two `Image`s, whose
  `MATERIALLINK` names `\common.mgb` as the owning package.

### How FCSE reaches it

`CUIPageBase::Init` hashes the page name and looks it up via
`GenericObjectServer::FindGenericObject`, whose registry `magma::Engine::LoadPackage` fills from
every loaded package's `GenericObjectTable`. So loading this package is what makes the page
resolvable. The table registers two keys for the same page:

| Key | Why |
|---|---|
| `FCSE_PAGE` | What the page object's name field is set to — 9 chars, so it fits MSVC's SSO buffer and needs no heap string. |
| `MAINMENU_FCSE_PAGE_PC` | The stock naming convention, registered so either name resolves. |

Rows are then built with the engine's own
`CSettingsPage::AddBoolSetting(label, "SETTING_LABEL_LIST", "FCSE_SLOT_nn", …)`, which is why both of
those names are `UserData` properties on the page area, each holding a `FullLink` to a widget.

### The 20-row ceiling

`common.mgb` `36150990`'s ListBox declares a 20-row viewport, and the value controls are absolutely
positioned siblings that **do not scroll with the list**. Past 20 rows the labels slide out from
under their controls. So 20 is the cap, and FCSE should log an overflow rather than call
`AddBoolSetting` with a `FCSE_SLOT_nn` this file does not declare — `GetUserDataElement` would miss
and the row would silently have no control.

Geometry is the Network tab's, the highest-anchored stock settings page and therefore the one with
the most usable rows: nav at `(83,111)`, controls at `x=552` from `y=158`, stepping 28. Twenty rows
end at `y=690` on a 768px page.

### The two decoration layers, and why the package is named `UI\fcse.mgb`

Two `Image` elements provide the page's paper and frame:

| Element | Material | Texture |
|---|---|---|
| `#F0CC8C29` (Normal blend) | `notebook` | `\textures\hud\notebook.png` |
| `#E82DE1C0` (Modulate blend) | `frame_color_scratch` | `\textures\common\frame_color_scratch.png` |

Unlike the chrome *areas*, these reference **materials**. The shipped options pages reach across to
`\common.mgb` for them, which does not resolve from a package the engine did not ship — but that is
not the pattern to copy anyway: every page *inside* `common.mgb` that draws these layers declares
the material in its own package (`PACKAGE=""`). So this package declares them too.

That alone is not enough, and the failure is instructive. A material's texture path is stored
UI-root-relative with a leading backslash, and it resolves against **the package's own name**. While
FCSE identified the package by its real absolute path (`C:\…\fcse.mgb`), those paths resolved to
nothing and both images rendered as untextured white quads — `notebook` covers the full screen, so
the entire page washed out.

The fix is in `magma_package.cpp`: the package tells the engine it is **`UI\fcse.mgb`**, exactly like
a shipped one. Nothing needs to exist at that path, because the reader hook matches on the `CPathID`
computed from the string and serves FCSE's own bytes. Identity and storage are simply different
things here.

### Not done here

- **`fcse.mgb.desc`.** Nav-bar prompts are configured from the `.mgb.desc` sibling, but FCSE loads
  the `.mgb` directly through `CEngineNomad::LoadPackage` rather than through
  `CMagmaConfigUIResource`, so a `.desc` would never be read on that path. Revisit if the Back
  prompt does not appear.
- **Slider and choice rows.** `AddSliderSetting` wants common `62EA6603` rather than `652FD37C`.
  Adding a second bank of slots is a small edit to `build_fcse_mgb.py`; it is left out until
  something needs it.
