---
sidebar_position: 12
---

# `CFCXOptionGamePage` — the ABI FCSE's settings page is built on

:::info[Verified via reverse engineering]
Every address, offset and call shape below was confirmed by decompile against `Dunia.dll`
(Steam v1.03) and then exercised in a running game. Addresses are quoted as RVAs on
**`fc2_103_uplay`** only — the two shipped v1.03 builds place these differently, and FCSE resolves
them through its address library by symbol id rather than baking them in. See
[Overview](./overview.md) for how claims are marked.
:::

This is the reference behind `tools/FCSE/src/ui/engine_page_abi.h`, which carries the constants
themselves. The header stays a list of numbers; the reasoning for each lives here.

For how the page is registered, bound to its Magma package and reached from the Options screen,
see [The Menu System](./magma-menu-system.md).

## The class to construct

FCSE constructs `CFCXOptionGamePage` — the concrete leaf — at `kGamePageCtor` (`0x1081e9c0`),
allocating `0x210` bytes for it.

**Not the tidier-looking `CFCXBaseOptionPage` one class below it** (`0x1087ec80`). That base was
tried on 2026-08-08 and is **abstract**: constructing it works and the page displays, but `Back`
kills the process with `R6025 "pure virtual function call"`. Its content-build slot at
`vtable+0x3c` being a plain `RET` says nothing about the rest of the table — do not read that as
"concrete" again.

`CUIPageBase::Init` (`0x10109410`) turns the page's authored name into a bound `magma::Page` and
its widgets. Nothing in the engine calls it implicitly, which is why every earlier hand-built page
displayed nothing and then faulted.

## The private vtable, and why it is a copy

`CFCXOptionGamePage`'s primary vtable is at `0x10ead9d8` and has exactly **26 slots** — that is
measured, not assumed: `0x10eada40` is not a 27th entry, it is the `"MAINMENU_OPTIONGAME_PAGE_PC"`
string the constructor pushes.

FCSE takes a private copy and replaces five slots:

| Slot | Offset | Stock address | What it does |
| --- | --- | --- | --- |
| 2 | `+0x08` | `0x108211c0` | `Display` — `RefreshOptionList(); this+0x200 = 0; base::Display();` |
| 4 | `+0x10` | `0x10820100` | `Update(float)` — base update, then a switch on `this+0x200` |
| 19 | `+0x4c` | `0x10820150` | `OnSettingChanged` → `FUN_1081f6c0` |
| 20 | `+0x50` | `0x108200d0` | apply → `CFCXOptionGamePage::ApplyOptionsFromSettings` (`0x1081fd10`) |
| 21 | `+0x54` | `0x108200e0` | refresh → `CFCXOptionGamePage::UpdateSettingsFromOptions` (`0x1081f800`) |

**Five is exact.** Everything about this class specific to the Game tab hangs off these slots, and
nothing reaches it any other way. Scanning the class's whole translation unit for instructions
forming the address of anything in the button-id block found 24 of them, in exactly five functions
— `RefreshOptionList`, `FUN_1081f4f0`, `FUN_1081f6c0`, and the apply/refresh pair — each reachable
only through one of the slots above. An earlier version of FCSE replaced only three, and the two it
missed were found the way such things always are: the game crashed.

### Why the apply/refresh pair mattered

The page stores ten **button ids** at `+0x1d8..+0x1fc`, one per Game-tab option. Both functions do:

```c
setting = m_settings[id];                     // the map at page+0x190
if (setting && !IsKindOf(setting, Expected))
    setting = nullptr;                        // the guard nulls it...
value = setting->vtable[14](setting);         // ...and the call dereferences it anyway
```

On the stock Game tab the ids always resolve to a setting of the expected type, so the shipped bug
never fires. FCSE's page cleared the native rows and appended its own, which were handed the same
button ids back with the wrong types — a bool value-list where `SETTING_SENSITIVITY` expects a
slider — so the type check failed and the null was dereferenced. Resetting the ids to `-1` is not a
fix either: `std::map::operator[]` inserts a null for a missing key and the same dereference
follows.

Owning a copy of the table fixes all of it at once and is strictly better than hooking:
`RefreshOptionList` never runs on FCSE's page, so the ids stay `-1` forever, no native settings are
built and destroyed per display, and the three observer objects `RefreshOptionList` allocates and
registers on every display are never created. The stock Game tab is untouched **by construction**,
because it still points at the engine's table — and since nothing in the process is patched, FCSE
does not compete with another mod hooking these functions.

The two chain targets FCSE's replacements call into:

- `kBaseOptionPageDisplay` (`0x1087ed50`) — the rest of the stock `Display`, minus the
  `RefreshOptionList` call that opens it.
- `kBaseUpdate` (`0x10108c10`) — the base class's per-frame tick, the only part of that slot a page
  like FCSE's still wants.

