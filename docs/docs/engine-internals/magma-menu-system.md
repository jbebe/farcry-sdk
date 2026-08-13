---
sidebar_position: 11
---

# The Menu System — `CGameMenu`, Magma Pages, and Building a Mod Configuration Menu

:::info[Verified via reverse engineering]
Traced live via GhidraMCP across two binaries: `Dunia.dll` (Steam v1.03, stripped function names but
retained RTTI/class-name strings) and `FarCry2_server` (the unstripped Linux dedicated-server build,
which links the same portable menu/UI code with full real symbols — see
[Overview](./overview.md)). Where a fact is confirmed on one binary only, that's stated explicitly.
Everything under "Confirmed facts" was checked against live, running code (decompiles, disassembly,
or an actual working/shipped feature) — not inference alone.
:::

:::caution[Superseded 2026-08-08 — this page records how the problem was solved, not the shipping code]
FCSE no longer borrows the stock Game tab. It now authors its **own** Magma package, feeds it to the
engine through a hooked file reader, and binds a private page to it by name — so the "shared page,
tell the two visits apart with a flag" design this page describes at length is gone, along with the
files it names.

**Every `tools/FCSE/src/` path below is historical.** `mod_page.{h,cpp}`, `menu_handler.{h,cpp}`,
`page_spike.{h,cpp}` and `PLAN-own-page.md` no longer exist. What ships now is:

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

The engine facts below — `CGameMenu`'s page table, `CUIPageBase::Init`, the `IMenuItemHandler`
shape, the `AddButton`/`RefreshOptionList` behaviour — all still hold and are what the current
implementation is built on. What is stale is which FCSE file does what, and the conclusion that a
private page was unreachable.
:::

