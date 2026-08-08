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

## The runtime object model

Everything below assumes this shape. It is worth getting right first, because the C++ API is split
across three object families that the `.mgb` vocabulary presents as one thing.

```
magma::Engine                     one per process; owns loaded packages
 └── magma::Package               one .mgb
      ├── magma::GenericObjectTable   the package's exported names
      ├── magma::Area              a timeline container
      │    └── magma::Element      a node: transform, visibility, keyframes, actions, UserData
      │         │                  (magma::Focusable is an Element subclass — see below)
      │         └── magma::Widget  the typed behaviour, attached at Element+0x14
      │                            Text, Image, ListBox, Slider, CheckBox, EditBox,
      │                            AreaInstance, PageInstance, ButtonInstance, Placeholder…
      └── magma::Page              a focus/input root that can go on the page stack
```

Three consequences that shape every API call you will make:

- **`Element` and `Widget` are different objects.** `Area::FindElement` returns an `Element*`. The
  `ListBox`/`Text`/`Image` you actually want hangs off it at **`Element+0x14`**, and you must
  type-check it before use. That is exactly what
  `CMagmaFacade::GetWidgetFromXml<T>` (`0x08a65860`) and `CMagmaFacade::GetListBox(Element*)` do:
  read `*(T**)(element + 0x14)`, call its `GetType()`, and run
  `magma::BaseObject::ObjectTypeInfo::IsKindOf(T::Type)`, returning `nullptr` on mismatch.
