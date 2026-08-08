---
sidebar_position: 2
---

# Authoring reference

:::info[The vocabulary is the engine's own]
Element and attribute names are the authored names recovered by joining `magma::BinaryLoadVisitor`
(wire order → object offset) against `magma::LoadVisitor` (XML name → object offset). Names marked
**(provisional)** come from a class's XML vocabulary rather than the per-field join — their width
and position are verified, only the label is a guess. Full working record:
[`.mgb` field names](../file-formats/mgb-field-names.md).

Frequencies quoted as "n/N" are counts over the 50-package vanilla corpus.
:::

This page is the complete set of things you can write in a `jackall mgb decode` document. The
reader is strict — an attribute it does not define, or an element in a place it does not define, is
an error, so anything not listed here will be rejected.

## Document skeleton

Order inside a list matters; named scopes are found by name. Every element listed is **required**
unless marked optional, including the empty ones.

```
<MagmaPackage sentinel version flag POOLCOUNTS PAGESIZE.w PAGESIZE.h
              DISPLAYOFFSET.x DISPLAYOFFSET.y DEFAULTMATERIAL>
  <TYPES>            <TYPE id/>×166
  <USERDATA>         the package's own property list
  <MATERIALS materialExtra>  <Material name texture REGION/>×n
  <FONTSUBSTS>       <FONTSUBST slot type fontData FONTSUBST/>×n   (embedded font blobs)
  <FONTS>            <FONT slot type name file/>×n
  <FONTFAMILIES>     <FONTFAMILY name font PACKAGE/>×n
  <CHILDREN>         <Area slot type …/>×n
  <STRINGTABLE>      optional
  <GENERICOBJECTTABLE> optional — but see below
```

| Attribute | Value |
|---|---|
| `sentinel` | `CD0000AB`. Only the last byte is checked; a different value flips the reader to big-endian. |
| `version` | `2010000` exactly. Any other value fails the load with error 5. |
| `flag` | Header byte 13. `false` everywhere in the corpus; purpose unknown. |
| `POOLCOUNTS` | 65 space-separated `u32` memory-pool hints. All-zero is safe. |
| `PAGESIZE.w/h` | The design canvas. `1280×800` in 49/50 shipped packages (`fonts.mgb` is `1280×720`); the 4:3 `pc` set uses `1024×768`. |
| `DISPLAYOFFSET.x/y` | `160 40` on the widescreen set, `32 24` on 4:3. |
| `DEFAULTMATERIAL` | Usually empty. |

`materialExtra` is the number of *distinct texture paths* among the materials — a setter argument,
not a loop count. Preserve it when editing; set it to the distinct-path count when authoring.

## Type slots

`slot="…"` is a raw index into the package's `<TYPES>` table, and the shipped table is identical in
all 50 packages, so these numbers are constants. `type="…"` beside it is decoration the reader
ignores — **the slot is authoritative**.

| Class | slot | Class | slot | Class | slot |
|---|---|---|---|---|---|
| `Area` | 44 | `Placeholder` | 77 | `ActionExecuter` | 133 |
| `Page` | 101 | `RectShape` | 76 | `ActionExecuterEvent` | 136 |
| `Button` | 99 | `Image` | 74 | `ActionExecuterInputable` | 137 |
| `CheckBox` | 100 | `Text` | 50 | `ActionExecuterFocusable` | 138 |
| `Cursor` | 68 | `Window` | 97 | `ActionExecuterEditbox` | 139 |
| `Element` | 69 | `AreaInstance` | 33 | `ActionExecuterListbox` | 140 |
| `Focusable` | 66 | `AutonomousAreaInstance` | 57 | `ActionExecuterPage` | 141 |
| `Keyframe` | 126 | `PageInstance` | 58 | `ActionExecuterPageInstance` | 142 |
| `PixmapFont` | 106 | `ButtonInstance` | 78 | `ActionExecuterSlider` | 143 |
| `EventTriggeredTimingStrategy` | 149 | `CheckBoxInstance` | 79 | `ListBox` | 61 |
| `TickTimingStrategy` | 150 | `RadioButtonInstance` | 80 | `EditBox` | 92 |
| `NoTimingStrategy` | 151 | `SyncTimingStrategy` | 152 | `Slider` | 64 |

Slots may be appended (the ceiling is 254, against the 167 shipped), but there is no reason to:
every class the engine can construct is already in the table.

## Areas

An `<Area>` is one entry of `<CHILDREN>`. Corpus mix: `Area` 371, `Page` 244, `Button` 141,
`Cursor` 15, `CheckBox` 2.

```xml
<Area slot="101" type="Page" FRAMERATE="30" CURRENTFRAME="0" STATICBOX="0 1280 0 800" …tail…>
  <USERDATA name="…"><PROPERTIES/></USERDATA>
  <ACTIONEXECUTER …/>            <!-- optional -->
  <CHILDREN>…elements…</CHILDREN>
  …tail elements…
</Area>
```

| Field | Meaning |
|---|---|
| `FRAMERATE` | Frames per second of this area's timeline. `30` (484) and `10` (201) cover 90% of the corpus; the engine stores `1000/FRAMERATE`. |
| `CURRENTFRAME` | Frame the area starts on. `0` everywhere. |
| `STATICBOX` | The area's declared box, in **LEFT RIGHT TOP BOTTOM** order. `0 0 0 0` is common for pure containers; `Button` areas carry their real extent (`9 191 7 31` for the standard menu button) and that is what the mouse tests against **(inferred)**. |

Per-type tails, appended after `</CHILDREN>`:

| Type | Tail |
|---|---|
| `Area` | none |
| `Page` | `<DEFAULT_ELEMENTS><DEFAULT_ELEMENT CONTROLLER ID/>…</DEFAULT_ELEMENTS>` then `SINGLE_GLOBAL_SELECTION` (attribute on `<Area>`). `CONTROLLER="255"` (any controller) in all 169 corpus entries; `ID` is the element that takes focus when the page opens. |
| `Cursor` | `HOTSPOT.x`, `HOTSPOT.y` (stored negated by the engine) |
| `Button` | `TIMINGS` — **6** frame indices |
| `CheckBox` | `TIMINGS` — **12** frame indices |

`TIMINGS` is the whole of Magma's interactive-widget model; see
[the button recipe](./patterns.md#a-button-is-a-six-state-timeline).

## Elements

```xml
<Element slot="74" type="Image" HIDDEN="false" ISDUPLICATABLE="true" MASKMODE="NOMASK">
  <USERDATA name="…"><PROPERTIES/></USERDATA>
  <ACTIONEXECUTER …/>              <!-- optional -->
  <KEYFRAMES><Keyframe …/>…</KEYFRAMES>
  <Image …>…</Image>               <!-- the widget body; element name == widget class -->
  <FOCUSABLE INPUTFILTER="255"><NEIGHBORS/></FOCUSABLE>   <!-- only for focusable widgets -->
</Element>
```

| Field | Meaning |
|---|---|
| `HIDDEN` | Inverted into `Element::SetVisible`. `false` 5,337 / `true` 85. |
| `ISDUPLICATABLE` | Whether the instancing pass may clone this element. **`true` on all 5,422 corpus elements** — treat `false` as untested. |
| `MASKMODE` | `NOMASK` (5,347) / `SETMASK` (26) / `USEMASK` (49) / `USEMASK_INVERTED` (0). See [masking](./patterns.md#masking-a-region). |

The 14 widget classes, the `Element` subclass each gets, and therefore whether a `<FOCUSABLE>` tail
follows:

| Widget | Wrapper | `<FOCUSABLE>` | Corpus uses |
|---|---|---|---|
| `Image` | `Element` | no | 2,127 |
| `Text` | `Element` | no | 810 |
| `AreaInstance` | `Element` | no | 711 |
| `Placeholder` | `Element` | no | 548 |
| `RectShape` | `Element` | no | 480 |
| `PageInstance` | `PageFocusable` | **yes** | 428 |
| `ButtonInstance` | `Focusable` | **yes** | 131 |
| `ListBox` | `Focusable` | **yes** | 101 |
| `EditBox` | `Focusable` | **yes** | 43 |
| `Slider` | `Focusable` | **yes** | 33 |
| `CheckBoxInstance` | `Checkable` | **yes** | 10 |
| `AutonomousAreaInstance` | `Element` | no | 0 |
| `RadioButtonInstance` | `Radioable` | **yes** | 0 |
| `Window` | `Element` | no | 0 |

`<FOCUSABLE>` carries the explicit navigation graph:

```xml
<FOCUSABLE INPUTFILTER="255">
  <NEIGHBORS>
    <NEIGHBOR CONTROLLER="255" DIRECTION="1" ID="b_next" />
  </NEIGHBORS>
</FOCUSABLE>
```

`DIRECTION`: `0` UP, `1` DOWN, `2` LEFT, `3` RIGHT. `CONTROLLER="255"` means any controller, and is
the only value in the corpus; likewise `INPUTFILTER="255"` on all 746 focusables. 592 of them
declare **no** neighbours at all — a `ListBox` navigates its own rows internally, so an explicit
graph is only needed between separate focusable elements.

## Widget bodies

Attribute order is irrelevant; presence is not.

### `Placeholder`

`<Placeholder />` — no fields. A named anchor with geometry and nothing drawn. Present as the first
child of most areas (527 of them named `action`), where it serves as the area's origin marker
**(inferred)**.

### `RectShape`

```xml
<RectShape ISOUTLINED="true" ISFILLED="true" BLENDINGMODE="Normal" />
```

Colours live in the keyframe (`FILLCOLOR1..4` corner colours, `OUTLINECOLOR`, `OUTLINEWEIGHT`), not
here. The only primitive Magma can draw without a texture.

### `Image`

```xml
<Image BLENDINGMODE="Normal" ALPHABLENDFIRST="false"
       ADDRESSINGMODEU="Clamp" ADDRESSINGMODEV="Clamp">
  <MATERIALLINK present="true" id="notebook" PACKAGE="" />
</Image>
```

`MATERIALLINK` names a `<Material>` — in this package when `PACKAGE=""` (1,234 uses), or in another
by **path** (`\common.mgb` 815, `\hud.mgb` 45). The material's own `texture` path is UI-root
relative with a leading backslash and resolves against the owning package's name.

### `Text`

```xml
<Text useStringTable="false" STRING="OPTIONS_DISPLAY_ASPECT_RATIO"
      ALIGNMENTX="LEFT" ALIGNMENTY="TOP"
      WRAPPING="false" CLIPPING="false" ELLIPSIS="true" AUTOSIZED="false"
      BOLD="false" ITALICS="false" UNDERLINED="false"
      BLENDINGMODE="Normal" ALPHABLENDFIRST="false">
  <FONTFAMILY present="true" id="Farcry2_25" PACKAGE="\fonts.mgb" />
</Text>
```

- `useStringTable="true"` swaps `STRING` for `TABLEID` + `RESOURCEID`. **No corpus file uses it** —
  the string-table path is dead in shipped data.
- `STRING` is UTF-16 on the wire. 290 of 810 shipped strings are OASIS localisation keys that
  resolve at runtime (`OPTIONS_DISPLAY_ASPECT_RATIO` → "Aspect Ratio"); most of the rest are
  design-time placeholders (`Wwwwwwwwwww` width rulers, `PLACEHOLDER TEXT…`) in templates whose text
  native code sets. Key resolution being automatic in `TextBase` is **(inferred)** from that data,
  not traced.
- `ALIGNMENTX`: `LEFT` / `CENTER` / `RIGHT` / `JUSTIFY`. `ALIGNMENTY`: `TOP` / `CENTER` / `BOTTOM`.
- `SLIDERLINK` (optional attribute) names a sibling `Slider` that scrolls the text.
- Font families come from `\fonts.mgb`; all 810 shipped `Text` widgets use one of two families,
  `Farcry2_25` (467) or `#24EE0F45` (343 — the family whose font file is `arial_cyrillic_25`; its
  own name string is not recoverable).

### Area instances — `AreaInstance`, `PageInstance`, `ButtonInstance`, `CheckBoxInstance`, `RadioButtonInstance`, `AutonomousAreaInstance`

```xml
<PageInstance LABEL="" INDEXOFFSET="0">
  <MATERIALLINK present="false" />
  <LINK slot="150" TIMING="TickTimingStrategy" PACKAGE="common"
        AREA="#36150990" ISUSINGDUPLICATEDAREA="true" />
  <DEFAULTFOCUSES />                <!-- PageInstance only -->
</PageInstance>
```

All five share one body; they differ only in the wrapper `Factory::MakeElement` gives them. **The
instance class must match the target area's class** — this holds without exception in the corpus:

| Instance widget | Target area type | Uses |
|---|---|---|
| `AreaInstance` | `Area` | 711 |
| `PageInstance` | `Page` | 428 |
| `ButtonInstance` | `Button` | 124 (+7 with no `LINK` at all) |
| `CheckBoxInstance` | `CheckBox` | 10 |

`LABEL` is the target area's *name as a string* — the one place a readable name survives, which is
why hashing labels recovers area names. Empty in 1,206 of 1,280 instances.

`<LINK>` is an `AreaLink`:

| Field | Meaning |
|---|---|
| `TIMING` | Timing-strategy slot: `TickTimingStrategy` (792, advances every tick), `NoTimingStrategy` (382, frozen on one frame), `SyncTimingStrategy` (84, follows the parent's frame), `EventTriggeredTimingStrategy` (15). |
| `PACKAGE` | Name **hash** of the owning package's bare name (`common`, not `\common.mgb`). |
| `AREA` | Optional; the target area's name hash. |
| `ISUSINGDUPLICATEDAREA` | `true` (728) gives this instance a private clone of the area — required whenever two instances of the same area must animate independently. `false` (545) shares one live instance. |

`INDEXOFFSET` is `0` in 1,279 of 1,280 instances.

### `ListBox` (names provisional)

```xml
<ListBox HEADERFOOTERPOS="0" AUTOCENTER="true" WRAPAROUND="false" SLIDESELITEM="false"
         flag4="false" BUTTONCOUNT="20" ITEMSPACING="0" SLIDERLINK="…">
  <HEADERLINK … />   <!-- wire position 1 — this is the ROW TEMPLATE -->
  <ITEMLINK … />     <!-- wire position 2 -->
  <FOOTERLINK … />   <!-- wire position 3 -->
</ListBox>
```

:::warning[The element named `HEADERLINK` is the row template]
The three link names are provisional and the evidence says they are mis-ordered. In every corpus
list that shows rows, the **first** link (`HEADERLINK`) points at the `Button` area duplicated once
per item, and the other two are absent: the 20-row options nav list `#36150990` sets only
`HEADERLINK`, to the standard row button `#CD72056E`. The lists that set all three are always
`BUTTONCOUNT="1" ITEMSPACING="1"` value spinners, where link 1 is the value label and links 2–3 are
the left/right arrow buttons. Judge by wire position, not by the name.
:::

`BUTTONCOUNT` is the number of row widgets the list instantiates — its **viewport size**, not its
item count. The list keeps a viewport and moves it with the selection, clamped at both ends, so a
long list scrolls with no scrollbar; `SLIDERLINK` names a sibling `Slider` that acts purely as a
visual indicator and drag target, and only 12 of 101 corpus lists set one.

`HEADERFOOTERPOS` holds values (`0`–`255`) a two-entry enum cannot be, so the name is probably
wrong; it is written as a bare number.

### `EditBox` (names provisional)

```xml
<EditBox maxLength="15" passwordChar="base64:KgA=">
  <FIELDLINK … /><CURSORLINK … />
</EditBox>
```

`passwordChar` is one UTF-16 unit (`base64:KgA=` is `*`), omitted when unset. `maxLength="0"` means
unbounded **(inferred)**; 20 of 43 corpus edit boxes use it.

### `Slider` (names provisional)

```xml
<Slider RANGEMIN="0" RANGEMAX="10" field2="0" field3="1" field4="1" ORIENTATION="false">
  <TRACKLINK … /><KNOBLINK … /><HEADERLINK … /><FOOTERLINK … />
</Slider>
```

`ORIENTATION="false"` is horizontal (31 of 33). `RANGEMAX` is `10` in 24 of 33 — sliders report a
normalised 0–10 position and the page maps it to a real value. All four links are set in 31 of 33.

### `Window` (untested)

The 9-patch border class: `SINGLECORNERMATERIAL`, `SINGLEEDGEMATERIAL`, then nine sections in the
engine's own order — `FILL`, `TOP_LEFT_CORNER`, `TOP_RIGHT_CORNER`, `BOTTOM_LEFT_CORNER`,
`BOTTOM_RIGHT_CORNER`, `TOP_EDGE`, `LEFT_EDGE`, `RIGHT_EDGE`, `BOTTOM_EDGE`. Each carries a
`<MATERIAL>`, `BLENDINGMODE`, `ALPHABLENDFIRST`, `FLIPHORIZONTAL`, `FLIPVERTICAL`, `ROTATED`, plus
`STRETCHMODE` on the stretchable ones (`FILL` and the four edges). **Zero uses in the corpus** — the
layout is decoded and writable, but nothing shipped exercises it.

## Keyframes and states

```xml
<Keyframe name="kf_fade_in" IDX="0" INTERPOLATION="Linear">
  <ACTIONEXECUTER …/>          <!-- optional; this is where Stop/GotoFrameIndex go -->
  <ImageState … />             <!-- class decided by the owning widget -->
</Keyframe>
```

`IDX` is the frame number. `INTERPOLATION` is the easing *into the next* keyframe: `None` (7,559),
`Linear` (3,054), `Root` (178), `Sin` (131), `Circle` (25), `Square` (2), `CircleDecel` (0).

The state class is fixed by the widget class — you cannot choose it:

| Widget | State | Widget | State |
|---|---|---|---|
| `Image` | `ImageState` | `Placeholder`, `Window` | `RectState` |
| `Text` | `TextState` | every instance widget, `ListBox`, `EditBox`, `Slider` | `ScaleState` |
| `RectShape` | `RectShapeState` | | |

Fields, cumulative down the hierarchy. **All coordinates are `u16`; write a negative as
`65536 + v`.** All colours are ARGB hex.

| Class | Fields |
|---|---|
| `State` (base of all) | `INTERPOLATIONFLAGS` (u32 bitmask of which channels interpolate), `STATECOLOR` (ARGB tint; alpha is how you fade anything) |
| `+ RotationState` | `ROTATION` (float degrees), `ORIGIN.x`, `ORIGIN.y` |
| `+ PosState` | `POSITION.x`, `POSITION.y` |
| `+ ScaleState` | `SCALEX`, `SCALEY` (floats, `1` = native) |
| `+ RectState` | `LEFT`, `RIGHT`, `TOP`, `BOTTOM` — **in that order** |
| `+ TextBaseState` | `OFFSETY` (float), `ABSOFFSETY` |
| `+ TextState` | `SHADOWCOLOR`, `HEIGHT` (point size), `SHADOWOFFSETX/Y`, `LEADING`, `TRACKING` |
| `+ ImageState` | `SHADOWCOLOR`, `SHADOWOFFSETX/Y`, `TILING.x/y`, `OFFSET.x/y` (floats), `FLIPHORIZONTAL`, `FLIPVERTICAL`, `ACTUALSIZE`, `COLOR1`–`COLOR4` (gradient-quad corners) |
| `+ RectShapeState` | `OUTLINEWEIGHT`, `OUTLINECOLOR`, `FILLCOLOR1`–`FILLCOLOR4`, `SHADOWCOLOR`, `SHADOWOFFSETX/Y` |

`ScaleState` positions an instance; `RectState`-derived states size a rectangle. `PosState` and
`RectState` are siblings sharing storage — `POSITION.x/y` occupy the same offsets as `LEFT/RIGHT`.

## Names, links and references

Four different reference forms, easy to confuse:

| Form | Written as | Used by |
|---|---|---|
| Name hash | `name="p_menu_nav"` or `#3D23C3C5` | every `NamedObject`: areas, elements, keyframes, property keys |
| `FullLink` | `<LINK slot LASTOBJECTTYPE IDS="a b c"/>` | `UserData` link properties, `GenericObject` targets, action arguments |
| `AreaLink` | `<LINK slot TIMING PACKAGE AREA ISUSINGDUPLICATEDAREA/>` | area instances, and the `ListBox`/`Slider`/`EditBox` sub-links |
| Resource ref | `<MATERIALLINK present id PACKAGE/>` | materials and font families |

A name renders as the readable string only when re-hashing it reproduces the stored value, so
`#XXXXXXXX` in a decode means the name is not recoverable — not that something is wrong. You may
write either form.

**`IDS` is a path from the package root**, one name hash per level, crossing into instanced areas:

```
IDS="fcse FCSE_PAGE p_menu_nav #36150990 l_menu_nav_list"
      ^pkg ^page      ^element   ^the area  ^element inside that area
                                  it instances
```

`LASTOBJECTTYPE` is the type slot of the final object. A two-element path (`common Cursor`) is the
common case: package, then area.

## UserData properties

```xml
<PROPERTIES>
  <PROPERTY key="LAYER" type="2" value="10" />
  <PROPERTY key="SETTING_LABEL_LIST" type="18">
    <LINK slot="66" LASTOBJECTTYPE="Focusable" IDS="fcse FCSE_PAGE p_menu_nav …" />
  </PROPERTY>
</PROPERTIES>
```

`type` is the wire tag in decimal. Every other tag is legal and carries no payload.

| `type` | Payload | Corpus |
|---|---|---|
| `2` (0x02) | `value` — u32 | 223 |
| `7` (0x07) | `value` — float | 0 |
| `12` (0x0c) | `value` — bool | 22 |
| `16` (0x10) | `value` — ANSI string | 17 |
| `17`/`18`/`21` (0x11/0x12/0x15) | `<LINK>` — a `FullLink` | 115 + others |
| `19` (0x13) | `<STRINGRESOURCE TABLEID RESOURCEID/>` | 19 |

Keys seen on areas: `LAYER` (159 — draw order, `10`/`50`/`126` are the common values),
`RESTRICT_INPUT`, `TOPLEVEL`, `SHOWCURSOR`, `MAINMENU_LISTACTIONS`, `BINKFILENAME`/`BINKLOOP`/
`BINKSTREAM` (Magma can host Bink video), and the `SETTING_*` family — those are the named widget
slots a native `CSettingsPage` looks up by name; see
[binding to native code](./patterns.md#binding-a-page-to-native-code).

## Actions and events

An `ActionCaller` may hang off an **`Area`**, an **`Element`**, or a **`Keyframe`**, and where it
sits decides what it is for:

| Site | Executer type | Uses | What it does |
|---|---|---|---|
| `Keyframe` | `ActionExecuter` (bare) | 1,523 | Timeline control — fires when playback reaches the frame |
| `Element` | `ActionExecuterFocusable` | 114 | Input/focus handling for a focusable widget |
| `Element` | `ActionExecuterListbox` | 76 | …plus list selection |
| `Element` | `ActionExecuterPageInstance` | 62 | …for an embedded page |
| `Element` | `ActionExecuterEditbox` | 34 | …plus text change |
| `Element` | `ActionExecuterSlider` | 10 | …plus value change |
| `Area` | `ActionExecuterPage` | 34 | Page lifecycle |

```xml
<ACTIONEXECUTER slot="138" type="ActionExecuterFocusable">
  <ACTIONS>
    <ACTION ACTIONNAME="NavBar_ButtonActivated">
      <USERDATA name="NavBar_ButtonActivated"><PROPERTIES/></USERDATA>
    </ACTION>
  </ACTIONS>
  <EVENTS>
    <EVENT /><EVENT /><EVENT /><EVENT /><EVENT /><EVENT /><EVENT /><EVENT />
    <EVENT /><EVENT /><EVENT /><EVENT /><EVENT />
    <EVENT ACTIONINDEX="0" />          <!-- index 13 = Activate -->
    <EVENT />
  </EVENTS>
</ACTIONEXECUTER>
```

`<ACTIONS>` is a flat pool. `<EVENTS>` is a **positional array**: the *n*-th `<EVENT>` is event id
*n*, and its `ACTIONINDEX` lists indices into the pool (space-separated for more than one). Bare
`ActionExecuter` has no `<EVENTS>` at all — its actions fire unconditionally when the keyframe is
reached. The empty groups are placeholders and must be present to keep the positions right; a decode
writes them as `ACTIONINDEX=""` and an omitted attribute means the same thing.

Array lengths, and the event ids the corpus actually populates:

| Executer | `<EVENT>` count | Populated ids |
|---|---|---|
| `ActionExecuterFocusable` | 15 | 4, 5, 6, 13, 14 |
| `ActionExecuterPage` | 16 | 3, 4, 6, 11 |
| `ActionExecuterPageInstance` | 16 | 3, 4, 11, 12, 13 |
| `ActionExecuterSlider` | 16 | 11, 12, 15 |
| `ActionExecuterListbox` | 17 | 3, 4, 11–15 |
| `ActionExecuterEditbox` | 17 | 3, 4, 11, 13, 15 |

Event ids, from `Util::GetType` groups 19–21 and confirmed by which actions sit at each index:

| id | Focusable family | `ActionExecuterPage` |
|---|---|---|
| 3 | `KeyDown` | `KeyDown` |
| 4 | `KeyUp` | `KeyUp` |
| 5 | `MouseDown` | `MouseDown` |
| 6 | `MouseUp` | `MouseUp` |
| 7–10 | `MouseDblClick`, `MouseMove`, `MouseEnter`, `MouseLeave` | same |
| 11 | `SetFocus` | `EnterPage` |
| 12 | `KillFocus` | `ExitPage` |
| 13 | **`Activate`** — the click handler | `Overlapped` |
| 14 | `Escape` | `UnOverlapped` |
| 15 | widget change: `ListBox` selection, `EditBox` text, `Slider` value | `Tick` |
| 16 | `ListBox`/`EditBox` only; unused in the corpus | — |

Ids 0–2 are never populated. The double meaning of 11–15 is exactly why the tag table carries two
groups both starting at 11: `ActionExecuterPageInstance` — despite the name — uses the *focus*
meanings (its id-11/12 actions come in matched `SetFocus`/`KillFocus` pairs, and id 13 carries
`DropDown_Activate`), because a `PageInstance` is a focusable.

### The action catalogue

`ACTIONNAME` is a raw `CRC32(name)` handed to `ActionServer::MakeAction`. The full registry — all 6
standard plus 81 game actions, where they are registered and how they dispatch — is on
[Interop with the Dunia engine](./engine-interop.md#actions-a-registry-compiled-into-the-binary).
**The registry is closed
and lives in the game binary** — you cannot define a new action from data. Names recovered by
hashing every ASCII run in `Dunia.dll`; the three engine ones first, then the game's:

| Action | Uses | Arguments (`UserData` keys) |
|---|---|---|
| `Stop` | 1,279 | `Area to stop` (link) |
| `GotoFrameIndex` | 128 | `Area` (link), `Frame` (u32) |
| `Continue` | 125 | `Area to start` (link) |
| `GotoKeyframe` | 8 | `Keyframe` (link) |
| `SoundEvent` | 45 | `Sound Event Name` (string), `Sound Event Type` (u32) |
| `KeyPressed` | 37 | `Trigger` (string) |
| `NavBar_ButtonActivated` | 63 | — |
| `Activated` / `Escaped` | 35 / 2 | — |
| `ListBox_SelectionChanged` | 38 | — |
| `MenuList_Item_Activated` / `_Escaped` / `_SelectItem` | 22 / 21 / 10 | `MenuList_SelectItem`: a link |
| `MenuList_Left_KeyDown` / `_Right_KeyDown` | 10 / 10 | `Trigger` |
| `Setting_Next_Value` / `_Previous_Value` / `_Activated` | 8 / 7 / 2 | `Trigger` |
| `CheckBox_Activate` | 10 | — |
| `Slider_ValueChanged` | 8 | — |
| `EditBox_TextChange` | 4 | — |
| `DropDown_Activate` / `_Item_Activate` / `_Item_Escape` | 28 / 3 / 3 | — |
| `ShowSelectionListNomad` | 24 | `Target list` (link), `Focus?` (bool) |
| `SetFocusListNomad` / `SetFocusNomad` | 11 / 5 | `Target list` / `Target element` (link), `Top?` (bool) |
| `PlayerPopup_Show` / `_Next` / `_Previous` | 5 / 2 / 2 | `Trigger` |
| `Vote` | 4 | `Trigger`, `Yes` (bool) |
| `IGE_*`, `Bazaar_*`, `Multi_*`, `Profile_*`, `CHAT_*` | 1–86 | screen-specific; see the corpus |

`Stop`, `Continue`, `GotoFrameIndex` and `GotoKeyframe` are the general-purpose ones — they take an
area or keyframe link and drive its timeline, and they are what makes data-only animation logic
possible. Everything else calls back into compiled game code for a specific screen and will do
nothing useful outside it.

## Enum values

Quoting [the `Util::GetType` tag table](../file-formats/mgb-field-names.md#utilgettypes-tag-table--the-named-value-sets).
A value the table does not name stays a bare number, which is how fields the engine masks
(`BLENDINGMODE` low byte, `MASKMODE` low 3 bits) keep their high bits.

| Field | Values |
|---|---|
| `INTERPOLATION` | `None`, `Linear`, `Square`, `Root`, `Sin`, `Circle`, `CircleDecel` |
| `ALIGNMENTX` | `LEFT`, `CENTER`, `RIGHT`, `JUSTIFY` |
| `ALIGNMENTY` | `TOP`, `CENTER`, `BOTTOM` |
| `MASKMODE` | `NOMASK`, `SETMASK`, `USEMASK`, `USEMASK_INVERTED` |
| `ADDRESSINGMODEU/V` | `Wrap`, `Mirror`, `Clamp`, `Border` |
| `BLENDINGMODE` | 27 modes; only six appear in shipped UI — `Normal`, `Add`, `Modulate`, `Multiply`, `Lighten 2X`, `Lighten 4X` (counts over the wider 500-package set are [on the field-names page](../file-formats/mgb-field-names.md#utilgettypes-tag-table--the-named-value-sets)) |
| `DIRECTION` (neighbours) | `0` UP, `1` DOWN, `2` LEFT, `3` RIGHT |

## Escape hatches

The XML is lossless by construction, so three fallback spellings exist and are always accepted:

- **`0x…` for a float** whose decimal spelling is not bit-exact (NaN payloads, denormals).
- **`base64:…` for a string** whose bytes cannot survive an XML attribute.
- **`#XXXXXXXX` for a name** that does not resolve.

An absent optional attribute and an empty one mean different things: omit it to mean "not present",
because present-with-zero writes different bytes.
