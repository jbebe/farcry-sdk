# Plan: give FCSE a real, own settings page

**Status:** research complete; the native half is **live-confirmed working** via the spike in work
item 0.5. Remaining work is content authoring and file delivery — no further reverse engineering is
blocking. Written and validated 2026-08-07.

Goal: an FCSE page that lives beside Game / Display / Sound / Controller / Network — its own title,
its own layout, its own row capacity — instead of the current approach of hijacking the real
`CFCXOptionGamePage` and hiding its native rows (`src/mod_page.cpp`).

Everything in "What the research found" below was traced live this session via GhidraMCP against
`FarCry2_server` (full `magma::`/`CFCX*` symbols) and cross-checked byte-for-byte against the real
`options.mgb` / `common.mgb` extracted from `patch.fat`. Addresses are `FarCry2_server` unless
marked otherwise; the `Dunia.dll` equivalents still have to be found (see "Work item 0").

---

## What the research found

### A native page binds to its Magma layout **by name**, through the `.mgb` GenericObjectTable

`CFCXOptionGamePage::CFCXOptionGamePage()` (`0x08ad6ae0`) does exactly one interesting thing:

```cpp
CFCXBaseOptionPage::CFCXBaseOptionPage(this,
    /*pageName */ "MAINMENU_OPTIONGAME_PAGE",          // "..._PC" on Dunia.dll
    /*title    */ Localize("OptionMenu", "GAMEOPTION_TITLE"));
```

That pair threads down `CSettingsPage` → `CListMenuPage` → `CMenuPage` → `CUIPage` → `CUIPageBase`.
`CUIPageBase::CUIPageBase` (`0x0912a490`) stores the page name twice: as a `CStringID` at `+0x24`
and as the raw string at `+0x28`/`+0x2c`. `CMenuPage` keeps the title at `+0xcc`/`+0xd0`.

`CUIPageBase::Init()` (`0x09129c60`ish, body at `0x09129ce5`) is what turns that name into pixels:

```cpp
if (pageName.empty()) { m_inited = true; return; }          // silent no-op, no crash
page = GenericObjectServer::FindGenericObject(Id::Hash(pageName));
page = FullLink::GetLastObject(page + 0xc);                 // must IsKindOf(magma::Page)
if (!page) page = CMagmaElementFactory::GetPage(m_pageStringId);   // fallback
SetPage(page);        // -> this+0x14 = the magma::Page*
ConfigPage();
this->FetchMagmaElements();                                  // vtable +0x20
m_inited = true;                                             // this[0x4c]
```

`magma::GenericObjectServer` (`FindGenericObject` @ `0x0a05aa50`) is a **vector of every loaded
package's `GenericObjectTable`**, and `magma::Engine::LoadPackage` (`0x0a03fc90`) registers a
package's table into it automatically on load (`RegisterGenericObjectTable` @ `0x0a05ad40`). So the
`GenericObjectTable` record at the tail of every `.mgb` — the one
[`mgb.md`](../../docs/docs/file-formats/mgb.md) documents as `VisitNamedObject` + `count ×
(nameHash, FullLink)` — **is the global name→widget registry native page classes resolve against.**

Confirmed against real data. `options.mgb`'s table has 16 entries; resolving the hashes against
`Dunia.dll`'s string pool:

```
GO BEBA1985 MAINMENU_OPTIONGAME_PAGE_PC   -> [options, C16854EF]   <- area[5], Page, 16 elements
GO FBFAB3BF MAINMENU_OPTION_DISPLAY_PC_PAGE -> [options, B0D7EA4C]
GO E47C4159 MAINMENU_OPTIONSOUND_PAGE     -> [options, AF9E9045]
GO B8E16D30 MAINMENU_OPTION_NETWORK       -> [options, 400736ED]
GO 3EB7F8E4 MAINMENU_OPTION_BRIGHTNESS    -> [options, 8E25D0EC]
GO 9D1B2651 MAINMENU_OPTION_PAGE          -> [options, EE623A00]
...
```

### What `FetchMagmaElements` demands of the layout

`CUIPageBase::FetchMagmaElements` (`0x0912a7f0`) looks up, by hardcoded name, inside the bound Page:

| Name | What it is | Stored at |
|---|---|---|
| `p_menu_nav` | `PageInstance` element wrapping the nav template | — |
| `l_menu_nav_list` | `ListBox` inside that template | `this+0x8` (Element), `this+0xc` (ListBox) |
| `a_title_bar` → `t_page_title` | the title `Text` | `this+0x10` |

`CListMenuPage::AddButton` (`0x0912c950`) is then just
`magma::ListBox::AddItem(this+0xc, label, 0)` + `SetItemDisabled` + push the handler into a parallel
`CryVector`. **If `this+0xc` is null it silently returns −1 and does nothing** — a missing ListBox is
a no-op, not a crash.

### How the Game tab's *value controls* work — the thing to copy

`CFCXOptionGamePage::RefreshOptionList` (`0x08ada3c0`) builds its rows with:

```cpp
AddBoolSetting     (label, labelListParamName, "SETTING_<name>", yesText, noText, enabled, handler);
AddSliderSetting   (label, labelListParamName, "SETTING_SENSITIVITY", 0, 10, enabled, handler);
AddValueListSetting<uint>(label, labelListParamName, "SETTING_<name>", n, labels, values, ...);
```

`CSettingsPage::AddBoolSetting` (`0x09132730`) → `CListMenuPage::AddButton` for the label row, then
`CValueListSetting<bool>::FetchMagmaElements(setting, page->m_magmaPage, labelListParam, settingParam)`
→ `CUISettingBase::FetchMagmaElements` (`0x09134210`) → `magma::UserData::GetUserDataElement(...)`.

So **both of those `char*` arguments are `UserData` property names on the page's own Magma Area**,
each holding a `FullLink` to a widget. Dumped from the real `options.mgb`, area `C16854EF`:

```
LAYER                 tag=0x02 u32 = 10
SETTING_LABEL_LIST    -> options, C16854EF, p_menu_nav, 36150990, l_menu_nav_list
SETTING_MOUSE_SMOOTH  -> options, C16854EF, F3BC8C48,   652FD37C, D240E092
SETTING_INVERTYAXIS   -> options, C16854EF, 6AB5DDF2,   652FD37C, D240E092
SETTING_CROSSHAIR     -> options, C16854EF, 1ADF297D,   652FD37C, D240E092
SETTING_DIFFICULTY    -> options, C16854EF, 6DD819EB,   652FD37C, D240E092
SETTING_SUBTITLE      -> options, C16854EF, F4D14851,   652FD37C, D240E092
SETTING_AMBX          -> options, C16854EF, 83D678C7,   652FD37C, D240E092
SETTING_MACHETE       -> options, C16854EF, 13696556,   652FD37C, D240E092
SETTING_SENSITIVITY   -> options, C16854EF, 84BBBCDE,   62EA6603, slider
```

and the matching elements:

```
[ 0] Placeholder  47CC8C92 "action"
[ 4] PageInstance 3D23C3C5 "p_menu_nav"        link(pkg=common, area=36150990)
[ 5] PageInstance 6AB5DDF2                     link(pkg=common, area=652FD37C)   <- one per setting
[ 6] PageInstance F3BC8C48                     link(pkg=common, area=652FD37C)
[ 7] PageInstance 84BBBCDE                     link(pkg=common, area=62EA6603)   <- the slider row
[ 8..12] PageInstance x5                       link(pkg=common, area=652FD37C)
[14] PageInstance E58F0F6C "p_prompts_navbar"  link(pkg=common, area=E58F0F6C)
```

**A settings page has a fixed, authored number of value-control slots** — the Game tab has 8. That
is the hard ceiling FCSE is fighting today, and the single best reason to author our own page.

Every template it instantiates lives in **`common.mgb`** (`pkg = E5EC7051 = CRC32("common")`,
verified; `D035FA87 = CRC32("options")`), which is always loaded:

| common.mgb area | Contents | Role |
|---|---|---|
| `36150990` | `Placeholder "action"`, `ListBox l_menu_nav_list`, `AreaInstance a_title_bar`, `Image` | the row list + title bar |
| `652FD37C` | `Placeholder "action"`, `ListBox D240E092` | one value-list cell |
| `62EA6603` | (slider row) | one slider cell |
| `E58F0F6C` | | nav-bar prompts |

