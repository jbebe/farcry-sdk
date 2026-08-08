---
sidebar_position: 4
---

# Patterns

:::info[Every recipe here is read off shipped packages]
These are the idioms the vanilla UI is actually built from, extracted by decoding all 50 packages
and looking at what repeats. Where a recipe names a specific area (`common #A671DE4C`), that is a
real area you can instance today. Claims about *why* something is done a certain way are marked
**(inferred)**.

The `ButtonInstance` and `ListBox` snippets below were dropped into
[`hello.mgb.xml`](pathname:///starters/hello.mgb.xml) and confirmed to encode and round-trip
byte-identically. Nothing on this page has been shown on screen in-game.
:::

## Static picture

The floor of everything: one element, one keyframe, geometry in the state.

```xml
<Element slot="74" type="Image" HIDDEN="false" ISDUPLICATABLE="true" MASKMODE="NOMASK">
  <USERDATA name="i_backdrop"><PROPERTIES /></USERDATA>
  <KEYFRAMES>
    <Keyframe name="kf_backdrop" IDX="0" INTERPOLATION="None">
      <ImageState INTERPOLATIONFLAGS="0" STATECOLOR="FFFFFFFF" ROTATION="0"
                  ORIGIN.x="0" ORIGIN.y="0" LEFT="240" RIGHT="1040" TOP="180" BOTTOM="620"
                  SHADOWCOLOR="00000000" SHADOWOFFSETX="0" SHADOWOFFSETY="0"
                  TILING.x="1" TILING.y="1" OFFSET.x="0" OFFSET.y="0"
                  FLIPHORIZONTAL="false" FLIPVERTICAL="false" ACTUALSIZE="false"
                  COLOR1="FFFFFFFF" COLOR2="FFFFFFFF" COLOR3="FFFFFFFF" COLOR4="FFFFFFFF" />
    </Keyframe>
  </KEYFRAMES>
  <Image BLENDINGMODE="Normal" ALPHABLENDFIRST="false"
         ADDRESSINGMODEU="Clamp" ADDRESSINGMODEV="Clamp">
    <MATERIALLINK present="true" id="notebook" PACKAGE="" />
  </Image>
</Element>
```

`COLOR1`–`COLOR4` are the quad's corner colours: set them differently and the image is tinted with a
gradient, for free, with no extra texture.

## Fade in and hold

Two keyframes and a `Stop`. `STATECOLOR`'s alpha channel is Magma's opacity — it multiplies the
whole element, image and text alike.

```xml
<KEYFRAMES>
  <Keyframe name="kf_in" IDX="0" INTERPOLATION="Linear">
    <ImageState STATECOLOR="00FFFFFF" … />        <!-- transparent -->
  </Keyframe>
  <Keyframe name="kf_hold" IDX="10" INTERPOLATION="None">
    <ACTIONEXECUTER slot="133" type="ActionExecuter">
      <ACTIONS>
        <ACTION ACTIONNAME="Stop">
          <USERDATA name="Stop">
            <PROPERTIES>
              <PROPERTY key="Area to stop" type="17">
                <LINK slot="101" LASTOBJECTTYPE="Page" IDS="hello HELLO_PAGE" />
              </PROPERTY>
            </PROPERTIES>
          </USERDATA>
        </ACTION>
      </ACTIONS>
    </ACTIONEXECUTER>
    <ImageState STATECOLOR="FFFFFFFF" … />        <!-- opaque -->
  </Keyframe>
</KEYFRAMES>
```

Of the corpus's 10,949 state colours, `FFFFFFFF` accounts for 8,380 and `00FFFFFF` for 718 — the two
ends of exactly this fade. The rest repeat the pattern as matched pairs at the same RGB:
`FFA5BDC5`/`00A5BDC5` (183/150), `FFC0C0C0`/`00C0C0C0` (95/108).

`Stop` targets an **area**, not an element: it halts that area's playhead, freezing every element in
it. `Continue` resumes it, `GotoFrameIndex` jumps it to a frame, `GotoKeyframe` to a named keyframe.
Between them, an area's timeline is a state machine you drive entirely from data.

## A button is a six-state timeline

This is the single most important idiom in Magma, and it is why `Button` is an *area* type rather
than a widget. A `Button` area declares `TIMINGS` — six frame indices, one per visual state — and
the engine parks the playhead at the frame for whatever state the button is in.

```xml
<Area slot="99" type="Button" FRAMERATE="30" CURRENTFRAME="0"
      STATICBOX="9 191 7 31" TIMINGS="0 10 20 30 40 50">
```

| `TIMINGS` slot | State |
|---|---|
| 0 | `ENABLED` — resting |
| 1 | `PRESSED` |
| 2 | `DISABLED` |
| 3 | `SELECTED` — keyboard/pad focus |
| 4 | `OVER` — mouse hover |
| 5 | `OVER_SELECTED` |

**Every `Button` in the corpus uses `0 10 20 30 40 50`** (one exception: `hud_mp #6E25AA20` uses
`0 5 10 15 20 25`), and every one authors a keyframe at exactly those frames. So each state gets a
10-frame block, and the frames *between* two block starts are that state's own micro-animation — the
standard menu button `common #A671DE4C` has extra keyframes at 11 and 12, a one-frame flash on
press.

The whole of `common #A671DE4C`, the button every options screen instances:

```
STATICBOX 9 191 7 31, FRAMERATE 30, TIMINGS 0 10 20 30 40 50
  Placeholder  #47CC8C92   keyframes at 0, 11, 20, 30, 40, 50   (origin marker)
  Image        #7A8A6532   keyframes at 0, 10, 11, 20, 30, 40, 50, material halftone_title_bar
  Text         #49D78B9C   keyframes at 0, 20, 30 — STATECOLOR FF9EB9C0 → 469EB9C0 → FF9EB9C0
```

The text element only bothers with three keyframes: full opacity at rest, dimmed at frame 20
(`DISABLED`), full again at 30 (`SELECTED`). Everything in between interpolates. **An element does
not need a keyframe at every state boundary** — only where its appearance changes.

`CheckBox` is the same mechanism with twelve slots: the six unchecked states at frames
`0 10 20 30 40 50`, then the six checked ones at `60 70 80 90 100 110`. Both corpus checkboxes
(`options`/`mp_menus` `#0FE88E26`) use exactly that.

You then place the button with a `ButtonInstance`, and hang the click handler on the *instance*, not
the button area:

```xml
<Element slot="78" type="ButtonInstance" HIDDEN="false" ISDUPLICATABLE="true" MASKMODE="NOMASK">
  <USERDATA name="b_prompt1"><PROPERTIES /></USERDATA>
  <ACTIONEXECUTER slot="138" type="ActionExecuterFocusable">
    <ACTIONS>
      <ACTION ACTIONNAME="NavBar_ButtonActivated">
        <USERDATA name="NavBar_ButtonActivated"><PROPERTIES /></USERDATA>
      </ACTION>
    </ACTIONS>
    <EVENTS>…13 empty EVENTs…<EVENT ACTIONINDEX="0" /><EVENT /></EVENTS>
  </ACTIONEXECUTER>
  <KEYFRAMES>
    <Keyframe name="kf" IDX="0" INTERPOLATION="None">
      <ScaleState STATECOLOR="FFFFFFFF" POSITION.x="46" POSITION.y="699" SCALEX="1" SCALEY="1" … />
    </Keyframe>
  </KEYFRAMES>
  <ButtonInstance LABEL="" INDEXOFFSET="0">
    <MATERIALLINK present="false" />
    <LINK slot="150" TIMING="TickTimingStrategy" PACKAGE="common"
          AREA="#A671DE4C" ISUSINGDUPLICATEDAREA="true" />
  </ButtonInstance>
  <FOCUSABLE INPUTFILTER="255"><NEIGHBORS /></FOCUSABLE>
</Element>
```

`ISUSINGDUPLICATEDAREA="true"` matters here: five prompt buttons on one strip all instance the same
`Button` area, and each needs its own playhead so they can be in different states at once.

## A list of rows

`ListBox` + a `Button` row template. The list duplicates the template once per item and drives each
copy's state; you author one row, not twenty.

```xml
<ListBox HEADERFOOTERPOS="0" AUTOCENTER="true" WRAPAROUND="false" SLIDESELITEM="false"
         flag4="false" BUTTONCOUNT="20" ITEMSPACING="0">
  <HEADERLINK slot="150" TIMING="TickTimingStrategy" PACKAGE="common"
              AREA="#CD72056E" ISUSINGDUPLICATEDAREA="true" />
</ListBox>
```

That is the real options nav list, `common #36150990`. Three things to copy:

- **The first link is the row template** despite being named `HEADERLINK` — see
  [the warning in the reference](./reference.md#listbox-names-provisional).
- **`ISUSINGDUPLICATEDAREA="true"` is mandatory** on it. Every row template in the corpus sets it;
  without a private clone per row, every row would share one playhead and highlight together
  **(inferred)**.
- **`BUTTONCOUNT` is the viewport, not the item count.** 20 here. The list scrolls a longer item set
  through those 20 row widgets, clamped at both ends rather than wrapping (confirmed live by
  appending 30 rows to one). Set `WRAPAROUND="true"` for wrap — 12 corpus lists do.

A scrollbar is optional and purely decorative: add a sibling `Slider` element and name it in
`SLIDERLINK`. Only 12 of 101 corpus lists have one; `COMMON_SAVELOADPAGE` is the example.

Selection is handled by an `ActionExecuterListbox` on the same element, with `ListBox_SelectionChanged`
at event 15 and `MenuList_Item_Activated` at event 13.

## A value spinner (`‹ Value ›`)

The options-screen "Difficulty: Normal ‹ ›" control is a `ListBox` with a viewport of **one**, plus
two arrow buttons. `common #652FD37C`, instanced 86 times:

```
Page #652FD37C
  Placeholder  #47CC8C92
  ListBox      #D240E092   BUTTONCOUNT=1  ITEMSPACING=1
     link 1 -> common #1A823B00   Button 270×25, one Text  (the value label)
     link 2 -> common #D0466EF1   Button 35×25, slider_arrow  (left)
     link 3 -> common #3DB37F7D   Button 35×25, slider_arrow  (right)
```

Every list in the corpus that sets all three links is this shape — `BUTTONCOUNT="1"`,
`ITEMSPACING="1"`. That is the tell: **one visible item plus two decorations is a spinner**;
`BUTTONCOUNT > 1` with only link 1 is a row list.

## Composing out of `common.mgb`

`common.mgb` is loaded whenever any menu is up, and 624 of the corpus's 1,273 area links point into
it. A new screen usually needs **no materials, fonts or textures of its own** — only instances.
The pieces worth knowing, with their instance counts across the corpus:

| Area | Type | ×  | What it is |
|---|---|---|---|
| `#EB3BB25F` | Page | 120 | Full-screen sky/cloud backdrop — four `clouds` images plus a vignette `RectShape` |
| `#A5D8B868` | Area | 86 | Nine `circle_glow` images — the ambient light blooms behind a menu |
| `#652FD37C` | Page | 86 | The value spinner above |
| `#E58F0F6C` | Page | 81 | `p_prompts_navbar` — the bottom prompt strip, five `ButtonInstance`s named `b_prompt1`…`b_prompt4` |
| `#5B36589B` | Area | 80 | The notebook page-flip animation (12 `notebook_flip_*` images, 47 keyframes) |
| `#0CEBAD43` | Area | — | A shorter page-flip variant |
| `#36150990` | Page | 15 | The 20-row nav list **plus** `a_title_bar` → `#AFDE26A1` (the `t_page_title` text) |
| `#9EA91A65`, `#77CABF50` | Page | 19 / 4 | Smaller list panels with a `brush_stroke` backing |
| `#A671DE4C`, `#7625EF31`, `#B5848FD5` | Button | 28 / 23 / — | Standard menu buttons, 191×31, `halftone_title_bar` |
| `#CD72056E` | Button | 23 | The wider list-row button, 339×37, `red_line` underline |
| `#1A823B00` | Button | — | Text-only 270×25 button — the spinner's value label |
| `#D0466EF1`, `#3DB37F7D` | Button | — | Left/right `slider_arrow` buttons |
| `#2F024CF2` | Button | — | The wide 700×41 prompt button |
| `#6155790C` | Page | — | `COMMON_SAVELOADPAGE` — a complete list screen with slider, thumbnails and chrome; the best full-page reference |
| `Cursor` | Cursor | 15 | The mouse cursor area |

To use one: an `AreaInstance`/`PageInstance`/`ButtonInstance` whose `LINK` has
`PACKAGE="common"` and `AREA="#…"`. Positioning is the instance's `ScaleState` `POSITION.x/y`.

`common.mgb` is loaded by the *menu*, not by you. A HUD-time package cannot assume it — `hud.mgb`
carries its own equivalents.

## Layering and draw order

Two mechanisms, both simple:

- **Within an area**, elements draw in document order — later siblings paint on top **(inferred from
  the mask pattern below, which only works that way)**.
- **Between areas**, the `LAYER` `UserData` property on the area. Values in the corpus: `10` (70
  areas), `50` (41), `126` (30), `40` (6), and a handful of 0–9 and 120–127. Higher is nearer the
  front **(inferred)**. Menu pages sit at `10`; HUD overlays and message boxes climb to `126`.

```xml
<USERDATA name="MY_PAGE">
  <PROPERTIES><PROPERTY key="LAYER" type="2" value="10" /></PROPERTIES>
</USERDATA>
```

## Masking a region

A `RectShape` with `MASKMODE="SETMASK"` defines a stencil; the siblings *after* it with
`MASKMODE="USEMASK"` are clipped to it. `common #0CEBAD43`:

```
Placeholder  NOMASK
RectShape    SETMASK     <-- the window
Image        USEMASK     <-- clipped
Image        NOMASK      <-- not clipped
…
```

25 of the corpus's 26 `SETMASK` elements are `RectShape`s, and the mask is always immediately
followed by its users. Animate the mask rectangle's `LEFT`/`RIGHT` across keyframes and you get a
wipe reveal — which is exactly how the notebook page-flip and the loading-bar fills work.

`USEMASK_INVERTED` exists in the enum and is used nowhere.

## Focus and navigation

Three layers, and most screens only use the first:

1. **`ListBox` internal navigation.** A list moves its own selection with up/down. 592 of the 746
   focusable elements declare no neighbours at all, because they are lists or single controls.
2. **`<NEIGHBORS>` on a focusable element** — an explicit directed edge per direction:

   ```xml
   <FOCUSABLE INPUTFILTER="255">
     <NEIGHBORS>
       <NEIGHBOR CONTROLLER="255" DIRECTION="0" ID="b_previous" />
       <NEIGHBOR CONTROLLER="255" DIRECTION="1" ID="b_next" />
     </NEIGHBORS>
   </FOCUSABLE>
   ```

   Corpus directions: DOWN 106, UP 104, RIGHT 53, LEFT 50 — pairs, as you would expect. Elements
   with neighbours have 2 (108), 3 (24), 1 (21) or 4 (1).
3. **`<DEFAULT_ELEMENTS>` on the page** — who has focus when the page opens.
   `<DEFAULT_ELEMENT CONTROLLER="255" ID="p_menu_nav" />`. `PageInstance` additionally has
   `<DEFAULTFOCUSES>`, entries of `DEFAULT_FROM_DIRECTION` / `DEFAULT_FROM_DIRECTION_2` / `DEFAULTFOCUS`
   — which child takes focus depending on the direction focus arrived from. Unused in the corpus.

## Text that localises

Put the OASIS key in `STRING` and the engine resolves it at draw time:

```xml
<Text useStringTable="false" STRING="OPTIONS_DISPLAY_ASPECT_RATIO" … />
```

`oasisstrings.xml` is a flat `<stringtable>` of `<section name="…">` / `<string enum="KEY" value="…"/>`
— 11,399 keys in the English set. 290 of the 810 shipped `Text` widgets carry one. The rest are
placeholders in templates (`Wwwwwwwwwww`, sized to reserve the widest plausible string) whose real
text native code writes at runtime; if you are authoring a screen driven by native code, copy that
convention rather than leaving the string empty.

The `<STRINGTABLE>` element is a *different* mechanism (`useStringTable="true"` plus
`TABLEID`/`RESOURCEID`) and **no shipped package uses it**.

## Aspect variants

The UI ships twice: `ui\localized\pc\…` at `1024×768` with `DISPLAYOFFSET 32 24`, and
`ui\localized\pcwidescreen\…` at `1280×800` with `DISPLAYOFFSET 160 40`. Geometry differs between
them — the options nav sits at x=83 in the 4:3 set and x=74 in the widescreen one. There is no
scaling layer that reconciles them: **if you author one, you author both**, or your screen is
misaligned for half of players. `CMagmaLocalizationUtil::GetLocalizedPackageName` picks the folder;
FCSE reads the same flag to pick between two embedded variants.

## Binding a page to native code

If your package's page is to be driven by a compiled `CUIPageBase` subclass, four things must line
up. Full trail in [the menu system page](../engine-internals/magma-menu-system.md).

1. **Export the page name** in the `GenericObjectTable`, mapping it to a `FullLink` at your `Page`
   area. `CUIPageBase::Init` hashes the page-name string and resolves it through
   `GenericObjectServer::FindGenericObject`. Registering more than one key for the same page is
   fine and is what FCSE does (`FCSE_PAGE` plus `MAINMENU_FCSE_PAGE_PC`).
2. **Provide the widgets `FetchMagmaElements` looks up by hardcoded name**: an element named
   `p_menu_nav` containing a `ListBox` named `l_menu_nav_list`, and `a_title_bar` containing a `Text`
   named `t_page_title`. Instancing `common #36150990` supplies all four at once. Miss them and the
   page renders empty rather than crashing — `AddButton` returns −1 when the list is null.
3. **Declare each settings row's value control** as a `UserData` `FullLink` property on the page
   area, named what the native call passes: `SETTING_LABEL_LIST` for the shared label list, then one
   key per slot. The stock Game tab declares eight (`SETTING_MOUSE_SMOOTH`, `SETTING_DIFFICULTY`, …);
   FCSE declares twenty (`FCSE_SLOT_01`…`FCSE_SLOT_20`), each linking to a `#652FD37C` instance.
4. **The title comes from the page object, not the layout** — `t_page_title` lives in the shared
   `common.mgb` title bar, so `CMenuPage::SetTitle` writes it; you cannot bake a title into your
   `.mgb`.

The row ceiling is the layout's: `#36150990`'s list declares a 20-row viewport, and absolutely
positioned value controls do not scroll with it, so past 20 rows the labels slide out from under
their controls.