For the data side of the same problem — authoring the `.mgb` a page binds to — see
[Magma UI](../magma-ui/index.md), in particular
[binding a page to native code](../magma-ui/patterns.md#binding-a-page-to-native-code).

This page exists because FCSE (`tools/FCSE`) needed a way to let plugins expose simple config UI
in-game, and building that required reverse-engineering a good chunk of Far Cry 2's native menu
system (distinct from the `.mgb`/Magma *binary format* itself, which [its own page](../file-formats/mgb.md)
already covers). Two dead/superseded approaches were tried along the way (a hand-rolled page inserted
into `CGameMenu`'s hashtable, which crashed; intercepting `.mgb` loading, never started) before
landing on what's actually implemented now: a genuinely separate page, built by privately
constructing a second instance of a real compiled page class and reaching it without touching
`CGameMenu`'s hashtable at all. This page collects everything confirmed along the way - see
"Building a real, separate 'Mods' page" below for the three paths (A/B/C) in order, and "What FCSE
actually shipped" for the current implementation.

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

## Confirmed facts

### Building a row of buttons (the mechanism FCSE's shipped feature uses)

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
| `CSetNextPageMenuHandler::SwitchPage` (the real click-time page switch) | not found | `0x0912ec60` |

**`CListMenuPage::AddButton`** real signature (confirmed via `FarCry2_server`'s mangled symbol
`_ZN13CListMenuPage9AddButtonEPKwbP16IMenuItemHandler`):
`void* __thiscall AddButton(CListMenuPage* this, wchar_t const* label, bool visible, IMenuItemHandler* handler)`.

**Critical, empirically confirmed constraint**: `AddButton` is only safe to call with a `this` whose
real object layout matches what it reads/writes (`+0xc` null-check guard, `+0xd4`, `+0x168`/`+0x16c`
row array). It is safe when called with `BuildMainMenu`'s own menu-list object, or with
`CFCXOptionPage::Setup`'s own `this` (exactly what that function itself does, 5 times, every time
Options opens) — **and it crashes the game** if called with `CFCXOptionPage`'s own top-level pointer
instead (a different, incompatible class layout). This was hit and diagnosed live this session before
landing on the correct target.

**`0x1084fa90` is *not* `CFCXOptionPage::Setup`**, despite superficially similar code (it also calls a
chain of get-or-create tab-descriptor getters). It fires eagerly, before intro videos even play, and
hooking it to call `AddButton` crashes the game. Confirmed via live crash-testing; a decompiler comment
is already left on this address in the shared Ghidra project warning against reuse. The *real*
`Setup()` (`0x1081aee0`) is invoked only via a data/vtable xref (never a direct `CALL` anywhere in the
binary) — i.e. genuine lazy virtual dispatch, fired only when Options is actually opened. This is
confirmed by the "Mods" row-append log line appearing exactly once per session, only once Options is
opened, not at boot.

### Building the click-handler yourself (`IMenuItemHandler`)

Confirmed via `FarCry2_server`'s mangled symbols: `IMenuItemHandler` is a tiny interface —
`Activate(unsigned int)`, `ActivateParent(unsigned int)`, plus a virtual destructor. **Not** part of
the `CMenuPage` class hierarchy above.

FCSE does **not** reuse the engine's own `CSetNextPageMenuHandler` for its click handling — it builds
its own hand-rolled object instead (`tools/FCSE/src/menu_handler.cpp`, `ModsMenuHandler`): a plain
struct whose first member is a `void**` vtable pointer, with a small hand-built vtable array (one real
slot pointing at our handler function, the rest safe no-ops). **`kActivateSlot = 1` was correct on the
first empirical try** — confirmed live, no iteration needed. This was never derived from a real,
confirmed Windows/MSVC vtable layout (no tool to read raw vtable data was available this session) —
it's an empirically-verified guess, documented as such in `menu_handler.cpp`.

### Page switching — `CGameMenu`

Fully decompiled on `FarCry2_server`. **All `Dunia.dll` addresses for `GetPage`/`SetNextPage`/
`SwitchPage`/the ctor+dtor/`Shutdown` are now found and structurally confirmed** — started from an
unverified name→address candidate list (produced by some external structural-matching pass, not
100% trusted going in — two of its `CGameMenu` candidates turned out to be wrong, see below), then
closed the one real gap (`SetNextPage`) by tracing `GetPage`'s callers directly.

| Method | `FarCry2_server` address | `Dunia.dll` address | What it does |
|---|---|---|---|
| `CGameMenu::GetPage(CStringID const&)` | `0x0912b860` | `0x101d1b90` | Pure hashtable **lookup** — returns 0 if not found. Does **not** create on demand. Confirmed: `Dunia.dll` version calls a lookup helper then compares against the same end-sentinel field (`this+0x14`) `Shutdown` (below) also uses, returning `*(node+0xc)` on a hit or 0 otherwise. |
| `CGameMenu::SetNextPage(CStringID const&)` | `0x0912b940` | `0x101d1bc0` | The candidate-list guess (`FUN_1071ab20`) was **wrong** (an unrelated `CFCXScoreboardService` singleton accessor, rejected). Found instead by tracing `GetPage`'s own callers: runs the identical hashtable-lookup helper `GetPage` uses (`FUN_101f7a90`, same `+0x14` sentinel check), and stores the hit into `this+0x3c` — precisely the field `SwitchPage` reads as "next page". Confirmed. |
| `CGameMenu::SwitchPage()` | `0x0912b5e0` | `0x101d1990` | The real transition. Confirmed structurally: old page (`this+0x40`) gets a vtable call first, then new page (`this+0x3c`) gets its owning-`CGameMenu` backpointer set (`+0x20`) and a vtable call, then current/next pointers are swapped. **Vtable slot numbers differ from the Linux server build** — here it's slot `+0xc` on the *old* page (deactivate) and slot `+0x8` on the *new* page (activate), vs. `+0x10`/`+0xc` on `FarCry2_server`. Expected: different compilers (MSVC vs. GCC/Itanium ABI) place virtuals at different indices, not a contradiction. |
| `CGameMenu::CGameMenu()` (ctor) | — | `0x101d1d70` | Confirmed via matching field layout: zeroes the same `+0x38`/`+0x3c`/`+0x40` fields `SwitchPage`/`Shutdown` operate on, then allocates and registers a small self-registration helper object stored at `+0x34` (torn down by the dtor). The candidate list's second ctor guess (`FUN_1011cec0`) is **wrong** — different vtable, different field layout (no `0x38/0x3c/0x40`), looks like an unrelated container copy-ctor; only called from one unrelated site (`FUN_106a9e30`). Rejected. |
| `CGameMenu::~CGameMenu()` (dtor) | — | `0x101d1ce0` | Confirmed: same vtable pointer as the ctor sets, tears down the `+0x34` helper object. |
| `CGameMenu::Shutdown()` | — | `0x101d1b20` | Confirmed, and a strong cross-check: walks the page hashtable (same `+0x14` sentinel `GetPage` uses) and calls `FUN_10108990`... actually `FUN_101088f0` on every entry — which is *exactly* the candidate list's own separate mapping for `CUIPageBase::Shutdown`. Two independent candidate-list entries corroborate each other here. |
| `CSetNextPageMenuHandler::SwitchPage()` | `0x0912ec60` | `0x10188d00` | What a real button's click ultimately calls: `GetPage` + `SetNextPage` + `CGameMenu::SwitchPage` (in that exact call order), plus a `"default_ui_transition"` sound/effect trigger. Found by tracing `GetPage`'s callers, not from the original candidate list — a bonus find; the doc previously had this marked "not found" on `Dunia.dll`. |
| `CGameMenu::AddPage<T>()` (one compiled instantiation per class `T`) | e.g. `0x0897be50` for `<CFCXOptionPage>` | — | **Compile-time template** — get-or-create a page instance in `CGameMenu`'s own hashtable. Only works for classes the game itself was compiled with; there is no generic runtime "create by name" factory. **Field offsets corrected 2026-08-03** (this row previously guessed `this+0x10`/`+0x14`=bucket base/end, `this+0x1c`=count - wrong, superseded by the rigorous decompile-based mapping in "`CGameMenu`'s page hashtable" below: `+0x10`=miss sentinel, `+0x14`=internal list sentinel *pointer* (not a bucket bound), `+0x1c`=node array base, `+0x28`=bucket mask, `+0x2c`=element count). |
| `CGameMenu::GetCurrentPage()` | not in this table before | rejected | The candidate list also offered `FUN_10a5a680` for a never-before-documented `GetCurrentPage`. Decompiled and **rejected** — completely different field layout (no `CGameMenu` fields at all), almost certainly a false positive from name-only fuzzy matching. |

**Also newly found this session (candidate list, not yet independently decompiled/verified beyond
name-plausibility): a near-complete `CUIPageBase` method table on `Dunia.dll`** — `Init`
(`0x10109410`), `Shutdown` (`0x10108990`... `0x101088f0`, see cross-check above), `Display`
(`0x10109490`), `Hide` (`0x101095c0`), ~~`PushPage`/`PopPage` (`0x10108e70`/`0x10109010`)~~, `SetPage`
(`0x101090d0`), `ConfigPage` (`0x10109f00`), `RegisterModule`/`UnRegisterModule`
(`0x102ffdb0`/`0x104fb660`), `AddListener`/`RemoveListener` (`0x10720790`/`0x10503880`),
`AddCommand`/`ExecuteCommands` (`0x10108ba0`/`0x10108b40`), `OnActionSignal` (`0x10108990`),
`Update` (`0x10108c10`), `GetLayer` (`0x10a962e0`), `Unload` (`0x10108760`). Cross-checked one data
point: `Display`'s and `Hide`'s vtable-slot data xrefs sit exactly 4 bytes apart in every vtable that
contains them (`0x10e1e2bc`/`0x10e1e2c0`, `0x10e25114`/`0x10e25118`, `0x10eabe3c`/`0x10eabe40`),
confirming they're adjacent slots — but the absolute slot offset from vtable base (needed to know
whether `Display`/`Hide` *are* `SwitchPage`'s activate/deactivate slots, or something else entirely)
wasn't pinned down. Not chased further this session.

**Correction, 2026-08-04**: the `PushPage`/`PopPage` guess above (`0x10108e70`/`0x10109010`) is
**wrong** — this was an unverified name-plausibility match from the original candidate list, and
decompiling both addresses this session shows neither is page-stack navigation.
`CUIPageBase::GetTopLevel` (`0x10108e70`, real name recovered via an independent `string_vote`
import pass, not this session's own work) reads a `"TOPLEVEL"` attribute off some
document/config-node interface; `0x10109010` reads a `"LAYER"` attribute the same way (and itself
calls the already-documented `GetLayer`, `0x10a962e0` — so it's some other layer-related accessor,
not `GetLayer` itself). The only real `PushPage` in this binary is `magma::ActionPushPage`
(`~ActionPushPage` dtor at `0x10ad6360`, confirmed via RTTI strings) — a `.mgb`-file `Action` class,
not a native `CUIPageBase` method. Reaching it means reverse-engineering `Action` *execution* (Open
question 7 below), not a simple vtable call. If page-stack push/pop semantics are needed in a future
session, this pair of addresses is not the way in — start over from `CUIPageBase`'s real vtable
instead of trusting the candidate list here.

**Implication for building a genuinely new page**: since `AddPage<T>` is compile-time-only, a truly
new C++ page class can't be registered through the normal path. But `CGameMenu`'s hashtable is just a
plain data structure — nothing stops inserting a hand-built object directly (the same trick already
used successfully for `ModsMenuHandler`) under an invented `CStringID`, *if* that object's vtable slots
`+0xc`/`+0x10` do something sane when called. This was identified as viable and **attempted live**
2026-08-02/2026-08-03 (`tools/FCSE/src/mod_page.cpp`) — see the next section for what that found.

### `CGameMenu`'s page hashtable — `Find`/`GetOrCreatePageSlot`/`InsertNode` (2026-08-02/03)

The three functions `GetPage`/`SetNextPage`/`Shutdown` all share underneath them, fully decompiled and
now given real names in the shared Ghidra project (`Dunia.dll` addresses):

| Function | `Dunia.dll` address | Role |
|---|---|---|
| `CGameMenu_PageTable_Find` | `0x101f7a90` | The shared read-only lookup every one of `GetPage`/`SetNextPage`/`GetOrCreatePageSlot` calls into (previously referenced only as `FUN_101f7a90`). Signature `(CGameMenu* this, void** outNode, uint32_t* key)`: hashes `*key` (an `ldiv`-based scramble, **not** CRC32), walks the bucket at `this+0x1c` indexed by `(hash & this+0x28)` with a wraparound correction against `this+0x2c` (element count), compares each node's own `[2]` field against `*key`, writes the hit node or the `this+0x10` miss-sentinel into `*outNode`. **Confirmed safe to call live** — used as a diagnostic probe against a real, live `CGameMenu*` (see below) and completed cleanly, returning the correct miss sentinel. |
| `CGameMenu_GetOrCreatePageSlot` | `0x107813e0` | The get-or-create wrapper `AddPage<T>` itself calls. Runs `Find`; on a miss, calls `InsertNode` to insert a fresh node, then returns a pointer to the node's value slot (`&node[3]`) for the caller to write its own page pointer into. **This is the function real tab descriptors' compile-time `AddPage<T>` calls rely on — an earlier pass of this doc called it "already confirmed working," which was wrong; it was never actually called live until 2026-08-02, and it crashes.** |
| `CGameMenu_PageTable_InsertNode` | `0x10206020` | A full Dinkumware/MSVC-STL-style hashtable insert-with-rehash implementation (bucket growth, node splicing, `std::logic_error("list<T> too long")` on overflow via `_CxxThrowException`) — matches classic `stdext::hash_map`/`_Hash` internals almost line for line. **This is where the live crash happens.** |

**Confirmed node shape** (from `Find`'s own field accesses): each node is (at least) 4 `uint32`-sized
slots — `[0]`/`[1]` unresolved (list-linkage, `[1]` is read as the node's own "next" pointer during
insert), `[2]` = the stored key, `[3]` = the caller's own value (what `GetOrCreatePageSlot` hands back
a pointer to).

**Live crash, reproduced twice** (2026-08-02 and 2026-08-03, `tools/FCSE/src/mod_page.cpp`, hooked from
inside `CFCXOptionPage::Setup`, same hook point `mods_tab.cpp`'s already-shipped feature uses): calling
`GetOrCreatePageSlot` on a real, live `CGameMenu*` (obtained via `ownerPage+0x140`, see below) raises
`STATUS_ACCESS_VIOLATION` (`0xC0000005`) inside `InsertNode`, caught cleanly by wrapping every native
call in SEH (`__try`/`__except`) so the game keeps running either way. This was actually first hit
2026-07-31 (a fact that had only ever been recorded as a Ghidra decompiler comment on these three
addresses, not written to this doc until now) — meaning it was independently re-discovered on
2026-08-02 before that context was found.

**What's independently confirmed correct** (so not worth re-litigating): `ownerPage+0x140` really is
the owning `CGameMenu*` — confirmed twofold: (1) disassembly of `CSetNextPageMenuHandler::SwitchPage`
(`0x10188d00`, the real click-time entry point every button uses) shows it reading `ownerPage+0x140`
itself before calling `GetPage`/`SetNextPage`/`SwitchPage`; (2) a live dump of the candidate pointer's
fields looked structurally sane where it mattered (`+0x2c` read back as a small integer, `7`, a
plausible live element count).

**The concrete crash mechanism, worked out from the decompile**: `InsertNode`'s own pre-check block
(`if (count <= *(uint*)(this+0x14) >> 2)`) reads `this+0x14` **as an integer capacity** and, when it's
garbage-large (which it always is against a real `CGameMenu`, see below), always evaluates true — which
means the "maybe grow the bucket array" bookkeeping that follows always runs, including a line
(`index = (count - (mask >> 1)) - 1; node = nodeArray[index];`) that isn't gated by whether growth
was actually needed. With a mask value that isn't small, `index` wraps to something astronomically
large, and indexing `nodeArray[index]` is what segfaults.

**Why `this+0x14` is garbage as an integer**: it isn't one. `CGameMenu::Shutdown`'s own decompile
(`0x101d1b20`, already independently cross-validated in the table above) reads the *identical* offset
as a genuine pointer — `puVar1 = *(undefined4**)(this+0x14);` — then walks it as a circular
sentinel-node linked list (`for (node = *puVar1; node != puVar1; node = *node)`). That's the standard
Dinkumware/MSVC `_List_nod`-style sentinel pattern, and it's independently confirmed (this function was
already cross-validated via a separate `CUIPageBase::Shutdown` candidate-list match). `InsertNode`'s own
decompile treats the *same* field two more ways within its own body (`>>2` as a byte-length capacity,
then later `!= 0x1fffffff` as if it were an integer compared against Dinkumware's classic `max_size()`
sentinel) — three incompatible readings of one field, all within functions operating on the same struct.
This is very likely a Ghidra decompiler type-recovery failure on genuinely tricky hand-tuned STL pointer
arithmetic, not three actually-different real fields at the same offset — but which of the three (if
any) reflects the real compiled logic wasn't resolved this session.

**Debugging technique worth reusing**: before attempting any risky native call live, add a strictly
*read-only* probe through the identical code path first (here: calling `Find` with a key that's known
not to be present, expecting a clean miss) and log whether it completes at all. A clean pass rules out
"the object isn't safely readable yet" (a timing/initialization-order theory) independently of whatever
the *next*, riskier call does — cheap, safe, and was what let this session narrow the crash from
"somewhere in this whole chain" down to one specific ~15-line block using one specific ambiguous field,
without ever needing another blind crash-and-diagnose cycle. Every native-pointer touchpoint should be
wrapped in SEH (`__try`/`__except`) regardless — this is what let both live attempts keep the game
running afterward instead of hard-crashing.

**Status**: unresolved. Calling `GetOrCreatePageSlot`/`InsertNode` live against a real `CGameMenu` is
not currently safe. See "If you want to build a real, separate Mods page" below for the resulting
strategic reassessment (pivoting toward Magma-side interception instead of fighting this further).

### Loading Magma resources (`.mgb`/`.mgb.desc`)

Fully decompiled on `FarCry2_server` this session (`mgb.md` only had these as addresses/summary before
now). **`Dunia.dll` addresses found and structurally confirmed this session** too, from the same
candidate list referenced above — both held up on decompile, and cross-validate each other (one calls
the other directly, matching the documented "binary is always the last thing loaded" relationship).

- **`CMagmaConfigUIResource::LoadResourceInMagma()`** — `FarCry2_server` `0x096077a0`, `Dunia.dll`
  `0x10554a40`. Walks its own `<dependencies>` child array (`this+0x28` base/`this+0x2c` count),
  **recursing only into nested `CMagmaConfigUIResource` children first** (depth-first, confirmed on
  `Dunia.dll` via a direct self-recursive call), then as the final step calls
  `CMagmaUIResource::LoadPackageInMagma` on its own paired binary resource (`this+0x4c`) — confirms
  `mgb.md`'s existing claim that the `.mgb` binary is always the last thing loaded for a given `.desc`.
- **`CMagmaUIResource::LoadPackageInMagma(char const*)`** — `FarCry2_server` `0x0961ee70`, `Dunia.dll`
  `0x105f3960`. Cache-check (`this+0x44`), builds a `CFileNameNomad` from the resource's own stored
  path (`this+0x1c`, set at construction from the `.desc`'s `ID=` attribute) plus a `"UI\\"` prefix,
  then calls a **virtual** `LoadPackage` method on the global `CEngineNomad` singleton — `vtable+0x14`
  on `FarCry2_server`, **`vtable+0x8` on `Dunia.dll`** (same ABI/compiler-driven slot-numbering
  difference noted for `CGameMenu::SwitchPage` above) — which returns the `Package*`, and caches it.

### `magma::objecttypemanager` — `Dunia.dll` addresses found

Not previously covered in this doc at all (only referenced speculatively in Open question 1 below, as
an untried debugging angle). The candidate list included this whole family; decompiling disambiguated
two internally-inconsistent duplicate guesses:

| Method | `Dunia.dll` address | Confirmed via |
|---|---|---|
| `Register(ObjectTypeInfo*)` | `0x10a982b0` | Decompile matches exactly: linear scan for an existing duplicate, append-and-increment-count if new, with one special-cased sentinel type (`&DAT_1165f4c4`). |
| `Initialize()` | `0x10a98ad0` | Decompile calls `FUN_10aa7150` — the already-confirmed `magma::Id::Hash` — once per registered type, building a hash-sorted lookup table at `DAT_1165f4c0`. The candidate list also offered this same address for `Register` and vice versa; the decompile is what disambiguates them. |
| `GetCount()` | `0x10a98290` | Trivial one-line accessor over `GetInternalRegisteredCount()` — matches. |
| `GetTypeIdFromId(...)` | `0x10a986a0` | Looks up a hash in the exact same `DAT_1165f4c0` table `Initialize` builds — matches. |
| `UnInitialize()` | `0x10a98a40` | Tears down the same `DAT_1165f4c0` table — matches. |

**This unblocks Open question 1(b) below**: hooking `Register` (address now known) to log every
`ObjectTypeInfo*`'s class name as it's registered — no hash computation involved at the registration
call site itself — was floated last session as a way to catch the still-unidentified `0x86F001E3`
class without depending on `Id::Hash` ever being called for it. **Attempted 2026-08-02, see Open
question 1's update and [`mgb.md`](../file-formats/mgb.md)** — resolved 98 real class names but not
this one; the hunt moved to `GetTypeIdFromId` and the header/body parser instead.

### `.mgb` header/body parsing entry points — `Dunia.dll` addresses found (2026-08-02)

Found while live-tracing a `0x86F001E3` lookup failure end-to-end (see Open question 1's update). Not
previously documented on either binary.

| Function | `Dunia.dll` address | What it does |
|---|---|---|
| `BinaryLoadVisitor::ReadHeader` equivalent | `FUN_10ac7a30` | Checks the `"MAGMA"` magic, the `0xAB` sentinel byte, and the `0x1eab90` version, then walks the type table: reads each raw hash, calls `GetTypeIdFromId`, and stores the result byte unconditionally into the per-`this`-instance remap array at `this+0x34+slotIndex` — no branch on found-vs-not-found. |
| Its caller/wrapper | `FUN_10ac9180` | Opens the archive/file, `memset`s the 255-byte remap array (`this+0x34`) to `0`, calls `ReadHeader`, and on success calls into `FUN_10a99230` next. |
| Body/`VisitPackage` dispatch trampoline | `FUN_10a99230` | A thin, reused/folded thunk (fires from multiple unrelated call sites — do not trust a single hit's target as "the" body dispatcher, learned the hard way this session): loads an arg, tail-jumps through `[[this+0x5c]]+8`. |

**Follow-up (2026-08-02), resolved via Ghidra Version Tracking** — rather than chasing virtual dispatch
targets live, the user ran a Version Tracking correlation between `FarCry2_server` (real symbols) and
`Dunia.dll`, and renamed the matched functions directly in the shared Ghidra project. This is by far the
most reliable way to bridge the two binaries and is now confirmed to work well — all of the following
were found this way, then independently confirmed by decompile:

| Function | `Dunia.dll` address | Confirmed via |
|---|---|---|
| `BinaryLoadVisitor::VisitArea` equivalent | `0x10AC9520` | Decompile 1:1 matches `FarCry2_server`'s `VisitArea` (`0xa05f4b0`): reads a raw type-id byte, indexes the remap array, calls `GetType`, calls the `MakeElement` equivalent, dereferences the result's vtable with **zero NULL check** — same crash-risk shape on both binaries. |
| `objecttypemanager::GetType(byte)` equivalent | `0x10AC9140` | 1:1 match to `FarCry2_server`'s `GetType` (`0xa075fa0`): `return TypeArray[index];`, no bounds check. |
| `Factory::MakeElement` equivalent | `0x10ABF0E0` (from `VisitArea`'s children loop) / `0x10ABED20` (from `VisitPackage`'s own `areaCount` loop — a different call site, not necessarily a different function) | 1:1 match to `FarCry2_server`'s `MakeElement` (`0xa0481a0`): ancestor-walk against ~11 hardcoded `PTR_DAT_*` leaf-category globals, returns `0` if none match. |
| `VisitPackage`'s `areaCount` loop | inside `0x10ACA570` (`VisitPackage` itself), loop body at `0x10ACAE60`–`0x10ACAEAA` | The real top-level-`Area` constructor loop — distinct from `VisitArea`'s own *children* loop; conflating the two wastes a lot of live-debugging time (see `mgb.md`'s `0x86F001E3` write-up). |
| `VisitAreaLink` | `0x10AC9710` | Same `GetType`/`MakeElement` shape, raw byte held in `ECX` not `EAX` at the resolved-value read. |
| `VisitFullLink` | `0x10AC9EF0` (call to `GetType` at `0x10AC9F29`) | Same shape, `EAX`. |
| "Has global focus area?" / "has second area?" special slot | `0x10AC97C0` | Matches `mgb.md`'s documented bool-gated single-`Area` slots (separate from `areaCount`'s loop entirely) — a call site not previously identified on either binary. |
| `LoadMaterial` | `0x10ACB900` | Resolved its full byte format this session — see `mgb.md`. |

The reader/`BinaryLoadVisitor`-equivalent object is **pooled and reused** across `.mgb` loads (confirmed
live: `FUN_10ac9180`'s `memset` fires again, at the same heap address, for a later, different file's
load) — worth knowing before trying to track one via a fixed address across more than one load. A
hardware watchpoint on a *heap* address (the reader object's own fields) is fragile for exactly this
reason; a breakpoint on a *code* address (like the ones in the table above) is not, and is the better
tool once the real function is known.

### Two separate CRC-32 implementations — do not confuse them

This was the single biggest source of wasted effort this session. Far Cry 2 has **two independent**
CRC-32 implementations, both the same algorithm (CRC-32/ISO-HDLC: poly `0xEDB88320` reflected, init
`0xFFFFFFFF`, final complement — confirmed byte-for-byte identical to Python's `zlib.crc32`), but
**completely separate code and separate lookup tables**:

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

### The `.mgb` byte format — hands-on validated this session

[`mgb.md`](../file-formats/mgb.md) already has the full documented spec. This session additionally
confirmed, byte-for-byte, against the four real sample files in `tmp/menu/` (`common.mgb`,
`common_mp.mgb`, `options.mgb`, `sp_menus.mgb`):

- **The entire header + 166-entry type table (bytes `0`–`0x2A6`) is byte-for-byte identical across all
  four files** (confirmed via `md5sum` of the first 679 bytes of each) — it's a fixed, engine-wide
  constant for a given build, not per-file content. A future `.mgb` writer can copy this prefix
  verbatim rather than reconstructing the type table.
- `PAGESIZE`/`DISPLAYOFFSET`/materials/`VisitUserData` all decode correctly via JackAll's existing
  `MgbReader`/`MgbBody` parser (`tools/JackAll/src/JackAll.Tools/Mgb/`), matching real,
  cross-checkable content (e.g. `sp_menus.mgb`'s materials decode to real texture paths matching its
  own `.desc` sidecar exactly).
- All four sample files still hit the known `0x86F001E3`-unresolved-class wall a few areas in (area
  index 2–3) — this is **not** a blocker for authoring a *new* file using only already-documented
  classes (`Page`/`Text`/`CheckBox`/`RectShape`/etc.), only for fully decoding these particular shipped
  files.

## How a page binds to its Magma layout — the missing link (2026-08-07)

:::info[Live-confirmed]
Traced on `FarCry2_server`, ported and verified by decompile on `Dunia.dll`, then **exercised live
in-game** by a private page that initialises and displays correctly.
:::

Everything further down this page that describes a hand-built page as missing "some unidentified
piece of state" is superseded. There was exactly one missing piece, and it was a call, not a field.

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
A hand-built page therefore has no bound `magma::Page`, no row `ListBox` and no title `Text`, which
is what killed every earlier attempt at one. An **empty** name string short-circuits `Init`
harmlessly: no page, no crash, nothing drawn.

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

### Confirmed live: a private page that works

The spike that established this (`src/page_spike.{h,cpp}`, since removed — its result is what
`src/ui/fcse_page.cpp` was built from) constructs a private `CFCXOptionGamePage`, overwrites its name string
to point at an already-shipped Magma page, calls `Init()`, and reaches it by writing
`CGameMenu+0x3c` and calling `SwitchPage`. In-game result: a genuinely separate screen, all four
bindings non-null, FCSE's own rows and nothing else, no exception across repeated entries.

Two behaviours that generalise to any page built this way:

- **The title comes from the page object, not the layout.** The spike borrowed the Network layout and
  displayed `"Game options"` — the string `CFCXOptionGamePage`'s ctor stored — pushed into the shared
  `t_page_title` widget. A title cannot be baked into a custom `.mgb`, because that widget lives in
  `common.mgb`'s shared `a_title_bar`; it must be set on the page object.
- **Rows survive only if appended from inside the per-display rebuild.** `RefreshOptionList` clears
  the row list every time the page displays, so anything added at construction is wiped — observed
  directly on the first spike run, and the same trap the shipped feature hit in 2026-08.

## What FCSE actually shipped

**Status as of 2026-08-04: the real, shared `CFCXOptionGamePage`, reached two ways and gated by a
flag** — see "Path C" below for the privately-constructed variant that was written, crashed and
abandoned, and the 2026-08-07 section above for why it crashed and what the working version of that
same idea looks like. Earlier sessions shipped a simpler fallback first (plain toggle-button rows
appended directly to the Options category-button screen, no separate page at all); that code is
gone. The rows themselves still use the same underlying primitive:

- `tools/FCSE/include/fcse_api.h` — `FCSE_Setting` (name, `FCSE_SettingValue` default carrying its
  own `FCSE_SettingType`, optional `onChanged` callback), `FCSE_RegisterSettingsFn`,
  `FCSE_API_VERSION` bumped 2→3 at the time (it has moved on since; the header is the authority).
  This replaced a `bool*`-based `FCSE_ConfigBool`/`FCSE_RegisterConfigPageFn` pair (API v2): FCSE
  now owns the value and hands it to the plugin through the callback, which is what lets settings
  persist — the old shape only knew *where* a plugin's bool lived, never what to call it in a file.
- `tools/FCSE/src/api/settings_registry.cpp`/`.h` (was `mods_registry`) — registry of
  `(pluginName, FCSE_Setting[])` groups backed by `bin\fcse.ini` (`src/ini_file.cpp`, tested by
  `tests/ini_file_tests.cpp` and `tests/settings_registry_tests.cpp`).
- The page lists **every loaded plugin** (`PluginLoader::LoadedNames`), not just the ones that
  registered settings — a plugin with none still gets a row, marked `(no settings)`. Settings are
  matched to plugins by name, and since a plugin picks its own registration name and may not use
  its module name, any group matching no loaded plugin is appended in its own block rather than
  hidden.
- `tools/FCSE/src/menu_handler.cpp`/`.h` — `ModsMenuHandler`, the hand-built `IMenuItemHandler` used
  for each row's click (toggles the backing `bool`, fires `onChanged`). Now
  `MenuItemHandler<Payload>` in `src/ui/menu_item_handler.h`, one mechanism over three payloads.
- `tools/FCSE/src/ui/mods_tab.cpp`/`.h` — hooks `CFCXOptionPage::Setup` (`0x1081aee0`), calls through to
  the original first, then calls `ModPage::Install` (below) to build the separate page and its
  navigation button. Still current, but it calls `FcsePage::Install`.
- `tools/FCSE/src/mod_page.cpp`/`.h` — Path C's implementation, see below. Superseded by the
  `src/ui/fcse_page` + `page_rows` + `page_slots` + `page_vtable` set.

**Row labels live-refresh after a click** (fixed; this page previously recorded the stale
`[ON]`/`[OFF]` label as a known cosmetic gap). Because `RefreshOptionList` rebuilds every row from
the registry's current values, updating a label needs no new mechanism — a row's click handler
re-enters that same rebuild (`ModPage::RefreshRows`) after flipping the value. The cost is that the
rebuild now happens from inside the engine's own click dispatch, destroying the clicked row while
the engine may still hold it; the handler objects themselves are heap-allocated and never freed, so
they survive it, and every native call in the path is SEH-wrapped. **Not yet live-tested** — this
re-entry is the most likely thing to misbehave in-game.

## Building a real, separate "Mods" page

This was the actual goal from the start. Three paths were explored across several sessions — A and
B are both dead/superseded, kept here for the record; **C is what's actually implemented**.

### Path A: hand-rolled `CGameMenu` page — dead, do not re-attempt

1. ~~Find `Dunia.dll` addresses for `CGameMenu::GetPage`/`SetNextPage`/`SwitchPage`.~~ **Done.**
   `GetPage` (`0x101d1b90`), `SetNextPage` (`0x101d1bc0`), `SwitchPage` (`0x101d1990`), the ctor/dtor,
   `Shutdown`, and `CSetNextPageMenuHandler::SwitchPage` (`0x10188d00`, the real click-time entry
   point) are all found and structurally confirmed (see the table above).
2. ~~Build a hand-rolled "page" object and insert it into `CGameMenu`'s hashtable.~~ **Attempted,
   currently blocked.** `tools/FCSE/src/mod_page.{h,cpp}` implements exactly this (vtable-pointer-first
   struct, `+0x8`=activate/`+0xc`=deactivate, SEH-wrapped around every native touchpoint) and is wired
   up live in `mods_tab.cpp`. The insert step (`GetOrCreatePageSlot`/`InsertNode`) crashes against a
   real, live `CGameMenu*` — see "`CGameMenu`'s page hashtable" above for the full mechanism and status.
   Everything *else* in this step (the vtable slot numbers, the invented `CStringID` via native
   `CRC32_Hash`, the hashtable being a plain insertable data structure) is confirmed correct; only the
   insert call itself is blocked.
3. **Point a real `CSetNextPageMenuHandler`** (built via the confirmed ctor at `0x10188ea0`) at that
   invented `CStringID`, wired to a new "Mod Configuration Menu" row appended the same way the shipped
   feature already does. Not reachable yet since step 2 blocks first.
4. **The open question this doesn't answer**: what backs the new page's actual *visuals*, even once step
   2 is unblocked. Two options, neither attempted: (a) have the hand-rolled page's "activate" slot just
   call the same `AddButton`-based row-building the shipped feature already does (correct `CGameMenu`
   bookkeeping, but no visually distinct new screen); or (b) a real, separate Magma `Page*` — see Path B.

### Path B: intercept `.mgb` loading instead — scoped out 2026-08-03, superseded, never started

Scoped out as a way to sidestep Path A's blocker by working inside the data-driven Magma system
instead of fighting `CGameMenu`'s native C++ internals: intercept `CMagmaUIResource::LoadPackageInMagma`
(or a lower-level file-read call - never found) for the Options screen's own `.mgb` resource, and hand
back a modified/extended version instead of the original bytes. Needed, none of it ever started: a
real `.mgb` *writer* (`tools/JackAll` was read-only at the time; a writer exists now, see
[`mgb.md`](../file-formats/mgb.md)), finding the file-load interception point, and — the single
biggest blocker — reverse-engineering `Action` **execution** (not just its file format; everything
`mgb.md`'s `ActionExecuter`/`Action` section covers is how actions are *serialized*, nothing about
how a live click *dispatches and runs* one). **Superseded by Path C** (2026-08-04), which reaches a
real, separate page using zero `.mgb`/`Action` knowledge at all - this path is not being pursued
further, but the `.mgb` writer it would have needed exists anyway for unrelated JackAll-editing
reasons (see [[mgb_parsing_progress]] in memory).

### Path C: a private, second instance of a real compiled page class — implemented 2026-08-04

The idea that actually worked, evolving out of an earlier "skip `CGameMenu` entirely, hook an
*existing* leaf page's own `Setup()`" suggestion. Two findings this session made it concrete:

1. **`CGameMenu::AddPage<T>()` is a per-class get-or-create, not a one-shot constructor.** Found
   `AddPage<CFCXOptionGamePage>` at `Dunia.dll 0x107d7ab0` (via the `"CFCXOptionGamePage"` RTTI
   string → its one non-getter xref → decompile) and its real ctor at `0x1081e9c0` (confirmed
   beyond doubt via literal loc keys `"GAMEOPTION_TITLE"`/`"MAINMENU_OPTIONGAME_PAGE_PC"` in its
   body). Calling either of these again, later, doesn't require the page to not already exist.
2. **`CGameMenu::SwitchPage()` (`0x101d1990`) never touches the page hashtable at all.** Fully
   decompiled: it only reads/writes two plain fields on the `CGameMenu` object itself (`+0x3c` =
   next page, `+0x40` = current page) and calls two vtable slots (deactivate old at `(*old)+0xc`,
   activate new at `(*new)+0x8`). The hashtable lookup that crashes in Path A
   (`CGameMenu_PageTable_Find`/`InsertNode`) only happens in the separate `SetNextPage` function,
   which normal buttons reach via `CSetNextPageMenuHandler`. **`SwitchPage` itself was never the
   blocker** — only inserting a *new key* into the hashtable was, and Path C never needs to.

**What's implemented** (`tools/FCSE/src/mod_page.{h,cpp}`): construct a **second, private,
heap-allocated instance** of `CFCXOptionGamePage` (the real ctor, `0x1081e9c0`, called on a
zero-initialized `new unsigned char[0x210]` — `0x210` is the object size confirmed from
`AddPage<T>`'s own allocation, and the buffer is zero-initialized because the ctor's own visible
field-writes never touch `CListMenuPage`'s base-class fields that `AddButton` needs, `+0xc`/`+0xd4`/
`+0x168`/`+0x16c` — reasoned to default correctly from zero, not independently confirmed). This
private object is **never registered in `CGameMenu`'s hashtable** — the real, shared "Game" tab is
completely untouched. FCSE's content (a header row, one disabled row per plugin name, one toggle row
per registered setting) is appended onto the private copy via the already-proven-safe `AddButton`
(safe on any `CListMenuPage`-derived `this` for the same base-class-layout reason it's safe on
`CFCXOptionPage`'s own `this`). The Options screen's navigation button uses a new hand-rolled
`IMenuItemHandler` (`PagePushHandler`, same fake-vtable/`kActivateSlot=1` technique as
`ModsMenuHandler`) whose `Activate()`: reads `ownerPage+0x140` fresh for the live `CGameMenu*` (the
same field `CSetNextPageMenuHandler::SwitchPage` itself reads), writes the private page's pointer
directly into `CGameMenu+0x3c`, then calls `CGameMenu::SwitchPage` directly - never calling
`SetNextPage`, never touching the hashtable.

**Not yet live-tested.** Two things to check first if it misbehaves: whether the zero-initialized
`CListMenuPage` fields really are sufficient (see above), and whether `CGameMenu+0x40` ("current
page") reliably holds a sane value at click time for `SwitchPage`'s deactivate-old step - expected
yes (the engine's own normal navigation to Options should already maintain it), but this exact
call path (triggering `SwitchPage` directly rather than via `CSetNextPageMenuHandler`) is new.

**Reusable beyond this specific page**: this "construct a private instance of any compiled page
class, append content via `AddButton`, reach it by writing `CGameMenu+0x3c` and calling
`SwitchPage` directly" recipe works for any real page class, not just `CFCXOptionGamePage` - no
hashtable-insert risk, no `Action`-dispatch RE needed.

## Open questions — the real gaps

1. **What string hashes to `0x86F001E3`?** Still unknown, despite: ~900 manually-tried candidates,
   a ~12,000-candidate automated sweep of every mangled `magma::`-namespaced symbol in
   `FarCry2_server`, a live FCSE hook logging ~1.7M `CRC32_Hash` calls, and a live IDA debugger capture
   of 2580+ real `magma::Id::Hash` inputs (which *did* newly resolve `CActionSignalBase` and
   `StretchableWindowSection`, but not this one). Leading theories, none confirmed: (a) computed too
   early for any hook installed after process start to observe (possibly during `Dunia.dll`'s own
   static initializers, before even a from-the-start IDA capture could attach — this contradicts the
   IDA capture apparently working for other classes, so it's not a clean explanation); (b) a
   hardcoded/precomputed hash that never passes through a runtime `Id::Hash(name)` call at all,
   registered some other way (a real, live line of investigation floated but not yet executed: break
   on `magma::objecttypemanager::Register` instead — it takes the `ObjectTypeInfo*` directly, so the
   class name is readable regardless of whether/how a hash gets computed at that call site); (c) a
   stale hash from a class that no longer exists in this build, left over in shipped content from an
   earlier engine version.

   **Update (2026-08-02) — extensive live + static investigation, still unresolved but much narrower.**
   Full write-up lives in [`mgb.md`](../file-formats/mgb.md#unknowns), this is the summary. Theory (b)'s
   `Register` hook was finally run live (98 real class names captured, no match). Static analysis on
   `FarCry2_server` then proved the class **must be real and currently registered** — `GetType(0)`
   resolves to `BaseObject` (not `AnonymousType`), and the real element-construction loops dereference
   `Factory::MakeElement`'s result with zero NULL check, so an unresolved type at real construction time
   would crash the shipped game every time, and it doesn't — which weakens theory (c) (stale/removed)
   considerably. But a comprehensive live capture covering all four real body-side consumption points
   (confirmed via Ghidra Version Tracking — see the address table above) across a full session, with the
   debugger attached before process start and the in-game pause menu opened (both explicitly verified,
   ruling out the two most obvious "we just didn't observe the right window" explanations), never once
   saw the specific type-id byte value this hash needs (`3`, per `mgb.md`'s off-by-one formula). A
   from-scratch static re-implementation of the format got close to settling it independently but hit an
   undiagnosed field-layout bug in `VisitArea`'s own byte layout before it could search a real file
   end-to-end. Net: still open, theory (b) (early-bootstrap registration, or a body path not yet covered)
   is the leading explanation, and the concrete next step is finishing that static parser rather than more
   live debugging — see `mgb.md` for exactly where it stopped.

   **Update (2026-08-07) — the static parser is finished, and this question is no longer blocking
   anything.** The `.mgb` format is now fully decoded and the reimplementation parses the whole 50-file
   corpus byte-exactly (see [`mgb.md`](../file-formats/mgb.md#validation)). That settles the part of this
   question that mattered: `0x86F001E3` **never appears as a live type byte** in any shipped file — it
   exists only as a type-table entry, alongside ~35 other unresolved hashes per file. The reason no live
   capture ever saw type-id byte `3` is simply that no file ever uses it. The class's *name* is still
   unidentified, but nothing depends on it: the body's type bytes only ever resolve to the small closed
   sets the three `Factory` dispatchers accept.
2. **Why does hooking `magma::Id::Hash` (`0x10aa7150`) crash unless the detour takes almost no action?**
   A pure no-op passthrough is safe; a version that only *compares* the hash and takes action (file
   I/O, even a `MessageBoxA`) **only on an exact, rare match** is safe and ran full sessions with zero
   crashes; but *any* version that did unconditional per-call work — file I/O, a CRT-free hand-rolled
   memory buffer, even just logging the first ~30 calls — crashed deterministically (same crash point
   every time, ruling out a timing race). A `CRITICAL_SECTION` around all file I/O made zero difference,
   ruling out unsynchronized concurrent access too. The real mechanism was never found. The empirically-
   safe workaround (act only on a rare match) works, but doesn't explain *why*.
3. **`Dunia.dll` addresses — resolved this session.** `CGameMenu::GetPage`/`SetNextPage`/`SwitchPage`,
   `CSetNextPageMenuHandler::SwitchPage`, `CMagmaUIResource::LoadPackageInMagma`,
   `CMagmaConfigUIResource::LoadResourceInMagma`, and `magma::objecttypemanager::Register`/
   `Initialize` are all now found and structurally confirmed (see the tables above). What's left:
   the absolute vtable-slot-to-named-method mapping for `CUIPageBase` on `Dunia.dll` (i.e., which real
   method — `Display`? `Hide`? something else? — actually lives at the `+0x8`/`+0xc` slots
   `SwitchPage` calls) wasn't pinned down. **Resolved 2026-08-07: they are `Display` (activate) and
   `Hide` (deactivate).** `CGameMenu::SwitchPage` on `FarCry2_server` (`0x0912b5e0`) calls the old
   page's vtable `+0x10` and the new page's `+0xc`, and `CUIPageBase::Hide` (`0x09129e00`) is
   referenced from vtables only — never by a direct call — matching pure activate/deactivate
   dispatch. `Display` null-guards the bound `magma::Page` at `this+0x14`, so an unbound page draws
   nothing rather than faulting at this level.
4. **`IMenuItemHandler`'s real vtable slot layout on Windows/MSVC is unconfirmed.** `ModsMenuHandler`'s
   `kActivateSlot = 1` works empirically (confirmed live, first try) but was never derived from an
   actually-read vtable — no tool was available this session to read raw vtable data out of
   `Dunia.dll`. If a future session gets debugger or memory-dump access to a real
   `CSetNextPageMenuHandler` instance's vtable, confirming this properly (and finding `ActivateParent`'s
   slot too) would remove the last "confirmed by luck" piece of the shipped feature.
5. **What actually determines the Options tab-selector's on-screen row order/membership at a lower
   level** (is there a real child-page array analogous to `CUIPageBase::Display`'s `this+0x40`/`+0x44`
   iteration found on the server, and does `CFCXOptionPage::Setup`'s flat `AddButton` sequence fully
   explain it, or is something else also involved)? Not needed for the shipped feature, but relevant if
   a future page needs to insert itself *into* that existing row rather than appending after it.
   **Partly answered 2026-08-07**: a page's row list is a real `magma::ListBox` (`l_menu_nav_list`,
   bound at `this+0xc`) and `AddButton` is a plain `ListBox::AddItem` on it — so ordering is just
   insertion order into that one widget, and inserting *into* the middle means going at the `ListBox`
   directly rather than through `AddButton`. What still isn't resolved is whether anything besides
   `Setup`'s flat `AddButton` sequence contributes rows to the tab selector specifically.
6. **`CGameMenu_PageTable_InsertNode`'s real field-usage for `this+0x14` — added 2026-08-03, no
   longer blocking anything (added 2026-08-04).** Originally the single blocker on Path A. Confirmed
   to be a genuine pointer (a linked-list sentinel, via `CGameMenu::Shutdown`'s own decompile) but
   `InsertNode`'s own decompile treats it as an integer twice, in two different ways, within the same
   function — almost certainly a Ghidra type-recovery failure on hand-tuned Dinkumware/MSVC STL
   pointer arithmetic rather than a real inconsistency in the compiled code, but which (if either)
   reading is correct was never resolved. **Path C (above) sidesteps this entirely** - it never calls
   `InsertNode`/`GetOrCreatePageSlot` at all, so this is now a pure curiosity rather than a blocker.
   Still open if anyone wants it for its own sake: a live memory watch on a *real* insert during
   normal boot (e.g. one of the five real Options category tabs registering itself via the
   compiled-in `AddPage<T>`) to observe confirmed-correct values for this field in a genuinely
   working call, rather than inferring from decompiled pseudocode alone.
7. **How does `Action` *execution* work at runtime? — added 2026-08-03, no longer blocking anything
   (added 2026-08-04).** Everything currently known about the `ActionExecuter`/`Action` family (see
   [`mgb.md`](../file-formats/mgb.md)) is about how actions are *serialized in the file* - nothing
   has been reverse-engineered yet about how a live button click actually dispatches and runs one,
   which would have blocked Path B's "hooks replaced to call FCSE
   functions" goal. A possibly-real shortcut, not yet verified: console/script-callback registration
   machinery (`CDominoConsoleCommandManager::RegisterConsoleCommand`-style templates) was spotted in
   the binary while investigating something unrelated - worth checking whether an existing native
   `Action` type can invoke a named, registerable callback before assuming the full dispatch mechanism
   needs reversing from scratch.

   **Update (2026-08-08) — mostly answered; full write-up on
   [Interop with the Dunia engine](../magma-ui/engine-interop.md).** `magma::ActionServer` is a
   name-hash → factory table filled at startup by two functions only:
   `magma::ActionServer::RegisterStandardActions` (`0x09fdb500` / `Dunia.dll 0x10ab8000`, the six
   magma built-ins — `Stop`, `Continue`, `GotoFrameIndex`, `GotoKeyframe`, `PushPage`, `PopPage`) and
   `CMagmaActionDispatcher::RegisterCustomActions` (`0x095f47f0` / `Dunia.dll 0x105031b0`, **81**
   `RegisterAction` calls, each a literal string plus a `CreateObject` pointer). Each game action is a
   `CActionSignal<&_magmaactiondispatcher_X>` or `CInputAction<&_magmaactiondispatcher_X>`
   instantiation; firing runs `CActionSignalBase::Execute` (`0x095f9630`), and the receiving side is
   the ~40 game classes implementing `OnActionSignal` (`CFCXBaseOptionPage`, `CBazaarComputerUI`,
   `CLoadOutUI`, `CFCXMainHudUI`, …). The registry has **no data path into it**, which settles Path
   B's "hooks replaced to call FCSE functions" idea: a new action name cannot be added from a `.mgb`.
   Still untraced is the *routing* step — how a raised signal reaches one particular listener
   (`CUIPageBase::RegisterModule`/`AddListener` are the likely mechanism, not followed).