So an FCSE page package needs **no materials, no fonts, no textures of its own** — just a `Page`
area whose elements are `PageInstance`s pointing into `common.mgb`, plus the `UserData` FullLinks
and a one-entry `GenericObjectTable`.

### The title is settable after all

`CMenuPage::SetTitle(CryStringBase<wchar_t> const&)` (`0x09131710`) stores the string at `+0xcc/0xd0`
and, if `this+0x10` (the `t_page_title` `TextBase`) is bound, does
`wstring::assign(text+0x18, s)` → two vtable calls (`+0xc4`, `+0xb4`) → three `u16` writes at
`+0x3a/0x3c/0x3e` → `magma::TextBase::UpdateBoundingBox`.

This supersedes the "real-title change closed out unresolved" note in
[`magma-menu-system.md`](../../docs/docs/engine-internals/magma-menu-system.md) and in memory. The
title is a plain setter; it just needs `FetchMagmaElements` to have run first.

### Why the previous private-instance attempt (Path C) crashed

`src/mod_page.h` records that a privately-constructed second `CFCXOptionGamePage` "crashed inside
`CGameMenu::SwitchPage`'s activate call", and that chasing missing fields one at a time (`+0xec`,
then still crashing) looked open-ended.

The research above explains it, and it was never a missing *field*:

1. **`Init()` was never called on the private instance.** Nothing else calls it —
   `CGameMenu::AddPage<T>` (`0x0897ef40`) doesn't, and `SwitchPage` (`0x0912b5e0`) doesn't; it only
   calls the old page's `Hide` (vtable `+0x10` on ELF / `+0xc` on MSVC) and the new page's `Display`
   (`+0xc` / `+0x8`). Without `Init()` the object has **no magma Page, no ListBox, no title Text**
   — `this+0x8/0xc/0x10/0x14` are all null.
2. Even if `Init()` *had* been called, the private instance carries the same page name as the real
   Game tab, so both objects would have bound to the **same** `magma::Page` and fought over it.

`CUIPageBase::Display` (`0x0912a3a0`ish) null-guards `this+0x14`, so the fault is in a
`CMenuPage`/`CSettingsPage` override further down — but the fix direction is the same either way:
**give the page its own name, make that name resolve, and call `Init()`.** That is exactly what a
new `.mgb` page delivers.

---

## The plan

### Work item 0 — port the addresses to `Dunia.dll` — **mostly done 2026-08-07**

The critical ones are confirmed by decompile against `Dunia.dll` (Steam v1.03) and renamed/commented
in the shared Ghidra project. The `tmp/anchors_all_confirmed.jsonl.written.jsonl` xport pass had
already matched two of them at tier-medium/0.81; both held up.

| Symbol | `Dunia.dll` | How confirmed |
|---|---|---|
| `CUIPageBase::Init` | **`0x10109410`** | calls `Id::Hash` (`0x10aa7150`) → `0x10108860` → `SetPage`; empty-string early-out; identical shape to ELF `0x09129c30` |
| `CUIPageBase::FetchMagmaElements` | **`0x10109150`** | contains the literals `"p_menu_nav"`, `"l_menu_nav_list"`, `"a_title_bar"`, `"t_page_title"`; writes `+0x8`/`+0xc`/`+0x10`; ~20 subclass overrides call it as their base |
| `CUIPageBase::SetPage` | **`0x101090d0`** | writes `this+0x14 = page`; renamed `CUIPageBase_SetPage` |
| `GenericObjectServer::FindGenericObject` + `GetLastObject` + `IsKindOf(Page)` | **`0x10108860`** | renamed `Magma_FindPageByIdHash` |
| `CMagmaElementFactory::GetPage` (fallback) | `0x10187700` | called with `this+0x24` |
| `CMenuPage::DoInit` | `0x10cdb5a0` | `"ui/common.mgb"`, `"SOUNDEVENT_BACK"` |
| `CListMenuPage::DoInit` | `0x10cdbe20` | `"ui/common.mgb"`, `"SOUNDEVENT_SELECT"` |

**`CUIPageBase` field layout on `Dunia.dll`, read straight off `Init`** — this closes the plan's
"`CryStringBase` layout is inferred, not read" risk:

