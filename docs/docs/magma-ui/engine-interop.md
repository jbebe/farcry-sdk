---
sidebar_position: 5
---

# Interop with the Dunia engine

:::info[Verified via reverse engineering]
Symbol names, addresses and call counts are read from `FarCry2_server`'s symbol table (the Linux
dedicated-server build links the same portable UI code with full `magma::` and game-class symbols)
and cross-checked against `Dunia.dll` by string reference. Where a claim is inferred or untraced,
it says so.
:::

A `.mgb` is a **view with no behaviour**. It draws, it animates, it can drive its own timeline — and
that is the whole of what it can do alone. Everything else is a contract with compiled C++ in
`Dunia.dll`, and the contract is made entirely of **names**. This page is that contract.

## The split

| Lives in the `.mgb` | Lives in `Dunia.dll` |
|---|---|
| Geometry, colour, materials, fonts | Every decision |
| Keyframe animation and its easing | What a click *means* |
| Timeline control (`Stop`, `Continue`, `GotoFrameIndex`, `GotoKeyframe`) | The list of action names that exist at all |
| Which named action an event raises | The handler that receives it |
| Which widgets exist, and their names | What text/values go into them |

## Actions: a registry compiled into the binary

`ACTIONNAME` in a `.mgb` is `CRC32(string)`. `magma::ActionServer` holds a `name-hash → Action*
(*create)()` table, built once at startup by exactly two registrars:

| Registrar | `FarCry2_server` | `Dunia.dll` | Registers |
|---|---|---|---|
| `magma::ActionServer::RegisterStandardActions` | `0x09fdb500` | `0x10ab8000` | **6** — magma's own |
| `CMagmaActionDispatcher::RegisterCustomActions` | `0x095f47f0` | `0x105031b0` | **81** — the game's |

`magma::ActionServer::RegisterAction(char const* name, Action* (*create)())` takes a literal string
and a factory pointer, hashes the name with `magma::Id::Hash`, and appends a `CreateEntry`.
`MakeAction` later runs `FindActionCreateFunc` — a linear scan — and calls the factory. **There is no
data path into this table.** A name your package fires that nobody registered resolves to nothing.

Each game action is a template instantiated over a per-action C++ symbol, which is what makes 81
distinct classes out of two templates:

```
CActionSignal<&_magmaactiondispatcher_NavBar_ButtonActivated>::CreateObject
CInputAction  <&_magmaactiondispatcher_Vote>::CreateObject
```

`CInputAction` is the variant carrying a `Trigger` string — which is exactly the `Trigger` `UserData`
key you see on `KeyPressed`, `Vote`, `Setting_Next_Value` and the `*_Gamepad` actions in shipped
packages. **An action's argument schema is compiled in too**: `SoundEvent` reads
`Sound Event Name` + `Sound Event Type` because its C++ class does, and no other keys mean anything
to it.

### The six standard actions

`Continue`, `GotoFrameIndex`, `GotoKeyframe`, `PopPage`, `PushPage`, `Stop` — the first five read
directly off `Dunia.dll 0x10ab8000`'s string references, the sixth confirmed by
`CRC32("Stop") = 0x1964B988`, the hash on 1,279 corpus keyframes.

**`PushPage` and `PopPage` are registered and no shipped package uses either** (0 uses of
`#3FDC56C2` / `#2BB5AD8B` across all 50). That is the one unexplored data-only capability worth
poking: page navigation without native code. Their argument keys are unknown for the same reason —
there is no shipped example to read them off.

### The 81 game actions

The complete registry, from the union of `RegisterCustomActions`' string references and the
`CActionSignal`/`CInputAction` template instantiations (both sets agree at exactly 81):

