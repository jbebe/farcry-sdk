---
sidebar_position: 11
---

# The Menu System — `CGameMenu`, Magma Pages, and Building a Mod Configuration Menu

:::info[Verified via reverse engineering]
Traced live via GhidraMCP across two binaries: `Dunia.dll` (Steam v1.03, stripped function names but
retained RTTI/class-name strings) and `FarCry2_server` (the unstripped Linux dedicated-server build,
which links the same portable menu/UI code with full real symbols — see
[Overview](./overview.md)). Where a fact is confirmed on one binary only, that's stated explicitly.
Everything on this page was checked against live, running code (decompiles, disassembly, or an
actual working/shipped feature) — not inference alone, unless it says otherwise.
:::

FCSE (`tools/FCSE`) gives plugins a configuration page in-game. It authors its **own** Magma package,
feeds it to the engine through a hooked file reader, and binds a private page to it by name — a
genuinely separate page, built by privately constructing a second instance of a real compiled page
class and reaching it without touching `CGameMenu`'s page hashtable. This page covers the native menu
system that makes that possible (distinct from the `.mgb`/Magma *binary format* itself, which
[its own page](../file-formats/mgb.md) covers).

| Concern | File |
| --- | --- |
| Page construction, installation, shared state | `src/ui/fcse_page.{h,cpp}` |
| Row building and reading values back | `src/ui/page_rows.cpp` |
| Slot-cell cache and EditBox binding | `src/ui/page_slots.cpp` |
| The private class-vtable overrides | `src/ui/page_vtable.cpp` |
| Engine offsets, call shapes, string layouts | `src/ui/engine_page_abi.h` |
| The `IMenuItemHandler` FCSE hands the engine | `src/ui/menu_item_handler.h` |
| Serving `fcse.mgb` through the hooked reader | `src/ui/magma_package.{h,cpp}` |
| Picking and validating the embedded package | `src/ui/page_assets.{h,cpp}` |
| The Options-screen hook | `src/ui/mods_tab.{h,cpp}` |
| The settings store behind the page | `src/api/settings_registry.{h,cpp}` |

The ABI those files compile against is documented on its own page:
[the settings page ABI](./fcse-settings-page-abi.md).

For the data side of the same problem — authoring the `.mgb` a page binds to — see
[Magma UI](../magma-ui/index.md), in particular
[binding a page to native code](../magma-ui/patterns.md#binding-a-page-to-native-code).

## Class hierarchy

Confirmed via `FarCry2_server`'s real class-hierarchy-info construction code (the `sm_HierarchyInfo`
lazy-init pattern every page class has, one link in the chain built per class the first time it's
touched):

```
CUIPageBase
  └─ CMenuPage
       └─ CListMenuPage            (adds AddButton - the row-list primitive)
            └─ CSettingsPage       (adds AddBoolSetting/AddSliderSetting/AddValueListSetting<T>/AddUISetting<T>)
                 └─ CFCXBaseOptionPage
                      ├─ CFCXOptionGamePage        ("Game" tab)
                      ├─ CFCXConsoleOptionDisplayPage → CFCXOptionDisplayPage   ("Display" tab)
                      ├─ CFCXOptionSoundPage        ("Sound" tab)
                      ├─ CFCXConsoleControllerOptionPage → CFCXControllerOptionPage ("Controller" tab)
                      └─ CFCXOptionNetworkPage      ("Network" tab)
```

`CFCXOptionPage` itself (the tab-*selector* page — the screen showing the row of category buttons)
sits at the same level as the leaf tabs conceptually but is its own class, extending
`CFCXBaseOptionPage`. Its whole job is building that row of buttons; each leaf class above builds its
*own* actual settings content (sliders, checkboxes) when the player navigates into it.

`CGameMenu` is a separate, non-page class that owns page instances and drives switching between them
(see below) — every top-level main-menu screen (Story Mode/Multiplayer/Options/Credits/Exclusive
Content) is a page registered with one top-level `CGameMenu`, and `CFCXOptionPage` internally uses the
exact same `AddButton`/handler pattern to build its own row of five category buttons.

## Building a row of buttons

Every button-list screen in the game (`BuildMainMenu`'s six main-menu buttons, `CFCXOptionPage`'s five
category buttons) is built with the identical repeated pattern:

```cpp
handler = new CSetNextPageMenuHandler(ownerPage, &targetCStringId, /*handler=*/nullptr, /*flag=*/true);
label   = /* build a wchar_t* label - see below */;
CListMenuPage::AddButton(ownerPage, label, /*visible=*/true, handler);
```

| Symbol | `Dunia.dll` (Steam v1.03) | `FarCry2_server` |
|---|---|---|
| `BuildMainMenu` | `0x108c8830` | — |
| `CFCXOptionPage::Setup` | `0x1081aee0` | `0x08ad4590` |
| `CListMenuPage::AddButton` | `0x10cdbb80` | (real signature confirmed via mangled symbol) |
| `CSetNextPageMenuHandler::CSetNextPageMenuHandler` | `0x10188ea0` | `0x0912ebc0`/`0x0912ec10` |
| `CBaseCommandMenuItemHandler::CBaseCommandMenuItemHandler` (base ctor) | `0x10188e20` | — |
| `CMemMng::NMalloc` (generic allocator) | `0x10228f30` | — |
| `CSetNextPageMenuHandler::SwitchPage` (the real click-time page switch) | `0x10188d00` | `0x0912ec60` |