- **`Focusable` is not a Widget.** `CMagmaFacade::FindElement<magma::Focusable>` (`0x08af2e70`)
  runs `IsKindOf(Focusable::Type)` against the **element itself**, not against `+0x14`. This is the
  runtime counterpart of the `<FOCUSABLE>` wrapper documented in
  [the reference](./reference.md#widget-bodies): the wrapper is the element, the widget body inside
  it is the thing at `+0x14`. So a list box is a `Focusable` element carrying a `ListBox` widget,
  and input arrives as `ListBox::OnKeyDown(Focusable*, const KeyInput&, MessageData&)` — the widget
  is told which focusable it is acting for.
- **Names are resolved through a global registry, not a package pointer.**
  `magma::GenericObjectServer` is a `magma::Singleton` with `FindGenericObject(const Id&)`;
  `magma::Engine::LoadPackage` publishes each package's `GenericObjectTable` into it via
  `RegisterGenericObjectTable`, and `UnloadPackage` withdraws it. Once a package is loaded, its
  exported names are visible process-wide with no reference to the package that supplied them.

| Class | Role | Selected API |
|---|---|---|
| `magma::Engine` | Package lifetime | `LoadPackage(const FileName*, LoadErrorId&)` (+2 overloads), `UnloadPackage(Package*&)`, `UnloadAllPackages()`, `SetViewport`, `InitializeClientEngine(const ClientFactory*)` |
| `magma::Package` | One `.mgb` | `FindArea(Id)`, `FindArea(const BasicString&, bool)`, `AppendArea`, `InsertArea`, `RemoveArea`, `AppendFont`, `InsertMaterial`, `SetDefaultMaterial` |
| `magma::Area` | Timeline container | `FindElement(Id)`, `FindElement(const BasicString&, bool)`, `AppendElement`, `InsertElement(Element*, int)`, `RemoveElement`, `LinkElement`, `UnLinkElement`, `SetVisible(bool,bool)`, `SetPlaying(bool,bool)`, `SetTime`, `Tick(uint,bool)`, `SetFrameRate` |
| `magma::Element` | Scene node | `SetVisible(bool)`, `SetWidget(Widget*)`, `ExecuteActions(uint,uint,ushort)`, `SetTime`, `InsertKeyframe`, `PushDrawHandler`, `CopyFrom` |
| `magma::Focusable` | Input/focus element | `Activate()`, `Escape()`, `SetFocus(InputType)`, `KillFocus(InputType)`, `Enable(bool)`, `SetSelected`, `SetPressed`, `SetDefault`, `PushEventHandler`, `SetNeighbor`, `ResolveNeighbors()` |
| `magma::Widget` | Typed behaviour | `SetParent(EngineObject*)`, `InitState(Focusable*)`, `OnChangeState(Focusable*)`, `On{Key,Mouse}*`, `Interpolate`, `GetAreaLink(uint)` |
| `magma::Page` | Focus root / stack entry | `Enter()`, `Exit()`, `SetSelected(InputType, Focusable*)`, `SelectDefaultElement(InputType)`, `MoveSelection(InputType, Direction::Type)`, `PushEventHandler`, `FindTopPage()`, `Overlapped(Page&)` |

## `CMagmaFacade`: the engine-side UI API

Game code almost never touches `magma::` types directly. It goes through **`CMagmaFacade`**, a
singleton (`CMagmaFacade::ms_instance`, ctor `0x09608020`) of roughly 130 methods that wraps the
raw object model in `CryStringBase`-friendly, null-safe helpers. If you are writing a native UI
plugin, this is the surface to mirror — it already does the `Element+0x14` casting, the
`IsKindOf` checks and the not-found handling.

| Group | Methods |
|---|---|
| **Find by name** | `FindPackage(name)`, `FindArea(Package*, name)`, `FindPage(Package*, name)`, `FindElement(Area*, name/Id)`, `FindElement<T>(Area*, name)`, `FindAreaRecurse(Area*, path)`, `FindAreaInstance`, `FindAutonomousAreaInstance`, `FindList`, `FindText`, `FindImage`, `FindButton`, `FindMaterialInPackage`, `GetElement(Area*, index)`, `GetElementIndex`, `FindElementIndex` |
| **Global registry** | `GetGenericObject<T>(name)` for `Area`/`Element`/`Focusable`/`Page`/`Keyframe`/`PageFocusable`; `GetGenericObjectWidget<T>(name / Id)` for `ListBox`/`Text`/`EditBox`/`Image`/`Widget`/`AreaInstance`; `GetGenericObjectId(name)`, `GetGenericObjectFrameIndex(name)` |
| **Text** | `SetLabel(Text*, char/wchar_t, colour)`, `SetLabel(Area*, …)`, `SetLabel(AreaInstance*, …)`, `GetLabel(const AreaInstance*)`, `SetLabelRecurse(Area*, …)`, `LocalizeText(Area*, name, CStringID, bool)`, `GetVisibleStringLengthOfWrappedText` |
| **Geometry / visibility** | `SetVisible(Element*/Area*/AutonomousAreaInstance*, bool)`, `SetChildVisible(Area*, name/Id, bool)`, `GetPosition`, `SetPosition(Element*, Vec2i)`, `GetSize`, `GetColor`/`SetColor(Element*, Vec4f)`, `AlignElementPositionRelativeTo(Element*, Element*, bool, EElementAlignment)`, `ResizeFitColumns` |
| **Timeline** | `SetPlaying`/`IsPlaying`, `SetCurrentFrame`/`GetCurrentFrame`, `SetFrameRate`, `GetNextKeyFrame(Keyframe*)` — each overloaded for `Area*`, `AreaInstance*` and `AutonomousAreaInstance*` |
| **Lists** | `AddItem(ListBox*, const char*, void* userData)`, `RemoveItem`, `RemoveAllItems`, `SetItemColumn(…, const char*/Material*)`, `SetItemColumnColor`, `SetItemDisabled`, `SetItemPlaying`, `SetHeaderText`, `UseAsColumn`, `SetMaxVisibleItems`, `SelectNextListItem`, `SelectPreviousListItem`, `SetNomadListCurrentItem`, `GetListBox(Element* / name)` |
| **Materials** | `GetMaterial(pkg, name)`, `SetMaterial(Element*, Material*)`, `SetMaterial(AutonomousAreaInstance*, Material*, name)`, `CreateTexture(void*, size, name, Material*&)`, `CreateTexture(CSmartResourcePtr<CTextureResource>&, name, Material*&)`, `GetMaterialSize` |
| **Page stack / focus** | `Push(Page*, …)`, `Pop(…)`, `Peek(…)`, `GetPage(NamedObject*)`, `SelectDefaultElement(Page*, InputType)`, `ClearDefaultElement`, `SelectElement(Page*, name, InputType)`, `SelectWidget(Page*, Widget*)`, `PushMouseCursor`/`PopMouseCursor` |
| **Handlers** | `PushEventHandler(Page*, CEventHandlerNomadUI*)`, `PopEventHandler(Focusable*, EventHandler*)`, `PopAllEventHandlers(Page*)`, `PushPageHandler(Page*, CPageHandlerNomadUI*)`, `PopAllPageHandlers`, `PopAreaHandler` |
| **UserData** | `GetUserDataInteger(Action*, key, int&)`, `CopyUserData(UserData*, UserData*)` |
| **Type tests** | `IsMagmaListBox`, `IsMagmaText`, `IsMagmaImage`, `IsMagmaAreaInstance`, `IsMagmaAutonomousAreaInstance`, `IsMagmaButtonInstance`, `IsNameMatching(Focusable&, name)` |
| **XML config** | `GetWidgetFromXml<T>(Area*, XmlNodeRef&, tag, T*&)`, `GetAreaFromXml(Area*, XmlNodeRef&, tag, Area*&)` |

`CreateTexture` is the one route by which content that is *not* in any `.mgb` reaches the screen: it
wraps a raw buffer or a `CTextureResource` into a `magma::Material` you can then hand to
`SetMaterial`. Everything else in the table operates on objects the package already authored.

Two lighter-weight value handles wrap the same objects for call sites that only need a few
operations — `CPageHandle` (`FindElement`, `GetSelected`/`SetSelected`, `SelectDefaultElement`,
`PushEventHandler`, `PushAreaHandler`, `KeyDown`/`KeyUp`, `IsOnStack()`, `SetPlaying`,
`Get`/`SetCurrentFrame`, `GetId`) and `CAreaHandle` (`FindElement`, `GetUserData`, `IsVisible`,
`IsPlaying`, `GetCurrentFrame`, `IsKindOf`). `CPageStackHandle::Push(CPageHandle, …)` is the
handle-level page push.

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

### The routing, traced

Signals reach those classes through **`CMagmaActionDispatcher::OnActionSignal(const CStringID&,
magma::Action*)`** (`0x095f6370`). Decompiled, it does four things in this order:

1. **Snapshots the listener list.** The live `std::list<IMagmaActionListener*>` is copied to a
   temporary before anything is called, so a handler may register or unregister listeners mid-signal
   without invalidating the walk.
2. **Handles five actions itself, before any listener sees them** — see below.
3. **Otherwise broadcasts**, in list order, to every registered `IMagmaActionListener`:

   ```cpp
   if (m_listenersEnabled)                          // byte at dispatcher+4
       for (IMagmaActionListener* l : snapshot)
           if (l->IsActionListenerEnabled())        // vtable slot 0
               if (l->OnActionSignal(id, action))   // vtable slot 1
                   return;                          // consumed — no one else runs
   ```
4. Frees the snapshot.

So dispatch is a **global, ordered chain of responsibility, not page scoping**. Four facts follow:

- **Registration order is dispatch order.** `CMagmaActionDispatcher::AddListener` (`0x095f4780`)
  scans for the pointer first and appends only if absent — registration is idempotent, and the
  earliest registrant gets first refusal on every signal.
- **The first listener returning true consumes the signal.** Nothing downstream runs. A listener
  that handles an action it did not author will silently starve the screen that owns it.
- **`IsActionListenerEnabled()` is the gate that makes a global broadcast behave like page scoping.**
  Each listener declares whether it is currently interested; `CUIPageBase` exposes this as the
  overridable `OnIsActionListenerEnabled()`, which is how a page that is loaded but not on top stops
  responding.
- **There is a global kill switch.** The byte at `dispatcher+4` gates the entire loop.
  `CFCXUiService::DisableMagmaActionDispatcherOneFrame()` (`0x091eed00`) clears it and sets a flag to
  restore it next frame — used to swallow input during transitions. Note the five built-in actions
  below sit *outside* this gate and still fire while listeners are muted.

### Five actions the dispatcher handles itself

These never reach a listener, and therefore need **no native code at all**. Their `UserData` keys are
read by string, and the names match what the corpus actually uses
([reference](./reference.md#the-action-catalogue)):

| Action | Hash | Keys read | Effect |
|---|---|---|---|
| `ShowElementNomad` | `0xD43EE265` | `Target element` (link) | `Element::SetVisible(true)` |
| `HideElementNomad` | `0x4A5A04D2` | `Target element` (link) | `Element::SetVisible(false)` |
| `SetFocusNomad` | `0xCA729FD4` | `Target element` (link) | `Page::SetSelected(page, controller, element)` |
| `SetFocusListNomad` | `0x0AE41C5B` | `Target list` (link), `Top?` (bool) | selects first or last item, then focuses the list |
| `ShowSelectionListNomad` | `0xB8E88588` | `Target list` (link), `Focus?` (bool) | toggles the list's selection highlight without moving focus |

The hashes are plain **CRC32** of the action name — confirmed by computing all five and matching the
dispatcher's compiled constants exactly, which also re-confirms `CRC32("Stop") = 0x1964B988` from the
keyframe corpus. `SetFocusNomad` resolves the owning page with `CMagmaFacade::GetPage(NamedObject*)`
rather than being told it, so the target element may live anywhere in the loaded tree.

### The second hop: pages and modules

`CUIPageBase` is itself an `IMagmaActionListener`. Its base `OnActionSignal` (`0x091296e0`) is a
second chain of responsibility:

```cpp
bool CUIPageBase::OnActionSignal(const CStringID& id, magma::Action* a) {
    for (IUIModule* m : m_modules)      // populated by RegisterModule
        if (m->OnActionSignal(id, a))   // vtable slot 6
            return true;
    return false;
}
```

A derived page overrides this, handles its own actions, and delegates the rest to the base — which
offers them to each `IUIModule` registered via `CUIPageBase::RegisterModule(IUIModule*)` /
`UnRegisterModule`. `CNavBarModule` is the worked example: it is a reusable widget-plus-behaviour
bundle with `OnLinkedToPage(CUIPageBase*)` / `OnUnLinkedToPage(CUIPageBase*)` hooks, shared by every
screen with a nav bar instead of being reimplemented per page.

The full round trip is therefore:

```
element raises ACTIONNAME
  → ActionServer::MakeAction builds the registered Action object (its UserData = the arguments)
    → CMagmaActionDispatcher::OnActionSignal(CStringID(name), action)
       ├── one of the five built-ins?  handle inline, done
       └── else, in registration order, to each enabled IMagmaActionListener
            └── CUIPageBase::OnActionSignal
                 ├── the derived page's own handling
                 └── each registered IUIModule, until one returns true
```

## Registering for events

Actions are only one of **four** independent notification mechanisms. They differ in what they
observe and where you attach them, and a real screen uses several at once.

| Mechanism | Interface | Attach to | Observes |
|---|---|---|---|
| Action listener | `IMagmaActionListener` | dispatcher (global) | named actions raised by any element |
| Event handler | `magma::EventHandler` | a `Focusable`, or a `Page` | input and focus on that widget/page |
| Page handler | `magma::PageHandler` | a `Page` | page lifecycle and page-level input |
| Draw / area handler | `magma::DrawHandler`, `magma::AreaHandler` | an `Element` / an `Area` | the render pass |

### Event handlers — per-widget input and focus

`magma::EventHandler` is a virtual interface. Every method takes the `Focusable` it concerns, so one
handler instance can serve many widgets:

```cpp
OnActivate(Focusable&)                          OnEscape(Focusable&)
OnSetFocus(Focusable&)                          OnKillFocus(Focusable&)
OnKeyDown(Focusable&, const KeyInput&)          OnKeyUp(Focusable&, const KeyInput&)
OnMouseDown/OnMouseUp/OnMouseMove(Focusable&, const MouseInput&)
OnMouseEnter/OnMouseLeave/OnMouseDoubleClick(Focusable&, const MouseInput&)
OnEnterPageInstance(InputType, PageInstance*, Focusable&, Focusable&, Focusable*&)
```

Handlers form a **stack**, not a list: `Focusable::PushEventHandler(RefPtr<EventHandler>)`,
`PopEventHandler()`, `PopAllEventHandlers()`, with the same trio on `magma::Page`
(plus `Page::HasPushedEventHandler`). The `Focusable::Notify*` family (`NotifyActivate`,
`NotifySetFocus`, `NotifyKeyDown`, `NotifyMouseEnter`, …) is what walks that stack; the
`Focusable::KeyDown`/`MouseDown`/`Activate` entry points are what the engine calls into.

Game code does not implement `magma::EventHandler` directly. It derives **`CEventHandlerNomadUI`**,
which owns a nested `SEventHandlerImpl : magma::EventHandler` and forwards to the outer class's own
virtuals (`OnKeyDown`, `OnKeyUp`, `OnMouseDown`, `OnMouseUp`, `SetFocus`, `KillFocus`) — a pimpl that
keeps game classes free of magma's inheritance. Registration is
`CUIPageBase::AddEventHandler(CEventHandlerNomadUI*)` / `RemoveEventHandler`, which reaches
`CMagmaFacade::PushEventHandler(Page*, CEventHandlerNomadUI*)`.

### Page handlers — lifecycle

`magma::PageHandler` covers what an event handler cannot see:

```cpp
OnEnter(Page&)                 OnExit(Page&)
OnTick(Page&, uint)
OnKeyDown/OnKeyUp(Page&, const KeyInput&)
OnMouseDown/Up/Move/Enter/Leave/DoubleClick(Page&, const MouseInput&)
OnOverlapped(Page&, Page&)     OnUnOverlapped(Page&, Page&)
```

`Overlapped`/`UnOverlapped` fire when another page is pushed on top of or popped off this one — the
correct hook for "pause when a dialog opens". The game's adapter is `CPageHandlerNomadUI`, pushed
with `CMagmaFacade::PushPageHandler(Page*, CPageHandlerNomadUI*)`; `Page::NotifyPageHandlers` walks
them, and `Page::HasPushedPageHandlerType(const Handler::ObjectTypeInfo*)` tests for one by type.

### Draw handlers — the render pass

`magma::DrawHandler` (`OnPreDraw(const Element&)`, `OnPostDraw(const Element&)`) and
`magma::AreaHandler` (`OnPreDraw(Area&)`, `OnPostDraw(Area&)`) bracket drawing. Push them with
`Element::PushDrawHandler(RefPtr<DrawHandler>)` / `Area::PushDrawHandler` /
`Area::PushAreaHandler(RefPtr<AreaHandler>)`, and the matching `Pop…`/`PopAll…`. These are the only
hooks that let native code inject rendering into a Magma page rather than merely reposition
authored content.

## Reading and writing parameters

`magma::UserData` is the parameter store. Every element, and every `Action` object, is a `UserData`
— a keyed map of `magma::Variant`, addressable by `magma::Id` (the name hash) or by the literal
string. It is **readable and writable at runtime**, which makes it the general-purpose channel
between the layout and the code.

| Direction | Methods |
|---|---|
| Read | `GetUserData(key)` → `Variant`; typed: `GetUserDataBool`, `GetUserDataInteger`, `GetUserDataFloat`, `GetUserDataString`, `GetUserDataPointer`, `GetUserDataElement`, `GetUserDataArea`, `GetUserDataAreaLink` (→ `FullLink*`), `GetUserDataKeyframe`, `GetUserDataStringResourceExternalId` — each `(key, out&)` returning `bool` |
| Enumerate | `GetUserDataItem(uint index)` |
| Write | `AddUserData(key, const Variant&)`, `SetUserData(key, const Variant&)`, `RemoveUserData(key)`, `ReserveNbUserData(uint)`, `CopyFrom(EngineObject*)` |

Every typed getter has both a `magma::Id` and a `BasicString<char>` overload, and they map
one-to-one onto the `UserData` value kinds you author in the `.mgb`
([reference](./reference.md#userdata-properties)) — so the XML side and the C++ side are the same
table seen from two directions.

**This is how an action's arguments arrive.** `CMagmaActionDispatcher::OnActionSignal` reads
`Target element` and `Focus?` straight off the `Action*` it is handed, and
`CMagmaFacade::GetUserDataInteger(magma::Action*, const char*, int&)` is the convenience wrapper
game handlers use. An action's argument schema is not declared anywhere — it is whichever keys its
C++ class chooses to read, which is why the tables in the reference were recovered by decompilation
rather than from a manifest.

### Per-instance overrides on `AreaInstance`

The one place Magma does have something like data binding is `magma::AreaInstance` — an element that
re-instantiates another `Area`. Rather than editing the shared source area, you override values
**per instance, keyed by `Id`**:

| Call | Overrides |
|---|---|
| `AreaInstance::SetLabel(Id, const BasicString<wchar_t>&)` (also `(const char*, …)` and `PKw` forms) | a text element's string inside this instance |
| `AreaInstance::SetStringResourceLabel(Id, const StringResourceExternalId&)` | the same, as a localised OASIS key |
| `AreaInstance::SetMaterial(Id, Material*)` / `(const char*, Material*)` | an image's material inside this instance |
| `AreaInstance::RemoveLabel(Id)`, `RemoveResourceLabel(Id)` | drops back to the source area's value |
| `AreaInstance::SetTimeOffset(int)`, `SetIndexOffset(int)` | staggers this instance's timeline |

`CMagmaFacade` mirrors these as `SetLabel(AreaInstance*, …)`, `GetLabel(const AreaInstance*)` and
`GetSubAreaInstance(AreaInstance*, name)`. Authoring one row/card/button area and stamping it out N
times with different labels is exactly this mechanism, and it is why `AreaInstance` is the
second-most common element in the corpus.

Nothing in a `.mgb` is substituted at **load** time — there is no templating pass. These overrides
are applied by code after the package is live.

## Adding widgets at runtime

The blunt answer: **you don't, and neither does the game.**

The mutation API exists and is complete — `Area::AppendElement(Element*)`,
`Area::InsertElement(Element*, int)`, `RemoveElement`, `LinkElement`/`UnLinkElement`,
`ReserveNbElement(int)`, `Element::SetWidget(Widget*)`, `Package::AppendArea`/`InsertArea`, and every
element type has a `ms_pool` `magma::MemoryPool` to allocate from. But it is the **package loader's**
API: `magma::BinaryLoadVisitor` builds the tree with it while parsing the `.mgb`. No game-code call
sites were observed constructing an element and appending it to a live area, and the whole rest of
the API — find-by-name, type-check, set properties — is shaped around the assumption that the tree
is fixed once loaded.

What the game does instead, in descending order of how much you get for free:

1. **`ListBox` items.** The one genuine create-at-runtime path. `ListBox::AddItem(const wstring&,
   void* userData)` (also `const wchar_t*` and `StringResourceExternalId` overloads) clones the
   authored row template per item; `InsertItem(int, …)`, `RemoveItem(int)`, `RemoveAllItems(bool)`
   and `RemoveDuplicatedItems()` complete it. Rows are addressed by index thereafter:
   `SetItemColumn(row, col, text/Material*)`, `SetItemColumnColor`, `SetItemDisabled`,
   `SetCurrentItem(int, bool, bool)`, `Sort(int)` / `SetSortFunction(const SortFunctor*)`,
   `FindItem(const wchar_t*)`, `GetAreaLink(uint)` for the row's own area. The `void* userData` you
   pass to `AddItem` round-trips back to you, which is how rows carry a payload pointer.
2. **`AreaInstance` stamping.** Instantiate an authored area repeatedly and override labels and
   materials per instance, as above.
3. **Pre-authored slots.** Author more widgets than you need, hide them, and reveal the ones you
   use. This is the standard trick for a variable-length screen that is not a list — and it is what
   FCSE does: ship a layout declaring 20 `FCSE_SLOT_nn` widgets, then drive them by name.

`Area::SetDynamicSize()` and `Area::SetStaticBox(const Rect2D<short>&)` control whether the
container re-measures itself after its contents change, which is what keeps a rebuilt list from
clipping.

:::warning[Rebuilds discard anything you appended out-of-band]
`RemoveAllItems` at 70 call sites against `AddItem` at 81 is the shape of the whole system: screens
**rebuild their lists from scratch** every time they display. Anything inserted outside that rebuild
disappears on the next `Display()` — the trap FCSE hit twice.
:::

## Element names are indirected through XML config

The name a page looks up is not always a literal in the binary. Two cooperating systems put an XML
file in between.

**`CFCXUiService::AddMagmaUIConfig(CMagmaConfigUIResource*)`** (`0x091f0640`) walks a config
resource's XML children, hashes each node's *name* with `magma::Id::Hash`, and inserts it into a
`std::map<magma::Id, XmlNodeRef>` on the service, recursing into nested resource containers. The
result is a registry of config nodes keyed by hashed name.

**`CMagmaFacade::GetWidgetFromXml<T>(magma::Area* root, XmlNodeRef& cfg, const char* childTag,
T*& out)`** then resolves a widget through one of those nodes. Both compiled specializations
(`magma::Text` at `0x08a65860`, `magma::AreaInstance` at `0x08a656c0`) are byte-for-byte the same
shape:

1. `cfg->findChild(childTag)` — bail if absent, leaving `out` untouched;
2. read the **`path`** attribute → `CMagmaFacade::FindAreaRecurse(root, path)`;
3. read the **`text`** attribute → `magma::Area::FindElement(area, name)`;
4. type-check `*(T**)(element + 0x14)` with `IsKindOf(T::Type)`; `nullptr` on mismatch.

So a config node child looks like `<someTag path="a_containing_area" text="t_the_element"/>`, and
the attribute names are fixed regardless of widget type. `GetAreaFromXml` is the same for areas.

Separately, **`CMagmaElementFactory`** (singleton, ctor `0x09283310`) is a fuller version of the
same idea: `LoadFromXML(const XmlConstNodeRef&)` parses a config into per-page `CFactoryPage`
objects — each holding a package name, page attributes, an element map and a material map keyed by
`CStringID` — with `ParseItems` and `ParseMaterials` doing the reading. Pages then ask for logical
names: `GetElement(pageId, id)`, `GetText`, `GetListBox`, `GetImage`, `GetAreaInstance`,
`GetButtonInstance`, `GetPrivateElement`, `GetMaterial`, `GetElementName(pageId, id)`,
`GetPackageName(pageId)`, `GetPageAttributes(pageId)`. `CFactoryPage::CacheElements(Area*,
Package*)` and `CElement::Cache(Area*)` resolve the logical names against a live package once, and
`RebuildElement`, `CopyElements(pageA, pageB)`, `Finalize(pageId)` and `Reset()` manage the lifetime.

The modding consequence is real: where a screen goes through the factory or `GetWidgetFromXml`,
**the binding between code and layout is a data file, not a hardcoded string** — you can re-point it
at differently-named widgets without patching code. Where a screen calls `FindElement` with a
literal (as `CUIPageBase::FetchMagmaElements` does for `p_menu_nav` and `a_title_bar`), you cannot.
Which screens use which was not enumerated.

## Page lifecycle

`CUIPageBase` is the base every menu screen derives from. Its surface is small and worth knowing in
full, because a native page must participate in all of it:

| Group | Methods |
|---|---|
| Lifetime | `Init()` / `DoInit()`, `Unload()` / `DoUnload()`, `Shutdown()` / `DoShutdown()` |
| Display | `Display()`, `Hide()`, `Update(float)`, `GetShowCursor()`, `GetLayer()`, `GetTopLevel()` |
| Binding | `SetPage(magma::Page*)`, `FetchMagmaElements()`, `ConfigPage()` |
| Navigation | `PushPage()`, `PopPage()` |
| Events | `OnActionSignal(const CStringID&, magma::Action*)`, `AddListener`/`RemoveListener(IMagmaActionListener*)`, `IsActionListenerEnabled()` / `OnIsActionListenerEnabled()`, `AddEventHandler`/`RemoveEventHandler(CEventHandlerNomadUI*)` |
| Composition | `RegisterModule`/`UnRegisterModule(IUIModule*)`, `AddCommand`/`RemoveCommand(CUICommand*)`, `ExecuteCommands()` |

`Init()` is the step that binds the C++ object to its layout, by hashing the page's authored name and
resolving it through `GenericObjectServer::FindGenericObject` — which is why a page's `.mgb` must be
loaded before its class is constructed, and why skipping `Init()` produces a page object whose
element pointers are all null. `FetchMagmaElements()` is the override where a derived page grabs and
caches the widgets it will drive; `ConfigPage()` is a pure forward to a vtable slot, i.e. a
derived-class hook with no base behaviour.

## There is no Lua in the menus

Far Cry 2 does embed Lua — see [the Lua API surface](../engine-internals/lua-api-surface.md) and
[Domino scripts](../engine-internals/domino-scripts.md) — but that surface is mission and gameplay
scripting. It exposes **no UI construction, no widget access and no menu control**; the only
menu-adjacent entries in the whole binding table are game-state toggles like `SetCinematicUIMode`
and `SetShowRescueBuddyInMenu`. Menu logic is compiled C++ with no scripting layer, which is why
adding behaviour means a native plugin rather than a script.

## How runtime data reaches a widget

Nothing in a `.mgb` is filled in at load time — there is no templating pass and no substitution.
Native code **finds live objects by name hash and writes into them** (the nearest thing to binding
is the per-instance `AreaInstance` override described [above](#per-instance-overrides-on-areainstance),
which is also applied by code after load):

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
- **Show/hide and focus, via the five `*Nomad` actions.** `ShowElementNomad`, `HideElementNomad`,
  `SetFocusNomad`, `SetFocusListNomad` and `ShowSelectionListNomad` are executed by the dispatcher
  itself and never reach game code — a `.mgb` can toggle visibility and move focus anywhere in the
  loaded tree with no C++ behind it. This is the largest data-only capability on the list and the
  corpus already uses all five.
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

And if you are writing that native plugin, the shape it must take is now fully determined:

| You want to… | Do this |
|---|---|
| Get your layout's names resolvable | `magma::Engine::LoadPackage`, which publishes the package's `GenericObjectTable` to `GenericObjectServer` |
| Reach a widget | `CMagmaFacade::GetGenericObjectWidget<T>(name)`, or `FindElement` + the `Element+0x14` type-check |
| React to a click | Implement `IMagmaActionListener`, register it, and gate yourself with `IsActionListenerEnabled()` |
| React to focus/keys on one widget | Push a `CEventHandlerNomadUI` |
| React to your screen opening/closing | Push a `CPageHandlerNomadUI` and use `OnEnter`/`OnExit`/`OnOverlapped` |
| Read an action's arguments | `magma::UserData` typed getters on the `Action*` |
| Put text/values on screen | `CMagmaFacade::SetLabel`, `SetMaterial`, `SetVisible`, `ListBox::AddItem` |
| Show a variable number of things | A `ListBox`, or `AreaInstance` stamping, or pre-authored hidden slots — never `Area::AppendElement` |
| Draw something Magma cannot | `CreateTexture` into a `Material`, or push a `DrawHandler` |