| Group | Names |
|---|---|
| Generic widget | `Activated`, `Escaped`, `CheckBox_Activate`, `ListBox_SelectionChanged`, `List_Item_Selected`, `Slider_ValueChanged`, `SetFocusNomad`, `SetFocusListNomad`, `ShowElementNomad`, `HideElementNomad`, `ShowSelectionListNomad`, `HidePageNomad`, `KeyPressed`, `SoundEvent` |
| Menu lists | `MenuList_Item_Activated`, `MenuList_Item_Escaped`, `MenuList_Item_Selected`, `MenuList_SelectItem`, `MenuList_Left_KeyDown`, `MenuList_Right_KeyDown`, `MapList_Item_Escaped`, `TapeList_Item_Selected` |
| Settings rows | `Setting_Activated`, `Setting_Next_Value`, `Setting_Previous_Value` |
| Drop-downs | `DropDown_Activate`, `DropDown_Item_Activate`, `DropDown_Item_Escape`, `DropDown_GotFocus`, `DropDown_LostFocus` |
| Message boxes | `MessageBox_Accept`, `MessageBox_Cancel`, `MessageBoxList_SelectItem` |
| Nav bar | `NavBar_ButtonActivated` |
| Music / misc | `OnMainMenuMusicStart`, `OnMainMenuMusicStop`, `OnCreditMusicStart`, `Diamond_StartCount` |
| Bazaar | `Bazaar_Buy`, `Bazaar_Buy_Gamepad`, `Bazaar_Cancel`, `Bazaar_CancelShopPad`, `Bazaar_CancelCheckoutPad`, `Bazaar_CheckOut`, `Bazaar_CheckOut_Gamepad`, `Bazaar_Checkout_List_Selected`, `Bazaar_Category_LeftSelectPad`, `Bazaar_Category_RightSelectPad`, `Bazaar_WeaponShop_Category_Selected`, `Bazaar_WeaponShop_MainList_Selected`, `Bazaar_Done`, `BazaarShop_AddRemove`, `BazaarCheckout_AddRemove` |
| Map editor (IGE) | `IGE_SelectTool`, `IGE_Tool_Settings_HeaderFocus`, `IGE_Tool_Settings_Update`, `IGE_Toolbox_Open_Done`, `IGE_Toolbox_Close_Done` |
| Multiplayer | `Multi_JoinMatch`, `Multi_DeleteMap`, `Multi_LaunchMapEditor`, `Multi_Loadout_ChangeWeapon`, `Multi_XP_ChangeRank`, `Multi_Avatar_ButtonActivated`, `Multi_Avatar_ButtonFocus`, `Multi_Avatar_CustomizationButton`, `Multi_Avatar_CustomizationLeftButton`, `Multi_Avatar_CustomizationRightButton`, `Vote` |
| Profile / chat | `Profile_Create`, `Profile_Load`, `Profile_ApplyChanges`, `Profile_UbiCom`, `CHAT_Setup`, `CHAT_Commit`, `CHAT_Cancel` |
| Player popup | `PlayerPopup_Show`, `PlayerPopup_Next`, `PlayerPopup_Previous` |
| Pause screens | `Pause_JackalFiles_TitleLR_Selected`, `Pause_PlayerStats_SettingsLR_Selected` |

Names that read generically (`Activated`, `Escaped`, `KeyPressed`, `SoundEvent`, the `*Nomad`
family) are the reusable ones. Everything under Bazaar/IGE/Multi/Profile is a doorbell wired to one
screen's handler: fire `Bazaar_Buy` from your own page and you get either nothing or the weapon shop
doing something you did not intend.

## Dispatch, and who is listening

`CActionSignalBase::Execute` (`FarCry2_server 0x095f9630`) is the `Action::Execute` override every
signal action shares — raising the action is a signal emission, not a direct call.

The receiving side is ordinary game code implementing **`OnActionSignal`**. Roughly forty classes do,
one per screen or service:

```
CFCXBaseOptionPage   CFCXOptionNetworkPage   CFCXBrightnessPage      CFCXLobbyPage
CFCXPauseBuddiesPage CFCXPauseGameStatsPage  CFCXPlayerStatsPage     CFCXReputationPage
CBazaarComputerUI    CLoadOutUI              CPlayerPopupMenu        CEndOfGamePage
CFCXMainHudUI        CFCXHudService          CSavePointSaveGamePage  CGameOverLoadPage
CFCXMultiCreateMapRotationPage   CValueListSetting<unsigned int>     …
```