**`CListMenuPage::AddButton`** real signature (confirmed via `FarCry2_server`'s mangled symbol
`_ZN13CListMenuPage9AddButtonEPKwbP16IMenuItemHandler`):
`void* __thiscall AddButton(CListMenuPage* this, wchar_t const* label, bool visible, IMenuItemHandler* handler)`.

**Critical, empirically confirmed constraint**: `AddButton` is only safe to call with a `this` whose
real object layout matches what it reads/writes (`+0xc` null-check guard, `+0xd4`, `+0x168`/`+0x16c`
row array). It is safe when called with `BuildMainMenu`'s own menu-list object, or with
`CFCXOptionPage::Setup`'s own `this` (exactly what that function itself does, 5 times, every time
Options opens) — **and it crashes the game** if called with `CFCXOptionPage`'s own top-level pointer
instead (a different, incompatible class layout).

**`0x1084fa90` is *not* `CFCXOptionPage::Setup`**, despite superficially similar code (it also calls a
chain of get-or-create tab-descriptor getters). It fires eagerly, before intro videos even play, and
hooking it to call `AddButton` crashes the game. The *real* `Setup()` (`0x1081aee0`) is invoked only
via a data/vtable xref (never a direct `CALL` anywhere in the binary) — i.e. genuine lazy virtual
dispatch, fired only when Options is actually opened: a row-append hooked there logs exactly once per
session, only once Options is opened, not at boot.

A page's row list is a real `magma::ListBox` (`l_menu_nav_list`, bound at `this+0xc`) and `AddButton`
is a plain `ListBox::AddItem` on it, so row order is insertion order into that one widget; inserting
into the middle means going at the `ListBox` directly rather than through `AddButton`.

### The click handler (`IMenuItemHandler`)

Confirmed via `FarCry2_server`'s mangled symbols: `IMenuItemHandler` is a tiny interface —
`Activate(unsigned int)`, `ActivateParent(unsigned int)`, plus a virtual destructor. **Not** part of
the `CMenuPage` class hierarchy above.