```
+0x14  magma::Page*            (bound by SetPage)
+0x24  CStringID of page name  (fallback lookup key only)
+0x2c  page-name string        MSVC std::string SSO - inline chars while capacity < 0x10,
+0x3c    size                                          otherwise a heap pointer
+0x40    capacity
+0x68  inited flag (byte)
+0x8/+0xc/+0x10   Element / magma::ListBox / magma::TextBase  (bound by FetchMagmaElements)
```

**Consequence worth designing around: keep the page name ≤ 15 characters.** It then lives inline in
the object's own SSO buffer, so setting it is `memcpy` + two integer writes with no allocation, no
heap ownership and no `CryStringBase` refcount emulation at all. Use `FCSE_PAGE` (9) or
`FCSE_MODS_PAGE` (14) rather than the `MAINMENU_FCSE_PAGE_PC` (21) used elsewhere in this document.

Still to find, none of them blocking the spike below:

| Symbol | Why |
|---|---|
| `CMenuPage::SetTitle` | real page title. Not in the xport list; not reachable by name search. Cheaper alternative to check first: `CMenuPage::Display` re-applies the page's *stored* title every display, so writing that string field (same SSO shape, ELF `+0xcc`/`+0xd0`) may be enough without calling anything |
| `CMagmaFacade::FindPackage` / `FindPage` | `FindPackage` is `FUN_105355b0` (from `CMenuPage::DoInit`, takes `"ui/common.mgb"` — note packages are looked up by **path**, not by the `CRC32("common")` name hash the FullLinks use) |
| `CEngineNomad::LoadPackage` (vtable `+0x8`) + `CFileNameNomad` ctor | loading our own package (work item 3) |

Already known and reused unchanged: `AddButton 0x10cdbb80`, `CGameMenu::SetNextPage 0x101d1bc0`,
`SwitchPage 0x101d1990`, `CFCXOptionPage::Setup 0x1081aee0`, `CFCXOptionGamePage` ctor `0x1081e9c0`
(size `0x210`), `RefreshOptionList 0x10820160`, `CFCXOptionGamePage::FetchMagmaElements` (the
function containing `0x10821699`), `ownerPage+0x140 = CGameMenu*`.

### Work item 0.5 — the spike: prove a private page can be `Init`-ed and displayed

**Written 2026-08-07 as `src/page_spike.{h,cpp}`; builds clean, not yet run.** Off unless
`bin\fcse.ini` contains:

```ini
[FCSE]
Page spike = true
Page spike name = MAINMENU_OPTION_NETWORK    ; optional, this is the default
```

It reads that flag straight out of the ini rather than through `SettingsRegistry`, so it never
appears as a menu row or gets written into a plugin's group, and it is installed *after*
`ModPage::Install` in `mods_tab.cpp` so a fault in the diagnostic cannot cost the shipped menu its
row. Every native touchpoint is SEH-wrapped; if the name fails to resolve it logs and declines to
add the Options row rather than offering a page that would display nothing.

Do this **before** authoring anything. The riskiest unknown is not the file, it is whether a
privately constructed page survives `Init()` + `Display()` — precisely what crashed twice before.
That is testable with zero authoring and zero delivery work:

1. Construct the private `CFCXOptionGamePage` exactly as the abandoned Path C did
   (`new byte[0x210]{}` + ctor `0x1081e9c0`), set `+0x140` = live `CGameMenu*`.
2. Overwrite the page name at `+0x2c`/`+0x3c`/`+0x40` with a ≤15-char name that already resolves —
   pick a `Page` area in `options.mgb` that no native class binds to, so there is no contention over
   the `magma::Page` (the four GenericObjectTable entries whose hashes do **not** appear in
   `Dunia.dll`'s string pool — `BDC06926`, `4D57B8E2`, `3A01A65E`, `16BE0729` — are the candidates;
   confirm one is unused in SP first). Failing that, temporarily reuse
   `MAINMENU_OPTION_NETWORK` and accept that `CFCXOptionNetworkPage` also binds it.
3. Call `CUIPageBase::Init` (`0x10109410`), SEH-wrapped, and log the resulting `+0x14`/`+0xc`/`+0x10`.
4. Reach it the way Path C already did: write `CGameMenu+0x3c`, call `SwitchPage`.

Pass condition: a visually distinct screen appears, `+0x14` and `+0xc` are non-null in `fcse.log`,
and `AddButton` rows land on it. That validates the entire native half of the plan; everything after
it is content authoring and file delivery.