So the round trip is: your element raises `MenuList_Item_Activated` → the active page's
`OnActionSignal` runs → that page calls back into the live widget tree to update it.

**Untraced:** how a raised signal is routed to a particular listener — whether it broadcasts to
registered modules, or is scoped to the page that owns the element. `CUIPageBase::RegisterModule` /
`AddListener` are the likely mechanism but were not followed. Everything above the routing step is
confirmed.

## There is no Lua in the menus

Far Cry 2 does embed Lua — see [the Lua API surface](../engine-internals/lua-api-surface.md) and
[Domino scripts](../engine-internals/domino-scripts.md) — but that surface is mission and gameplay
scripting. It exposes **no UI construction, no widget access and no menu control**; the only
menu-adjacent entries in the whole binding table are game-state toggles like `SetCinematicUIMode`
and `SetShowRescueBuddyInMenu`. Menu logic is compiled C++ with no scripting layer, which is why
adding behaviour means a native plugin rather than a script.

## How runtime data reaches a widget

There is no templating, no parameter substitution and no data binding. Nothing in a `.mgb` is
filled in at load time. Native code **finds live objects by name hash and writes into them**:

| Call | Call sites | Role |
|---|---|---|
| `magma::Package::FindArea` | 20 | area by name |
| `magma::Area::FindElement` | 60 | element by name |
| `magma::UserData::GetUserDataElement` | 12 | resolve a `FullLink` property to a widget |
| `magma::ListBox::AddItem` | 81 | append a row |
| `magma::ListBox::RemoveAllItems` | 70 | clear before a rebuild |
| `magma::Element::SetVisible` | 94 | show/hide |
| `magma::Slider::SetValue` | 16 | push a value |
| `magma::TextBase::SetString` | — | push text |

`RemoveAllItems` at 70 call sites against `AddItem` at 81 is the shape of the whole system: screens
**rebuild their lists from scratch** every time they display, which is why anything appended outside
that rebuild disappears (the trap FCSE hit twice).

### The name contract, in both directions

- **Names the code demands of your layout.** `CUIPageBase::FetchMagmaElements` looks up
  `p_menu_nav` → `l_menu_nav_list` and `a_title_bar` → `t_page_title` by hardcoded name. Miss one and
  the page renders empty — `AddButton` returns −1 and does nothing.
- **Names your layout offers to the code.** The `SETTING_*` `UserData` `FullLink` properties are a
  manifest: "the widget you will ask for as `SETTING_DIFFICULTY` is this one". That is the closest
  thing Magma has to a binding, and it carries a pointer, not a value.

Both are covered with worked examples in
[binding a page to native code](./patterns.md#binding-a-page-to-native-code).

## What is genuinely data-driven

Worth knowing precisely, because it is the part you can use with no code at all:

- **Timelines.** Keyframes plus `Stop`/`Continue`/`GotoFrameIndex`/`GotoKeyframe` are a real state
  machine — fades, reveals, page-flip animations and button states all run with no native
  involvement.
- **List row duplication.** A `ListBox` clones its template area per item; you author one row.
- **Widget-to-widget links.** `SLIDERLINK`, and the `ListBox`/`Slider`/`EditBox` sub-links.
- **`PushPage` / `PopPage`.** Registered, unexplored.
- **Localised text.** OASIS keys in a `Text` resolve at draw time.
- **The [`.mgb.desc` sidecar](./desc-sidecar.md).** The one declarative configuration layer — nav-bar
  prompts, HUD prompt element paths, controller layouts — keyed by page name.

## The practical shape of a UI mod

1. **Reskinning and re-layout** of an existing screen needs nothing but a `.mgb` edit: the names stay
   the same, so the code keeps finding its widgets.
2. **New animation and visual state** likewise — it is all timeline.
3. **New screens** need a native plugin, because a page is bound by a C++ page object calling
   `Init()`, and rows are built by native `AddButton`/`AddBoolSetting` calls. FCSE is the worked
   example: ship a layout declaring 20 `FCSE_SLOT_nn` widgets, then call against those names.
4. **New behaviour** always means code. There is no data-only path to a new action.