A handler does not have to be the engine's own `CSetNextPageMenuHandler`. FCSE hand-rolls its own: a
plain struct whose first member is a `void**` vtable pointer, with a small hand-built vtable array
(one real slot pointing at the handler function, the rest safe no-ops) — `MenuItemHandler<Payload>` in
`src/ui/menu_item_handler.h`, one mechanism over three payloads. Slot 1 is the activate slot — see
[the settings page ABI](./fcse-settings-page-abi.md#menu-item-handlers). `ActivateParent`'s slot is
not identified.

## Page switching — `CGameMenu`

Fully decompiled on `FarCry2_server`, with every `Dunia.dll` address below structurally confirmed.

| Method | `FarCry2_server` address | `Dunia.dll` address | What it does |
|---|---|---|---|
| `CGameMenu::GetPage(CStringID const&)` | `0x0912b860` | `0x101d1b90` | Pure hashtable **lookup** — returns 0 if not found. Does **not** create on demand. Confirmed: `Dunia.dll` version calls a lookup helper then compares against the same end-sentinel field (`this+0x14`) `Shutdown` (below) also uses, returning `*(node+0xc)` on a hit or 0 otherwise. |
| `CGameMenu::SetNextPage(CStringID const&)` | `0x0912b940` | `0x101d1bc0` | Runs the identical hashtable-lookup helper `GetPage` uses (`FUN_101f7a90`, same `+0x14` sentinel check), and stores the hit into `this+0x3c` — precisely the field `SwitchPage` reads as "next page". |
| `CGameMenu::SwitchPage()` | `0x0912b5e0` | `0x101d1990` | The real transition. Confirmed structurally: old page (`this+0x40`) gets a vtable call first, then new page (`this+0x3c`) gets its owning-`CGameMenu` backpointer set (`+0x20`) and a vtable call, then current/next pointers are swapped. **Vtable slot numbers differ from the Linux server build** — here it's slot `+0xc` on the *old* page (deactivate) and slot `+0x8` on the *new* page (activate), vs. `+0x10`/`+0xc` on `FarCry2_server`. Expected: different compilers (MSVC vs. GCC/Itanium ABI) place virtuals at different indices, not a contradiction. |
| `CGameMenu::CGameMenu()` (ctor) | — | `0x101d1d70` | Confirmed via matching field layout: zeroes the same `+0x38`/`+0x3c`/`+0x40` fields `SwitchPage`/`Shutdown` operate on, then allocates and registers a small self-registration helper object stored at `+0x34` (torn down by the dtor). |
| `CGameMenu::~CGameMenu()` (dtor) | — | `0x101d1ce0` | Confirmed: same vtable pointer as the ctor sets, tears down the `+0x34` helper object. |
| `CGameMenu::Shutdown()` | — | `0x101d1b20` | Confirmed: walks the page hashtable (same `+0x14` sentinel `GetPage` uses) and calls `FUN_101088f0` (`CUIPageBase::Shutdown`) on every entry. |
| `CSetNextPageMenuHandler::SwitchPage()` | `0x0912ec60` | `0x10188d00` | What a real button's click ultimately calls: `GetPage` + `SetNextPage` + `CGameMenu::SwitchPage` (in that exact call order), plus a `"default_ui_transition"` sound/effect trigger. |
| `CGameMenu::AddPage<T>()` (one compiled instantiation per class `T`) | e.g. `0x0897be50` for `<CFCXOptionPage>` | e.g. `0x107d7ab0` for `<CFCXOptionGamePage>` | **Compile-time template** — a per-class get-or-create of a page instance in `CGameMenu`'s own hashtable, not a one-shot constructor. Only works for classes the game itself was compiled with; there is no generic runtime "create by name" factory. Hashtable fields: `+0x10`=miss sentinel, `+0x14`=internal list sentinel *pointer* (not a bucket bound), `+0x1c`=node array base, `+0x28`=bucket mask, `+0x2c`=element count. |

Three name-matching candidates do not hold up on decompile: `FUN_1071ab20` is an unrelated
`CFCXScoreboardService` singleton accessor, not `SetNextPage`; `FUN_1011cec0` is not the ctor
(different vtable, no `0x38/0x3c/0x40` fields — it looks like an unrelated container copy-ctor, called
only from `FUN_106a9e30`); and `FUN_10a5a680` has no `CGameMenu` fields at all, so it is not a
`GetCurrentPage`.

The activate/deactivate slots are `Display` and `Hide`. `CGameMenu::SwitchPage` on `FarCry2_server`
calls the old page's vtable `+0x10` and the new page's `+0xc`, and `CUIPageBase::Hide`
(`0x09129e00`) is referenced from vtables only — never by a direct call — matching pure
activate/deactivate dispatch. `Display` null-guards the bound `magma::Page` at `this+0x14`, so an
unbound page draws nothing rather than faulting at this level.

A near-complete `CUIPageBase` method table on `Dunia.dll`, from a name-matching pass and not each
independently decompiled: `Init` (`0x10109410`), `Shutdown` (`0x101088f0`), `Display`
(`0x10109490`), `Hide` (`0x101095c0`), `SetPage` (`0x101090d0`), `ConfigPage` (`0x10109f00`),
`RegisterModule`/`UnRegisterModule` (`0x102ffdb0`/`0x104fb660`), `AddListener`/`RemoveListener`
(`0x10720790`/`0x10503880`), `AddCommand`/`ExecuteCommands` (`0x10108ba0`/`0x10108b40`),
`OnActionSignal` (`0x10108990`), `Update` (`0x10108c10`), `GetLayer` (`0x10a962e0`), `Unload`
(`0x10108760`). `Display`'s and `Hide`'s vtable-slot data xrefs sit exactly 4 bytes apart in every
vtable that contains them (`0x10e1e2bc`/`0x10e1e2c0`, `0x10e25114`/`0x10e25118`,
`0x10eabe3c`/`0x10eabe40`), confirming they're adjacent slots.

Neither `0x10108e70` nor `0x10109010` is page-stack navigation. `0x10108e70` is
`CUIPageBase::GetTopLevel`, which reads a `"TOPLEVEL"` attribute off some document/config-node interface; `0x10109010` reads a
`"LAYER"` attribute the same way (and itself calls `GetLayer`, `0x10a962e0` — so it's some other
layer-related accessor, not `GetLayer` itself). The only real `PushPage` in this binary is
`magma::ActionPushPage` (`~ActionPushPage` dtor at `0x10ad6360`, confirmed via RTTI strings) — a
`.mgb`-file `Action` class, not a native `CUIPageBase` method; see
[how Magma actions execute](#how-magma-actions-execute).

### `CGameMenu`'s page hashtable — `Find`/`GetOrCreatePageSlot`/`InsertNode`

Since `AddPage<T>` is compile-time-only, a new C++ page class can't be registered through the normal
path, and inserting a hand-built page object directly into the hashtable under an invented
`CStringID` crashes. The three functions `GetPage`/`SetNextPage`/`Shutdown` all share underneath
them, fully decompiled (`Dunia.dll` addresses):

| Function | `Dunia.dll` address | Role |
|---|---|---|
| `CGameMenu_PageTable_Find` | `0x101f7a90` | The shared read-only lookup every one of `GetPage`/`SetNextPage`/`GetOrCreatePageSlot` calls into. Signature `(CGameMenu* this, void** outNode, uint32_t* key)`: hashes `*key` (an `ldiv`-based scramble, **not** CRC32), walks the bucket at `this+0x1c` indexed by `(hash & this+0x28)` with a wraparound correction against `this+0x2c` (element count), compares each node's own `[2]` field against `*key`, writes the hit node or the `this+0x10` miss-sentinel into `*outNode`. **Safe to call live** — against a real, live `CGameMenu*` with an absent key it completes cleanly, returning the correct miss sentinel. |
| `CGameMenu_GetOrCreatePageSlot` | `0x107813e0` | The get-or-create wrapper `AddPage<T>` itself calls. Runs `Find`; on a miss, calls `InsertNode` to insert a fresh node, then returns a pointer to the node's value slot (`&node[3]`) for the caller to write its own page pointer into. **Called live against a real `CGameMenu*`, it crashes.** |
| `CGameMenu_PageTable_InsertNode` | `0x10206020` | A full Dinkumware/MSVC-STL-style hashtable insert-with-rehash implementation (bucket growth, node splicing, `std::logic_error("list<T> too long")` on overflow via `_CxxThrowException`) — matches classic `stdext::hash_map`/`_Hash` internals almost line for line. **This is where the live crash happens.** |

**Confirmed node shape** (from `Find`'s own field accesses): each node is (at least) 4 `uint32`-sized
slots — `[0]`/`[1]` unresolved (list-linkage, `[1]` is read as the node's own "next" pointer during
insert), `[2]` = the stored key, `[3]` = the caller's own value (what `GetOrCreatePageSlot` hands back
a pointer to).

**The live crash**: calling `GetOrCreatePageSlot` from inside `CFCXOptionPage::Setup` on a real, live
`CGameMenu*` (obtained via `ownerPage+0x140`, see below) raises `STATUS_ACCESS_VIOLATION`
(`0xC0000005`) inside `InsertNode`. Wrapping every native call in SEH (`__try`/`__except`) catches it
cleanly, so the game keeps running either way.

`ownerPage+0x140` really is the owning `CGameMenu*` — confirmed twofold: (1) disassembly of
`CSetNextPageMenuHandler::SwitchPage` (`0x10188d00`, the real click-time entry point every button
uses) shows it reading `ownerPage+0x140` itself before calling `GetPage`/`SetNextPage`/`SwitchPage`;
(2) a live dump of the pointer's fields is structurally sane where it matters (`+0x2c` reads back as a
small integer, `7`, a plausible live element count).

**The crash mechanism, from the decompile**: `InsertNode`'s own pre-check block
(`if (count <= *(uint*)(this+0x14) >> 2)`) reads `this+0x14` **as an integer capacity** and, when it's
garbage-large (which it always is against a real `CGameMenu`, see below), always evaluates true — which
means the "maybe grow the bucket array" bookkeeping that follows always runs, including a line
(`index = (count - (mask >> 1)) - 1; node = nodeArray[index];`) that isn't gated by whether growth
was actually needed. With a mask value that isn't small, `index` wraps to something astronomically
large, and indexing `nodeArray[index]` is what segfaults.

**Why `this+0x14` is garbage as an integer**: it isn't one. `CGameMenu::Shutdown`'s own decompile
(`0x101d1b20`) reads the *identical* offset as a genuine pointer — `puVar1 = *(undefined4**)(this+0x14);`
— then walks it as a circular sentinel-node linked list (`for (node = *puVar1; node != puVar1; node =
*node)`). That's the standard Dinkumware/MSVC `_List_nod`-style sentinel pattern. `InsertNode`'s own
decompile treats the *same* field two more ways within its own body (`>>2` as a byte-length capacity,
then later `!= 0x1fffffff` as if it were an integer compared against Dinkumware's classic `max_size()`
sentinel) — three incompatible readings of one field, all within functions operating on the same struct.
This is very likely a Ghidra decompiler type-recovery failure on genuinely tricky hand-tuned STL pointer
arithmetic, not three actually-different real fields at the same offset — but which of the three (if
any) reflects the real compiled logic is unresolved.

**Debugging technique**: before a risky native call, a strictly *read-only* probe through the
identical code path (here: calling `Find` with a key that's known not to be present, expecting a clean
miss) rules out "the object isn't safely readable yet" (a timing/initialization-order theory)
independently of whatever the *next*, riskier call does. Every native-pointer touchpoint should be
wrapped in SEH (`__try`/`__except`) regardless.

Calling `GetOrCreatePageSlot`/`InsertNode` live against a real `CGameMenu` is not safe. A new page is
reached without the hashtable instead — see [a private page that works](#a-private-page-that-works).

## How a page binds to its Magma layout

:::info[Live-confirmed]
Traced on `FarCry2_server`, ported and verified by decompile on `Dunia.dll`, then **exercised live
in-game** by a private page that initialises and displays correctly.
:::

**A page class binds to its Magma layout by *name*, and that name resolves through the `.mgb`'s
`GenericObjectTable`.** A page ctor takes `(char const* pageName, wchar_t const* title)` —
`CFCXOptionGamePage` passes `"MAINMENU_OPTIONGAME_PAGE_PC"` — and `CUIPageBase::Init()` turns the
string into a live widget tree:

```
Id::Hash(pageName)                                   FUN_10aa7150 (the magma-side CRC32)
  -> GenericObjectServer::FindGenericObject          folded into 0x10108860
  -> FullLink::GetLastObject, IsKindOf(magma::Page)
  -> CUIPageBase::SetPage         0x101090d0         writes this+0x14
  -> ConfigPage (vtable +0x20), DoInit (vtable +0x14)
       -> CUIPageBase::FetchMagmaElements   0x10109150
  -> this+0x68 = 1
```

| Symbol | `Dunia.dll` | `FarCry2_server` |
|---|---|---|
| `CUIPageBase::Init` | `0x10109410` | `0x09129c30` |
| `CUIPageBase::FetchMagmaElements` | `0x10109150` | `0x0912a7f0` |
| `CUIPageBase::SetPage` | `0x101090d0` | `0x09129590` |
| `GenericObjectServer::FindGenericObject` (+ `GetLastObject`/`IsKindOf`) | `0x10108860` | `0x0a05aa50` |
| `CMagmaElementFactory::GetPage` (fallback path) | `0x10187700` | `0x09283040` |
| `CMenuPage::DoInit` / `CListMenuPage::DoInit` | `0x10cdb5a0` / `0x10cdbe20` | — / `0x0912d660` |
| `CMenuPage::SetTitle` | not found | `0x09131710` |

`magma::Engine::LoadPackage` (`FarCry2_server 0x0a03fc90`) registers each loaded package's
`GenericObjectTable` into the global `GenericObjectServer`, so **any** package can contribute names.
Confirmed in shipped data: `options.mgb`'s table maps `MAINMENU_OPTIONGAME_PAGE_PC` → its `Page`
area `C16854EF`, `MAINMENU_OPTION_NETWORK` → `400736ED`, and so on for every Options tab.

**Nothing in the engine calls `Init()` implicitly** — not `CGameMenu::AddPage<T>`, not `SwitchPage`.
A hand-built page therefore has no bound `magma::Page`, no row `ListBox` and no title `Text`. An
**empty** name string short-circuits `Init` harmlessly: no page, no crash, nothing drawn.

`CUIPageBase` field layout on `Dunia.dll`, read straight off `Init`'s decompile — the page-name
string is a plain MSVC `std::string`, not an opaque `CryStringBase`:

```
+0x08 / +0x0c / +0x10   row-list Element / magma::ListBox / title magma::TextBase
+0x14                   bound magma::Page*
+0x24                   CStringID of the page name (feeds the GetPage fallback only)
+0x2c                   page-name chars: inline while capacity < 0x10, else a heap pointer
+0x3c  size      +0x40  capacity
+0x68                   inited flag (byte)
```

A name of 15 characters or fewer lives in the object's own SSO buffer, so setting it is a `memcpy`
plus two integer writes — no allocation, no refcount emulation.

### What a `CListMenuPage`/`CSettingsPage` layout must contain

`FetchMagmaElements` looks these up by hardcoded name inside the bound page. Miss them and the page
renders empty rather than crashing: `AddButton` returns `-1` and does nothing when `+0xc` is null.

| Name | Found via | Stored at |
|---|---|---|
| `p_menu_nav` → `l_menu_nav_list` | `AreaInstance` → `ListBox` | `+0x8`, `+0xc` |
| `a_title_bar` → `t_page_title` | `AreaInstance` → `Text` | `+0x10` |

`CListMenuPage::AddButton` is then just `magma::ListBox::AddItem(this+0xc, label, 0)` plus a parallel
handler vector.

Each settings **row's value control** is a separate pre-authored widget, named by a `UserData`
property on the page's own Area — those are the two `char const*` arguments to
`AddBoolSetting`/`AddSliderSetting`/`AddValueListSetting<T>`. Dumped from the real `options.mgb`, the
Game page declares `SETTING_LABEL_LIST` (the shared label list) plus `SETTING_MOUSE_SMOOTH`,
`SETTING_INVERTYAXIS`, `SETTING_SENSITIVITY`, `SETTING_CROSSHAIR`, `SETTING_DIFFICULTY`,
`SETTING_SUBTITLE`, `SETTING_AMBX` and `SETTING_MACHETE`. **A settings page has a fixed, authored
number of setting slots** — eight here.

Every template those slots instantiate lives in `common.mgb` (`CRC32("common") = E5EC7051`;
`CRC32("options") = D035FA87`), which is always loaded: `36150990` = nav list + title bar,
`652FD37C` = one value-list cell, `62EA6603` = slider cell, `E58F0F6C` = navbar prompts. A new page
package therefore needs no materials, fonts or textures of its own.

Dump any of this from a real file with
`tools/JackAll/src/JackAll.Tools/Mgb/mgb_dump_generic_objects.py` and `mgb_dump_area.py`, which
resolve the stored name hashes by CRC32-ing every ASCII run in `Dunia.dll`.

### A private page that works

A second, private instance of a real compiled page class is reachable without ever touching the
hashtable, because **`CGameMenu::SwitchPage()` (`0x101d1990`) never touches the page hashtable at
all**. It only reads/writes two plain fields on the `CGameMenu` object itself (`+0x3c` = next page,
`+0x40` = current page) and calls two vtable slots (deactivate old at `(*old)+0xc`, activate new at
`(*new)+0x8`). The hashtable lookup only happens in the separate `SetNextPage` function, which normal
buttons reach via `CSetNextPageMenuHandler`.

The recipe, on `CFCXOptionGamePage`:

1. Construct a **private, heap-allocated instance** with the real ctor (`0x1081e9c0`, identified by
   the literal loc keys `"GAMEOPTION_TITLE"`/`"MAINMENU_OPTIONGAME_PAGE_PC"` in its body) on a
   zero-initialized `0x210`-byte buffer — the object size `AddPage<CFCXOptionGamePage>`
   (`0x107d7ab0`) allocates. The ctor's own visible field-writes never touch the `CListMenuPage` base
   fields `AddButton` needs (`+0xc`/`+0xd4`/`+0x168`/`+0x16c`). The object is never registered in
   `CGameMenu`'s hashtable, so the real, shared "Game" tab is untouched.
2. Overwrite its name string to point at a Magma page and call `Init()`.
3. From a hand-rolled `IMenuItemHandler`'s `Activate()`, read `ownerPage+0x140` fresh for the live
   `CGameMenu*` (the same field `CSetNextPageMenuHandler::SwitchPage` itself reads), write the private
   page's pointer directly into `CGameMenu+0x3c`, then call `CGameMenu::SwitchPage` directly — never
   calling `SetNextPage`.

Done against an already-shipped Magma page, the result in game is a genuinely separate screen, all
four bindings non-null, only the rows the page adds, and no exception across repeated entries.
`src/ui/fcse_page.cpp` is built on this. The recipe works for any real page class, not just
`CFCXOptionGamePage` — no hashtable-insert risk, no `Action`-dispatch RE needed.

Two behaviours that generalise to any page built this way:

- **The title comes from the page object, not the layout.** A page borrowing the Network layout
  displays `"Game options"` — the string `CFCXOptionGamePage`'s ctor stored — pushed into the shared
  `t_page_title` widget. A title cannot be baked into a custom `.mgb`, because that widget lives in
  `common.mgb`'s shared `a_title_bar`; it must be set on the page object.
- **Rows survive only if appended from inside the per-display rebuild.** `RefreshOptionList` clears
  the row list every time the page displays, so anything added at construction is wiped.

### What FCSE's page lists

- `tools/FCSE/src/ui/mods_tab.cpp` hooks `CFCXOptionPage::Setup` (`0x1081aee0`), calls through to
  the original first, then calls `FcsePage::Install` to build the separate page and its navigation
  button.
- `tools/FCSE/include/fcse_api.h` is the authority for the settings API — `FCSE_Setting` (name, an
  `FCSE_SettingValue` default carrying its own `FCSE_SettingType`, optional `onChanged` callback),
  `FCSE_RegisterSettingsFn`, `FCSE_API_VERSION`. FCSE owns each value and hands it to the plugin
  through the callback, which is what lets settings persist in `bin\fcse.ini` (`src/ini_file.cpp`,
  tested by `tests/ini_file_tests.cpp` and `tests/settings_registry_tests.cpp`).
- The page lists **every loaded plugin** (`PluginLoader::LoadedNames`), not just the ones that
  registered settings — a plugin with none still gets a row, marked `(no settings)`. Settings are
  matched to plugins by name, and since a plugin picks its own registration name and may not use
  its module name, any group matching no loaded plugin is appended in its own block rather than
  hidden.

**Row labels live-refresh after a click.** Because `RefreshOptionList` rebuilds every row from the
registry's current values, updating a label needs no new mechanism — a row's click handler re-enters
that same rebuild after flipping the value. The rebuild then happens from inside the engine's own
click dispatch, destroying the clicked row while the engine may still hold it; the handler objects
themselves are heap-allocated and never freed, so they survive it, and every native call in the path
is SEH-wrapped. This re-entry is not live-tested and is the most likely thing to misbehave in-game.

## Loading Magma resources (`.mgb`/`.mgb.desc`)

Fully decompiled on `FarCry2_server`, with the `Dunia.dll` addresses structurally confirmed by
decompile. The two cross-validate each other: one calls the other directly, matching the documented
"binary is always the last thing loaded" relationship.

- **`CMagmaConfigUIResource::LoadResourceInMagma()`** — `FarCry2_server` `0x096077a0`, `Dunia.dll`
  `0x10554a40`. Walks its own `<dependencies>` child array (`this+0x28` base/`this+0x2c` count),
  **recursing only into nested `CMagmaConfigUIResource` children first** (depth-first, confirmed on
  `Dunia.dll` via a direct self-recursive call), then as the final step calls
  `CMagmaUIResource::LoadPackageInMagma` on its own paired binary resource (`this+0x4c`) — so the
  `.mgb` binary is always the last thing loaded for a given `.desc`, as `mgb.md` states.
- **`CMagmaUIResource::LoadPackageInMagma(char const*)`** — `FarCry2_server` `0x0961ee70`, `Dunia.dll`
  `0x105f3960`. Cache-check (`this+0x44`), builds a `CFileNameNomad` from the resource's own stored
  path (`this+0x1c`, set at construction from the `.desc`'s `ID=` attribute) plus a `"UI\\"` prefix,
  then calls a **virtual** `LoadPackage` method on the global `CEngineNomad` singleton — `vtable+0x14`
  on `FarCry2_server`, **`vtable+0x8` on `Dunia.dll`** (same ABI/compiler-driven slot-numbering
  difference noted for `CGameMenu::SwitchPage` above) — which returns the `Package*`, and caches it.

### `magma::objecttypemanager` — `Dunia.dll` addresses

| Method | `Dunia.dll` address | Confirmed via |
|---|---|---|
| `Register(ObjectTypeInfo*)` | `0x10a982b0` | Decompile matches exactly: linear scan for an existing duplicate, append-and-increment-count if new, with one special-cased sentinel type (`&DAT_1165f4c4`). |
| `Initialize()` | `0x10a98ad0` | Decompile calls `FUN_10aa7150` — `magma::Id::Hash` — once per registered type, building a hash-sorted lookup table at `DAT_1165f4c0`. |
| `GetCount()` | `0x10a98290` | Trivial one-line accessor over `GetInternalRegisteredCount()` — matches. |
| `GetTypeIdFromId(...)` | `0x10a986a0` | Looks up a hash in the exact same `DAT_1165f4c0` table `Initialize` builds — matches. |
| `UnInitialize()` | `0x10a98a40` | Tears down the same `DAT_1165f4c0` table — matches. |

Hooking `Register` logs every `ObjectTypeInfo*`'s class name as it's registered, with no hash
computation involved at the registration call site itself; it yields 98 real class names.

### `.mgb` header/body parsing entry points — `Dunia.dll` addresses

| Function | `Dunia.dll` address | What it does |
|---|---|---|
| `BinaryLoadVisitor::ReadHeader` equivalent | `FUN_10ac7a30` | Checks the `"MAGMA"` magic, the `0xAB` sentinel byte, and the `0x1eab90` version, then walks the type table: reads each raw hash, calls `GetTypeIdFromId`, and stores the result byte unconditionally into the per-`this`-instance remap array at `this+0x34+slotIndex` — no branch on found-vs-not-found. |
| Its caller/wrapper | `FUN_10ac9180` | Opens the archive/file, `memset`s the 255-byte remap array (`this+0x34`) to `0`, calls `ReadHeader`, and on success calls into `FUN_10a99230` next. |
| Body/`VisitPackage` dispatch trampoline | `FUN_10a99230` | A thin, reused/folded thunk (fires from multiple unrelated call sites, so a single hit's target is not "the" body dispatcher): loads an arg, tail-jumps through `[[this+0x5c]]+8`. |

The rest come from a Ghidra Version Tracking correlation between `FarCry2_server` (real symbols) and
`Dunia.dll` — the most reliable way to bridge the two binaries — each then independently confirmed by
decompile:

| Function | `Dunia.dll` address | Confirmed via |
|---|---|---|
| `BinaryLoadVisitor::VisitArea` equivalent | `0x10AC9520` | Decompile 1:1 matches `FarCry2_server`'s `VisitArea` (`0xa05f4b0`): reads a raw type-id byte, indexes the remap array, calls `GetType`, calls the `MakeElement` equivalent, dereferences the result's vtable with **zero NULL check** — same crash-risk shape on both binaries. |
| `objecttypemanager::GetType(byte)` equivalent | `0x10AC9140` | 1:1 match to `FarCry2_server`'s `GetType` (`0xa075fa0`): `return TypeArray[index];`, no bounds check. On `FarCry2_server`, `GetType(0)` resolves to `BaseObject` (not `AnonymousType`). |
| `Factory::MakeElement` equivalent | `0x10ABF0E0` (from `VisitArea`'s children loop) / `0x10ABED20` (from `VisitPackage`'s own `areaCount` loop — a different call site, not necessarily a different function) | 1:1 match to `FarCry2_server`'s `MakeElement` (`0xa0481a0`): ancestor-walk against ~11 hardcoded `PTR_DAT_*` leaf-category globals, returns `0` if none match. |
| `VisitPackage`'s `areaCount` loop | inside `0x10ACA570` (`VisitPackage` itself), loop body at `0x10ACAE60`–`0x10ACAEAA` | The real top-level-`Area` constructor loop — distinct from `VisitArea`'s own *children* loop. |
| `VisitAreaLink` | `0x10AC9710` | Same `GetType`/`MakeElement` shape, raw byte held in `ECX` not `EAX` at the resolved-value read. |
| `VisitFullLink` | `0x10AC9EF0` (call to `GetType` at `0x10AC9F29`) | Same shape, `EAX`. |
| "Has global focus area?" / "has second area?" special slot | `0x10AC97C0` | Matches `mgb.md`'s documented bool-gated single-`Area` slots (separate from `areaCount`'s loop entirely). |
| `LoadMaterial` | `0x10ACB900` | Byte format in `mgb.md`. |

The reader/`BinaryLoadVisitor`-equivalent object is **pooled and reused** across `.mgb` loads (confirmed
live: `FUN_10ac9180`'s `memset` fires again, at the same heap address, for a later, different file's
load) — worth knowing before trying to track one via a fixed address across more than one load. A
hardware watchpoint on a *heap* address (the reader object's own fields) is fragile for exactly this
reason; a breakpoint on a *code* address (like the ones in the table above) is not, and is the better
tool once the real function is known.

### Two separate CRC-32 implementations — do not confuse them

Far Cry 2 has **two independent** CRC-32 implementations, both the same algorithm (CRC-32/ISO-HDLC:
poly `0xEDB88320` reflected, init `0xFFFFFFFF`, final complement — confirmed byte-for-byte identical to
Python's `zlib.crc32`), but **completely separate code and separate lookup tables**:

| | Native engine hash (`GetNameHash`/`CRC32_Hash`) | Magma widget-class hash (`magma::Id::Hash`) |
|---|---|---|
| `Dunia.dll` address | `CRC32_Hash` @ `0x10229400`, `GetNameHash` wrapper @ `0x10228380` | `0x10aa7150` (found live in a debugger by inspection, not derived statically) |
| `FarCry2_server` address | — | `0xa0782a0` |
| Lookup table | Shared, precomputed constant `DAT_10f95388` | **Own separate table**, lazily generated on first call into `DAT_1165ff80` |
| Used for | Native C++ class/page navigation hashes (e.g. `CRC32("CFCXOptionPage")` = `0x977107FF`, used by `CGameMenu`-style page lookup) | The `.mgb` type-table class hashes (`RectShape`, `CheckBox`, `Page`, etc. — [see `mgb.md`](../file-formats/mgb.md)) |
| Confirmed via | A live hook logging ~1.7M real calls over a full session | Live capture (both an FCSE hook and, more successfully, a live IDA debugger session with an IDC script) confirming real class names like `CActionSignalBase`, `StretchableWindowSection` |

`GetNameHash`'s real signature: `void __thiscall GetNameHash(uint* outSlot, char* str, bool
useAltHashFn)` — if `useAltHashFn` is true it calls a different function (`FUN_10229440`, not
investigated) instead of `CRC32_Hash`.

**`FUN_10aa7150`'s signature**: `void __cdecl(unsigned int* outHash, const char* str)` — confirmed via
raw disassembly (`MOV EDX,[ESP+8]` for the string, plain `RET`, no stack-cleanup immediate = genuine
`__cdecl`, not `__thiscall`/`__fastcall`).

### The `.mgb` byte format against the shipped menu files

[`mgb.md`](../file-formats/mgb.md) has the full documented spec. Checked byte-for-byte against four
shipped menu files (`common.mgb`, `common_mp.mgb`, `options.mgb`, `sp_menus.mgb`):

- **The entire header + 166-entry type table (bytes `0`–`0x2A6`) is byte-for-byte identical across all
  four files** (confirmed via `md5sum` of the first 679 bytes of each) — it's a fixed, engine-wide
  constant for a given build, not per-file content. An `.mgb` writer can copy this prefix verbatim
  rather than reconstructing the type table.
- `PAGESIZE`/`DISPLAYOFFSET`/materials/`VisitUserData` all decode correctly via JackAll's
  `MgbReader`/`MgbBody` parser (`tools/JackAll/src/JackAll.Tools/Mgb/`), matching real,
  cross-checkable content (e.g. `sp_menus.mgb`'s materials decode to real texture paths matching its
  own `.desc` sidecar exactly).

## How Magma actions execute

Full write-up on [Interop with the Dunia engine](../magma-ui/engine-interop.md).
`magma::ActionServer` is a name-hash → factory table filled at startup by two functions only:
`magma::ActionServer::RegisterStandardActions` (`0x09fdb500` / `Dunia.dll 0x10ab8000`, the six
magma built-ins — `Stop`, `Continue`, `GotoFrameIndex`, `GotoKeyframe`, `PushPage`, `PopPage`) and
`CMagmaActionDispatcher::RegisterCustomActions` (`0x095f47f0` / `Dunia.dll 0x105031b0`, **81**
`RegisterAction` calls, each a literal string plus a `CreateObject` pointer). Each game action is a
`CActionSignal<&_magmaactiondispatcher_X>` or `CInputAction<&_magmaactiondispatcher_X>`
instantiation; firing runs `CActionSignalBase::Execute` (`0x095f9630`), and the receiving side is
the ~40 game classes implementing `OnActionSignal` (`CFCXBaseOptionPage`, `CBazaarComputerUI`,
`CLoadOutUI`, `CFCXMainHudUI`, …). The registry has **no data path into it**: a new action name
cannot be added from a `.mgb`.

## Open questions

1. **What string hashes to `0x86F001E3`?** The class's name is unidentified, but nothing depends on
   it: `0x86F001E3` **never appears as a live type byte** in any shipped file — it exists only as a
   type-table entry, alongside ~35 other unresolved hashes per file, and the body's type bytes only
   ever resolve to the small closed sets the three `Factory` dispatchers accept (see
   [`mgb.md`](../file-formats/mgb.md#validation) and [its unknowns](../file-formats/mgb.md#unknowns)).
   It is not among ~900 manually-tried candidates, a ~12,000-candidate sweep of every mangled
   `magma::`-namespaced symbol in `FarCry2_server`, ~1.7M live `CRC32_Hash` calls, 2580+ live
   `magma::Id::Hash` inputs, or the 98 class names a `Register` hook logs. A live capture of all
   four body-side consumption points, with the debugger attached before process start and the pause
   menu opened, never sees the type-id byte this hash needs (`3`, per `mgb.md`'s off-by-one formula)
   — consistent with no file using it.
2. **Why does hooking `magma::Id::Hash` (`0x10aa7150`) crash unless the detour takes almost no action?**
   A pure no-op passthrough is safe; a version that only *compares* the hash and takes action (file
   I/O, even a `MessageBoxA`) **only on an exact, rare match** is safe and runs full sessions with zero
   crashes; but *any* version that does unconditional per-call work — file I/O, a CRT-free hand-rolled
   memory buffer, even just logging the first ~30 calls — crashes deterministically (same crash point
   every time, ruling out a timing race). A `CRITICAL_SECTION` around all file I/O makes zero
   difference, ruling out unsynchronized concurrent access too. The mechanism is unknown; acting only
   on a rare match is the empirically safe workaround.
3. **What else determines the Options tab-selector's rows?** Whether there is a real child-page array
   analogous to `CUIPageBase::Display`'s `this+0x40`/`+0x44` iteration found on the server, and
   whether anything besides `CFCXOptionPage::Setup`'s flat `AddButton` sequence contributes rows to
   the tab selector specifically.
4. **`CGameMenu_PageTable_InsertNode`'s real use of `this+0x14`.** Which of its three readings of the
   field reflects the compiled logic. Nothing a private page does depends on it.
5. **How a raised Magma action signal reaches one particular listener.** `CUIPageBase::RegisterModule`
   /`AddListener` are the likely mechanism, not followed.