`this+0x200` is cleared by the stock `Display` on every display; FCSE mirrors that so its page
behaves identically.

## Building rows

`CSettingsPage::ClearSettings` (`0x10cddf20`, vtable `+0x40`) is the engine's own "drop my rows and
delete their settings". The stock `RefreshOptionList` calls it before building anything; FCSE does
the same.

Three row builders, all read off `CFCXOptionGamePage::RefreshOptionList`:

```c
AddBoolSetting(this, label, labelListParam, settingParam, yesText, noText, enabled, handler)
AddValueListSetting(this, label, labelListParam, settingParam, count, itemLabels,
                    itemValues, enabled, handler)
AddSliderSetting(this, label, labelListParam, settingParam, min, max, enabled, handler)
```

- **`AddBoolSetting`** (`0x10cde0d0`) — a label plus a YES/NO control, which is what makes FCSE's
  rows look like stock ones instead of a caption with an `[ON]`/`[OFF]` suffix glued on. Its body
  forwards `label` and `handler` straight to `CListMenuPage::AddButton`, so the label is a plain
  `wchar_t*` — the localised strings the stock page passes are incidental, not a required type. It
  then binds the value widget with
  `CUISettingBase::FetchMagmaElements(page->m_magmaPage, labelListParam, settingParam)`, the lookup
  that resolves `FCSE_SLOT_nn` against the page's own UserData.