#### Result — PASSED, live, 2026-08-07

```
PageSpike: enabled, target magma page "MAINMENU_OPTION_NETWORK"
PageSpike: private page constructed
PageSpike: CUIPageBase::Init returned - bound state follows
PageSpike:   magma::Page (+0x14) = 0x0C7A6A40
PageSpike:   row list Element (+0x08) = 0x0A34DA20
PageSpike:   row ListBox (+0x0C) = 0x0A1B261C
PageSpike:   title TextBase (+0x10) = 0x0A1AEBC0
PageSpike:   inited flag (+0x68) = 0x00010001
PageSpike: switching to the spike page
PageSpike: SwitchPage returned without faulting
PageSpike: appended 2 row(s) from inside the per-display rebuild
```

A separate screen displays, carrying the borrowed Network layout, and shows exactly FCSE's two rows
— the native rows are gone, so the clear-rows call works on a private page too. No SEH exception
anywhere, across repeated entries. **`Init()` was the entire missing piece**; there were never any
additional unset fields, contrary to what the abandoned attempt concluded.

Three things the run settled beyond the pass condition:

- **The title is a page-object field, not a layout property.** The borrowed Network page rendered
  `"Game options"`, i.e. the `GAMEOPTION_TITLE` string `CFCXOptionGamePage`'s own ctor stored, pushed
  into the layout's `t_page_title` widget. So an FCSE page gets its own title by setting that field
  (or calling `SetTitle`); it does not come from, and cannot be baked into, the `.mgb` — the title
  widget lives in the shared `common.mgb` `a_title_bar` template.
- **Rows must be appended from inside the per-display rebuild.** Anything added at construction time
  is wiped: `CFCXOptionGamePage::RefreshOptionList` clears the row list on every display. The first
  spike run demonstrated this exactly (`AddButton` succeeded, no row appeared). The shipped Mod
  Configuration Menu already had to learn this in 2026-08; it applies to any private page too.
- **`CFCXOptionGamePage::RefreshOptionList` runs on our page whether we want it or not**, because
  the private instance shares its class and therefore its vtable. On the borrowed Network layout its
  `SETTING_*` lookups simply miss and it is harmless, but see the first open question below.

### Work item 1 — author `fcse.mgb`

A standalone package (name `fcse`, `CRC32 = 4C52BD58`), authored with JackAll's C# `.mgb` codec
(`tools/JackAll/src/JackAll.Tools/Format/Mgb/`, already a byte-exact writer). Start by copying
`options.mgb`'s area `C16854EF` as the template and stripping it down.

Contents:

```
header            copied verbatim from options.mgb (bytes 0..0x2A6 are a build-wide constant)
materials/fonts   none
areas             1 x Page "FCSE_PAGE"
  UserData:
    LAYER              = 10
    SETTING_LABEL_LIST -> [fcse, FCSE_PAGE, p_menu_nav, 36150990, l_menu_nav_list]
    FCSE_SLOT_01       -> [fcse, FCSE_PAGE, p_slot_01,  652FD37C, D240E092]
    ... FCSE_SLOT_NN   (author generously - 24 or 32; this is the row ceiling)
  Elements:
    Placeholder  "action"
    PageInstance "p_menu_nav"       link(pkg=common, area=36150990, dup=1)
    PageInstance "p_slot_01".."NN"  link(pkg=common, area=652FD37C, dup=1)
    PageInstance "p_prompts_navbar" link(pkg=common, area=E58F0F6C, dup=1)
  each slot positioned by copying the Game page's per-row keyframe RectStates and stepping Y
GenericObjectTable
  1 entry: Id::Hash("MAINMENU_FCSE_PAGE_PC") -> FullLink [fcse, FCSE_PAGE]
```

Verification before ever launching the game: the file must round-trip byte-exactly through the C#
codec and decode identically under `mgb_parser.py` (the existing two-implementation differential
check), and
the GO entry must resolve with the same script used above.

### Work item 2 — deliver the file

FC2 has **no loose-file override** ([getting-started.md](../../docs/docs/modding/getting-started.md)),
so FCSE must provide it. Two routes, in preference order:

- **(a) Absolute path.** `FUN_102358a0` (the VFS resolver) checks `FUN_10231510(path)` for `:` or a
  leading `\\` and, if absolute, goes straight to `CreateFileW`. The archives page calls this "the
  raw filesystem escape hatch, already reachable for any absolute path with zero engine
  modification." If `CFileNameNomad` accepts an absolute path this needs **no hook at all** — try
  this first, it is by far the cheapest.
- **(b) Hook `VFS_ResolvePath`** (`FUN_102358a0`, `this` = `DAT_10ff0ef8`) and serve
  `ui\fcse\fcse.mgb` from `bin\fcse\`. This is the same interception ModPatcher already uses for
  loose `.sbao` overrides, so it is known to work.

Deliberately **not** doing: editing `options.mgb`. It would mean patching 12 localized variants
(`{pc,pcwidescreen} × {eng,fre,ger,ita,spa,cze}`) and either repacking `patch.fat` — which collides
with every other mod — or transforming the bytes at intercept time from C++. A standalone package is
one file and touches nothing shipped.

### Work item 3 — load the package

At the same hook point `mods_tab.cpp` already owns (`CFCXOptionPage::Setup`, fires lazily the first
time Options opens — well after `common.mgb` is up), once per session:

```
CFileNameNomad name(<path>);
Package* pkg = CEngineNomad::LoadPackage(name);       // virtual, Dunia vtable+0x8
```

`magma::Engine::LoadPackage` registers our `GenericObjectTable` globally as its last step, so
`MAINMENU_FCSE_PAGE_PC` becomes resolvable from that moment on. Log the returned `Package*`; a null
means the file didn't resolve and FCSE should fall back to today's behaviour rather than proceed.

Every native touchpoint stays SEH-wrapped, as the rest of `mod_page.cpp` already does.

### Work item 4 — construct and initialise the page

```
page = new byte[0x210]{};                       // size from AddPage<CFCXOptionGamePage>
CFCXOptionGamePage::ctor(page);                 // 0x1081e9c0 - correct vtables for free
memcpy(page+0x2c, "FCSE_PAGE", 10);             // <=15 chars => MSVC SSO, inline, no allocation
*(u32*)(page+0x3c) = 9;                         // size
*(u32*)(page+0x40) = 15;                        // capacity
page[+0x140] = <live CGameMenu*>                // read from ownerPage+0x140
page[+0xec]  = <the Options page>               // what AddPage<T>'s real caller passes
CUIPageBase::Init(page);                        // 0x10109410 - the step Path C missed
CMenuPage::SetTitle(page, L"Mod Configuration");// address still TBD, see work item 0
```

`+0x24`'s `CStringID` only feeds the `CMagmaElementFactory::GetPage` fallback, which never runs once
the GenericObject lookup succeeds — leave it alone unless the primary path misses.

Then the existing `PagePushHandler` mechanism reaches it unchanged: write the page pointer into
`CGameMenu+0x3c` and call `CGameMenu::SwitchPage` directly — never `SetNextPage`, never the
hashtable (that is `CGameMenu_PageTable_InsertNode`, which crashes, and which this design never
needs).

Two decisions to make while implementing:

- **Overwriting the name string vs. calling `CFCXBaseOptionPage`'s ctor with our own.** Overwriting
  needs the exact `CryStringBase<char>` layout on MSVC (a `{heap*, char*}` pair with a refcounted
  header before the data — build a static buffer with a saturated refcount so nothing frees it).
  Calling the base ctor directly is cleaner but then the `CFCXOptionGamePage`-specific vtable/field
  writes have to be replicated by hand. Prefer overwriting; it keeps the real ctor's work intact.
- **`RefreshOptionList` hooking.** Once the page has its own identity, the `g_appendPending` flag
  hack goes away entirely: hook the vtable slot on *our instance only*, or just call our own
  row-builder from the page's `Display`. The stock Game tab is then never touched at all.

### Work item 5 — real settings rows

With `FCSE_SLOT_nn` authored, FCSE can call the engine's own
`CSettingsPage::AddBoolSetting(label, "SETTING_LABEL_LIST", "FCSE_SLOT_nn", yes, no, enabled, handler)`
instead of the current caption-row-with-`[ON]`/`[OFF]`-suffix workaround. That gives real toggles
with the game's own look, and `AddSliderSetting`/`AddValueListSetting<uint>` open up
`FCSE_SettingType_Slider` / `_Choice` in `include/plugin_api.h` (currently `Checkbox` only) without
inventing any UI.

Cap the row count at the authored slot count and log an overflow rather than calling
`AddBoolSetting` with a `FCSE_SLOT_nn` the file doesn't declare — `GetUserDataElement` would miss,
and `AddBoolSetting` handles a null value widget but the row would silently have no control.

---

## Risks and open questions

- **`CFCXOptionGamePage::RefreshOptionList` runs on our page too** (same vtable) — **confirmed
  live** — and it hardcodes the Game tab's own `SETTING_*` names, which our page won't declare. On
  the borrowed layout the misses are harmless, and routing content through the existing hook (call
  the original, clear rows, append ours) works. It is still the engine building rows we then throw
  away, and it installs the *Game* nav-bar handlers on our page.

  **Dropping to a less-derived class does not fix this — `CFCXBaseOptionPage` is abstract.** Tried
  live 2026-08-08 (ctor `0x1087ec80`): construction succeeds, `Init` binds the layout, the page
  displays correctly with its own title and rows — and Back kills the process with R6025 *pure
  virtual function call*. The input path hits a pure virtual before dispatch reaches any menu
  handler; an instrumented handler vtable logged nothing at all on that press while logging slot 1
  for an ordinary row click. Its content-build slot (`vtable+0x3c` → `0x10a21200`, a bare `RET`)
  is an empty folded virtual and is **not** evidence of concreteness — that inference was wrong.

  It also wires no nav-bar handlers of its own: `CFCXBaseOptionPage::Display` makes buttons 1 and 2
  visible but only `RefreshOptionList` ever calls `SetItemHandler`. Wiring them by hand is
  straightforward (`CNavBarModule` at page `+0x10c`, `GetButton` `0x1018a390`, `SetItemHandler`
  `0x10189c00` — read off `RefreshOptionList`'s disassembly at `0x10820195`), but it does not help
  while the class stays abstract.

  **Conclusion: stay on the concrete leaf.** The remaining real concern is not the wasted rows —
  they are cleared before the player sees them — but that the Game page's Accept handler may write
  unbound values back into the player profile (`GetOptionsFromProfile` and friends). The targeted
  fix, not yet done, is to re-wire nav-bar buttons 1–3 with FCSE's own handlers *after*
  `RefreshOptionList` runs, using the three addresses above.
- ~~**`CryStringBase` layout on MSVC** is inferred from the GCC build, not read.~~ **Resolved
  2026-08-07** — it is a plain MSVC `std::string` (SSO at `+0x2c`, size `+0x3c`, capacity `+0x40`),
  read directly off `CUIPageBase::Init`'s own decompile. Keeping the page name ≤15 chars avoids the
  heap case entirely.
- **Cross-package `PageInstance` links** are proven to work between `options` and `common` in
  shipped data, but never yet from a package the engine didn't ship. Load order is the thing to
  watch: `common.mgb` must be loaded before `fcse.mgb`, which the Options-screen hook point
  guarantees.
- **`CMagmaElementFactory::GetPage(CStringID)`** (`0x09283040`) is a plain linear scan over a
  `CFactoryPage*` vector — a second, hook-free registration point if the GenericObjectTable route
  disappoints. Not needed if work item 1 lands.
- **Nav-bar prompts** (`p_prompts_navbar`, `CNavBarModule` at page `+0xd4`) are configured from the
  `.mgb.desc` XML sibling, which a standalone package would also need if the B/Back prompt should
  appear. Untested; the page should still function without it.

## Reproducing the data dumps

```
tools/third-party/FC2_DuniaTools/Gibbed.Dunia.Unpack.exe "<install>/Data_Win32/patch.fat" <out>

cd tools/JackAll/src/JackAll.Tools/Format
python mgb_dump_generic_objects.py <out>/ui/localized/pc/eng/ui/options.mgb
python mgb_dump_area.py            <out>/ui/localized/pc/eng/ui/options.mgb C16854EF
```

Both scripts CRC32 every printable ASCII run in `Dunia.dll` to build a hash→string dictionary, which
is what turns the raw name hashes above back into `SETTING_MOUSE_SMOOTH`, `l_menu_nav_list`, etc.
They sit next to `mgb_dump.py` and hardcode the Steam install path at the top.