- **`AddValueListSetting<unsigned>`** (`0x1081d660`) — the N-option form, what a Choice renders as
  (the Game tab's Difficulty and Machete rows are this). No new layout is needed: `common.mgb`
  `#652FD37C`, the cell `FCSE_SLOT_nn` already links, is a ListBox with `BUTTONCOUNT=1` — a
  one-item viewport that scrolls through however many items were added. Two items is a YES/NO
  toggle, four is a dropdown; same widget either way. `itemLabels` is an array of plain `wchar_t*`,
  not of engine string objects — the stock caller fills it from `Oasis::GetLocalizedString`, which
  returns exactly that — so FCSE's own strings work as long as they outlive the row.
- **`AddSliderSetting`** (`0x10cddff0`) — a label plus a draggable slider, bound through the second
  slot bank (`FCSE_SLIDER_nn` → `common.mgb` `#62EA6603`) rather than the value cell. The value is
  an `int` in both directions: `CSliderSetting::SetValue` (`0x10cde270`) forwards to
  `magma::Slider::SetValue`, which takes an int and converts to float itself, and `GetValue`
  (`0x10cde240`) reads the widget's float back, adds `0.5` and truncates. So the same vtable slots
  13/14 the other settings use work here unchanged.

### Reading a row's value back

On the object `AddBoolSetting` returns (vtable `PTR_FUN_10eb1f38`), slots 13 (`+0x34`, SetValue)
and 14 (`+0x38`, GetValue) both take and return a **pointer** to the value. `SetValue`
(`0x10864250`) scans the value array `AddBoolSetting` filled with `{true, false}` and selects the
matching row in the bound YES/NO list. That the getter also returns a pointer is not a guess — the
object's own slot 9 (`0x10cde2f0`) is literally `SetValue(GetValue())`.

## Slot cells and visibility

`magma::UserData::GetUserDataElement(const std::string& name, Element*& out)` (`0x10a963a0`)
resolves one of the page area's `FullLink` properties to the element it names — exactly how the
engine finds `FCSE_SLOT_nn` itself, via `CUISettingBase::FetchMagmaElements`. FCSE resolves all 40
cell elements up front so the unused ones can be hidden; without it a cell is only reachable by
binding a setting to it, which is the one thing you do not want to do to a row that is not using
it.

`magma::Element::SetVisible(bool)` (`0x10ab13f0`) is bit 0 of the flags byte at `element+0x34`, and
the same call the dispatcher makes for `ShowElementNomad` / `HideElementNomad`. Safe in this
direction only: the cells are authored visible, so their sub-areas exist and only the draw flag
moves. Going the other way — authored `HIDDEN`, revealed by code — is bit 1 and **does not work**.

The UserData property naming the row list is `SETTING_LABEL_LIST`, and there are 20 row slots. Both
must match what `fcse.mgb` declares — see `tools/FCSE/assets/README.md`.

## Text rows and the EditBox

`magma::EditBox::SetText(const std::wstring&, bool)` (`0x10ab0220`) is the EditBox's **own** setter,
not `TextBase`'s. An EditBox derives from `Widget`, so `magma::TextBase::SetText` writes through the
wrong layout: that was tried, and it corrupted the widget badly enough that the CRT faulted and then
magma's draw pass did.

The engine clamps to the layout's `maxLength` itself (a `u16` at `widget+0x18`), and the trailing
bool copies the value into the committed string beside the displayed one — which is what a caller
seeding a field wants, so FCSE passes `true`. It ends by marking the text dirty, which is what
actually makes the field re-render; writing the string in memory would not have.

`magma::Page::SetSelected(int controller, Focusable* element)` (`0x10aa5180`) moves input focus to
an element — what the dispatcher does for the `SetFocusNomad` action. It is needed because an
EditBox authored beside the row list has no `NEIGHBORS`, so nothing routes focus into it on its own.
FCSE passes controller **255**, the "any controller" value the layout's own `DEFAULT_ELEMENT` uses;
the engine's own call site passes the real main-controller id (from `0x104fe5a0`) instead, which is
the next thing to try if 255 ever stops taking.

`magma::TextBase::SetText` (`0x1007d770`) takes a **raw** `wchar_t*`, so no string object has to be
forged for a plain label.

## The dirty flag

`CFCXBaseOptionPage` keeps a "the player changed something and has not applied it" flag at
`+0x1b8`. `SetDirty` (`0x1087eb50`) sets it when a row changes; `ApplyIfDirty` (`0x1087eb10`) calls
vtable `+0x50` and clears it; the `Back` path reads it and puts up the "you have unsaved changes"
prompt.

That prompt is right for a stock options page, which batches edits until Apply — and wrong for
FCSE's, which writes `fcse.ini` on the change itself. So FCSE clears the flag rather than answering
the question: there is genuinely nothing pending.

## MSVC string layouts

This build lays `std::string` out as:

```c
struct { void* proxy; char sso[16]; uint32_t size; uint32_t capacity; };
```

Characters live inline at `+0x04` while `capacity < 0x10`, otherwise behind a pointer there.
Confirmed from two directions — it is what the UserData getter reads (capacity at `+0x18`,
characters at `+0x04`) and what `CFCXOptionGamePage`'s own constructor builds for its page name.

`std::wstring` is the same shape with 8 inline `wchar_t` while capacity is under 8.

`CUIPageBase`'s page-name `std::string` starts at `+0x28` with its embedded allocator. The three
fields `Init` itself reads are the data at `+0x2c`, size at `+0x3c` and capacity at `+0x40`, so
overwriting only those leaves the constructor's allocator in place. `Init` branches on
`capacity < 0x10` to decide where the characters live, which is why the name is kept short —
`FCSE_PAGE` is 9 characters and lives inline, so there is no allocation, no heap ownership and no
`CryStringBase` refcount emulation to reproduce.

`CMenuPage`'s stored title `std::wstring` starts at `+0xf0` with its allocator; data `+0xf4`, size
`+0x104`, capacity `+0x108`.

The empty-string constant this build's `std::string` points its proxy field at is at `0x10fd42d1`;
FCSE copies it so a forged string looks exactly like one the engine made.

## Page field offsets

| Offset | Field |
| --- | --- |
| `+0x08` | row-list element — written by `FetchMagmaElements` |
| `+0x0c` | row-list box — `"` |
| `+0x10` | title text — `"` |
| `+0x14` | bound `magma::Page` — written by `SetPage` |
| `+0x68` | inited flag — set to 1 at the end of `Init` |
| `+0xec` | parent page — what `AddPage<T>`'s real caller passes as its second argument |
| `+0x140` | owning `CGameMenu*` — read by `CSetNextPageMenuHandler::SwitchPage` itself |

And on `CGameMenu` itself, `+0x3c` is the "next page" field: `SwitchPage` (`0x101d1990`) reads it,
deactivates `+0x40` (the current page) and activates this one. It never touches the page hashtable,
which is why this route avoids the `InsertNode` crash entirely.

## Menu item handlers

The engine's `IMenuItemHandler` is a struct whose first member is a vtable pointer. FCSE hand-rolls
these with 8 slots, one real entry and the rest safe no-ops.

**Slot 1 is the activate slot.** That is not a guess: an instrumented run on 2026-08-08 logged slot
1 and only slot 1 for a row click, and slot 0 is independently known to be the MSVC scalar deleting
destructor.

## Localised YES/NO

`RefreshOptionList` fills the `YES`/`NO` strings lazily, once per process, guarded by bits 1 and 2
of the flag word at `0x1164fca8`. The pointers themselves are at `0x1164fca4` (YES) and `0x1164fca0`
(NO). Since FCSE's page no longer runs `RefreshOptionList`, they are only populated if the player
has opened the stock Game tab at least once — so FCSE reads both defensively and falls back to
English literals.

Doing it properly means calling the OASIS lookup directly. Its shape is recovered and recorded here
so it can be picked up without re-deriving it:

```c
const wchar_t* __thiscall Oasis::GetLocalizedString(   // 0x104d1e40
    void* self,                    // *(void**)0x11644778
    const std::string& category,   // "Generic"
    const std::string& key,        // "YES" / "NO"
    void* extra);                  // *(void**)0x10f9d874
```

It is left undone because it is 40 lines of string marshalling for a cosmetic gain, and every other
label on the page is plugin-supplied English anyway.
