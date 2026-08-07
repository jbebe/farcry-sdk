---
sidebar_position: 12
---

# `.mgb` field names — the `LoadVisitor` join

:::info[Verified via reverse engineering]
Companion to [the `.mgb` format page](./mgb.md), which carries the wire layout. This page is the
per-field working record: which object offset each wire field lands on, and therefore which authored
name belongs to it.
:::

`magma::LoadVisitor` — the engine's XML loader for Magma's `.mgm` source format — is a complete 1:1
mirror of `BinaryLoadVisitor`: ~55 `ReadX` methods, one per class, parsing *named* XML elements into
the *same object offsets* the binary visitor writes. That makes field names recoverable rather than
guessable:

- `BinaryLoadVisitor::VisitX` gives *(wire order, width) → object offset*
- `LoadVisitor::ReadX` gives *XML element name → object offset*
- joining on the object offset names each wire field

Addresses are `FarCry2_server`.

Fields are listed in **wire order**. "off" is the object offset both visitors agree on.

---

## State hierarchy

### `State` — `ReadState` @ `0x0a066400`
| # | wire | off | name | notes |
|---|---|---|---|---|
| 0 | u32 | `+0x08` | `INTERPOLATIONFLAGS` | `%u`; defaults to `ALL_INTERPOLATION_FLAGS` when absent |
| 1 | u32 | `+0x10` | `STATECOLOR` | `%d %d %d %d` → packed ARGB |

**Correction**: earlier passes called these `start`/`end` (a time range). They are not.

**Correction**: an earlier pass called every colour word here "packed RGBA". It is **ARGB** —
`0xAARRGGBB`, alpha in the high byte, authored `A R G B`. `ReadState` packs the four `%d` components
first-component-highest (`c1 << 24 | c2 << 16 | c3 << 8 | c4`), which fixes the order but not which
one is alpha; the shipped data fixes that. Across the 500 vanilla packages the two commonest state
colours are `0xFFFFFFFF` (80,240 uses) and `0x00FFFFFF` (7,010), and every other colour in use
repeats the pattern — `0xFFA5BDC5`/`0x00A5BDC5`, `0xFFC0C0C0`/`0x00C0C0C0`, `0xFF9CB1B8`/`0x009CB1B8`
— matched pairs of the same low three bytes at high byte `FF` and `00`, i.e. the two ends of a fade.
Read as RGBA those pairs would be "opaque white" and "opaque cyan".

### `RotationState` — `ReadRotationState` @ `0x0a066980` (base `State`)
| # | wire | off | name |
|---|---|---|---|
| 2 | f32 | `+0x18` | `ROTATION` (`atof`) |
| 3 | u16 | `+0x1c` | `ORIGIN` x (`%d %d`) |
| 4 | u16 | `+0x1e` | `ORIGIN` y |

### `PosState` — `ReadPosState` @ `0x0a066050` (base `RotationState`)
| # | wire | off | name |
|---|---|---|---|
| 5 | u16 | `+0x24` | `POSITION` x |
| 6 | u16 | `+0x26` | `POSITION` y |

### `ScaleState` — `ReadScaleState` @ `0x0a0654c0` (base `PosState`)
| # | wire | off | name |
|---|---|---|---|
| 7 | f32 | `+0x2c` | `SCALEX` |
| 8 | f32 | `+0x30` | `SCALEY` |

### `RectState` — `ReadRectState` @ `0x0a065130` (base `RotationState`)
| # | wire | off | name |
|---|---|---|---|
| 5 | u16 | `+0x24` | `LEFT` |
| 6 | u16 | `+0x26` | `RIGHT` |
| 7 | u16 | `+0x28` | `TOP` |
| 8 | u16 | `+0x2a` | `BOTTOM` |

Note the order is **left, right, top, bottom** — not l/t/r/b. `PosState` and `RectState` are siblings
that reuse the same `+0x24`/`+0x26` storage (`POSITION` x/y == `LEFT`/`RIGHT`).

### `TextBaseState` — `ReadTextBaseState` @ `0x0a064fb0` (base `RectState`)
| # | wire | off | name |
|---|---|---|---|
| 9 | f32 | `+0x30` | `OFFSETY` (`%f`) |
| 10 | u16 | `+0x34` | `ABSOFFSETY` (`%d`) |

### `TextState` — `ReadTextState` @ `0x0a06eca0` (base `TextBaseState`)
| # | wire | off | name |
|---|---|---|---|
| 11 | u32 | `+0x44` | `SHADOWCOLOR` |
| 12 | u16 | `+0x40` | `HEIGHT` (stored as float) |
| 13 | u8 | `+0x48` | `SHADOWOFFSETX` |
| 14 | u8 | `+0x49` | `SHADOWOFFSETY` |
| 15 | u16 | `+0x3c` | `LEADING` |
| 16 | u16 | `+0x3e` | `TRACKING` |

`TEXTCOLOR` (alias `COLOR`) writes `+0x10`, i.e. it is the inherited `STATECOLOR` under another
name in the XML — not a separate wire field.

### `ImageState` — `ReadImageState` @ `0x0a06e490` (base `RectState`)
| # | wire | off | name |
|---|---|---|---|
| 9 | u32 | `+0x54` | `SHADOWCOLOR` |
| 10 | u8 | `+0x58` | `SHADOWOFFSETX` |
| 11 | u8 | `+0x59` | `SHADOWOFFSETY` |
| 12 | f32 | `+0x38` | `TILING` x |
| 13 | f32 | `+0x3c` | `TILING` y |
| 14 | f32 | `+0x30` | `OFFSET` x |
| 15 | f32 | `+0x34` | `OFFSET` y |
| 16 | bool | `+0x40` b0 | `FLIPHORIZONTAL` |
| 17 | bool | `+0x40` b1 | `FLIPVERTICAL` |
| 18 | bool | `+0x40` b2 | `ACTUALSIZE` |
| 19-22 | u32 ×4 | `+0x44`..`+0x50` | `COLOR1`..`COLOR4` (packed ARGB) |

When `COLORn` (n>1) is absent in XML the loader copies `COLOR1` — a gradient quad's corner colours.

### `RectShapeState` — `ReadRectShapeState` @ `0x0a06cff0` (base `RectState`)
| # | wire | off | name |
|---|---|---|---|
| 9 | u8 | `+0x30` | `OUTLINEWEIGHT` |
| 10 | u32 | `+0x34` | `OUTLINECOLOR` |
| 11-14 | u32 ×4 | `+0x38`..`+0x44` | `FILLCOLOR1`..`FILLCOLOR4` |
| 15 | u32 | `+0x48` | `SHADOWCOLOR` |
| 16 | u8 | `+0x4c` | `SHADOWOFFSETX` |
| 17 | u8 | `+0x4d` | `SHADOWOFFSETY` |

---

## Tree structure

### `Keyframe` — `ReadKeyframe` @ `0x0a06c5a0`
| # | wire | off | name |
|---|---|---|---|
| — | u32 | — | `NamedObject` name hash |
| — | — | — | `ActionCaller` |
| 0 | u32 | `+0x18` | `IDX` (stored u16) — the frame index |
| 1 | u32 | `+0x1c` | `INTERPOLATION` — the easing curve, a plain [tag-group 0](#utilgettypes-tag-table--the-named-value-sets) value (`ReadKeyframe` calls `Util::GetType(0, …)` at `+0xec`) |

Then the concrete `State`, chosen by `Factory::MakeState` from the owning widget's class.

### `Element` — `ReadElement` @ `0x0a06bab0`
| # | wire | off | name |
|---|---|---|---|
| — | — | — | `UserData`, then `ActionCaller` |
| 0 | bool | via `SetVisible` | `HIDDEN` (inverted into visibility) |
| 1 | bool | `+0x2e` b0 | `ISDUPLICATABLE` |
| 2 | u32 | `+0x2d` b4-6 | `MASKMODE` (low 3 bits; XML resolves a name via `Util::GetType(0xb, …)`) |
| 3 | u32 | — | keyframe count (`KEYFRAMES`/`COUNT` in XML) |

Then the keyframes, then `WIDGET` — the widget's own body.

### `Area` — `ReadArea` @ `0x0a067b50`
| # | wire | off | name |
|---|---|---|---|
| — | — | — | `UserData`, then `ActionCaller` |
| 0 | u32 | `+0x18` | `FRAMERATE` (engine stores `1000 / framerate`) |
| 1 | u32 | — | `CURRENTFRAME` |
| 2 | u32 | — | element count (`CHILDREN`/`COUNT` in XML) |
| 3-6 | u16 ×4 | — | `STATICBOX` (`%d %d %d %d` → `Area::SetStaticBox`) |

### `AreaLink` — `ReadAreaLink` @ `0x0a06c8d0`
| # | wire | off | name |
|---|---|---|---|
| 0 | u8 | — | `TIMING` — timing-strategy type slot |
| 1 | u32 | `+0x14`+4 | `PACKAGE` |
| 2 | bool | — | (gate for `AREA`) |
| 3 | u32 | `+0x14`+8 | `AREA` |
| 4 | bool | `+0x18` b0 | `ISUSINGDUPLICATEDAREA` |

---

### `Focusable` — `ReadFocusable` @ `0x0a06a730` (base `Element`)
| # | wire | name |
|---|---|---|
| 0 | u32 | neighbour count (`NEIGHBORS`/`COUNT`) |
| — | u8 | `CONTROLLER` (per `NEIGHBOR`; default 8) |
| — | u8 | `DIRECTION` (per `NEIGHBOR`; default 4) |
| — | u32 | `ID` (per `NEIGHBOR`) |
| 1 | bool | `INPUTFILTER` → `Focusable::SetInputController` |

`PageFocusable`, `Checkable`, `Radioable` are pure forwards to this.

---

## Widgets

### `RectShape` — `ReadRectShape` @ `0x0a06f820`
| # | wire | off | name |
|---|---|---|---|
| 0 | bool | `+0x19` b0 | `ISOUTLINED` |
| 1 | bool | `+0x19` b1 | `ISFILLED` |
| 2 | u32 | `+0x18` | `BLENDINGMODE` (`Util::GetType(9, …)`) |

### `Image` — `ReadImage` @ `0x0a072490`
| # | wire | off | name |
|---|---|---|---|
| 0 | resource ref | `+0x1c` | `MATERIALLINK` (legacy alias `MATERIAL`) |
| 1 | u32 | `+0x18` | `BLENDINGMODE` |
| 2 | bool | `+0x1a` b0 | `ALPHABLENDFIRST` |
| 3 | u32 | `+0x19` lo nibble | `ADDRESSINGMODEU` (`Util::GetType(10, …)`) |
| 4 | u32 | `+0x19` hi nibble | `ADDRESSINGMODEV` |

### `TextBase` — `ReadTextBase` @ `0x0a06ad10`
| # | wire | off | name |
|---|---|---|---|
| 0 | bool | — | use-string-table gate |
| 1a | u32 | — | `TABLEID` (gate = 1) |
| 1b | u32 | — | `RESOURCEID` (gate = 1) |
| 1c | u32 + UTF-16 | `+0x18` | `STRING` (gate = 0) |
| 2 | u32 | `+0x1c` | `ALIGNMENTX` (alias `ALIGNMENT`; `Util::GetType(1, …)`) |
| 3 | u32 | `+0x20` | `ALIGNMENTY` (`Util::GetType(2, …)`) |
| 4 | bool | `+0x38` b0 | `WRAPPING` (alias `WRAP`) |
| 5 | bool | `+0x38` b1 | `CLIPPING` |
| 6 | bool | `+0x38` b2 | `ELLIPSIS` |
| 7 | bool | `+0x38` b3 | `AUTOSIZED` |
| 8 | bool | — | has-slider-link gate |
| 9 | u32 | — | `SLIDERLINK` |

### `Text` — `ReadText` @ `0x0a0713f0` (base `TextBase`)
| # | wire | off | name |
|---|---|---|---|
| 10 | resource ref | `+0x40` | font family (`LoadFontFamily`) |
| 11 | bool | `+0x48` b0 | `BOLD` |
| 12 | bool | `+0x48` b2 | `ITALICS` |
| 13 | bool | `+0x48` b1 | `UNDERLINED` |
| 14 | u32 | `+0x50` | `BLENDINGMODE` |
| 15 | bool | `+0x51` b0 | `ALPHABLENDFIRST` |

### `AreaInstance` — `ReadAreaInstance` @ `0x0a070910`
| # | wire | off | name |
|---|---|---|---|
| 0 | u32 + UTF-16 | `+0x18` | `LABEL` (the referenced area's name) |
| 1 | resource ref | `+0x38` | `MATERIALLINK` |
| 2 | bool | — | has-`LINK` gate |
| 3 | `AreaLink` | `+0x1c` | `LINK` |
| 4 | u32 | — | `INDEXOFFSET` |

`AutonomousAreaInstance` / `ButtonInstance` / `CheckBoxInstance` / `RadioButtonInstance` add nothing.

### `Window` — `ReadWindow` @ `0x0a06c1d0`
| # | wire | off | name |
|---|---|---|---|
| 0 | bool | `+0x100` | `SINGLECORNERMATERIAL` |
| 1 | bool | `+0x101` | `SINGLEEDGEMATERIAL` |
| 2-10 | section ×9 | — | see below |

Section index → XML name, matching the binary's own 0–8 order exactly (stretchable ones marked *):

| idx | name | | idx | name |
|---|---|---|---|---|
| 0 | `FILL` * | | 5 | `TOP_EDGE` * |
| 1 | `TOP_LEFT_CORNER` | | 6 | `LEFT_EDGE` * |
| 2 | `TOP_RIGHT_CORNER` | | 7 | `RIGHT_EDGE` * |
| 3 | `BOTTOM_LEFT_CORNER` | | 8 | `BOTTOM_EDGE` * |
| 4 | `BOTTOM_RIGHT_CORNER` | | | |

### `WindowSection` — setter-name derived (`ReadWindowSection` @ `0x0a066af0`)
`MATERIALLINK` → `BLENDINGMODE` (`SetBlendingMode`) → `ALPHABLENDFIRST` (`SetAlphaBlendFirst`) →
`FLIPHORIZONTAL` (`SetFlipHorizontal`) → `FLIPVERTICAL` (`SetFlipVertical`) → `ROTATED`
(`SetRotated`). `StretchableWindowSection` appends `STRETCHMODE` (`SetStretchMode`).

---

## Naming still inferred, not offset-verified

For these the XML vocabulary and the binary field list are both known and the counts line up, but
the per-field join was not run. Names are from the vocabulary plus the binary's own setter calls,
which are unambiguous in most cases (`Slider::SetRange`, `Page::SetGlobalSelectionMode`,
`PageInstance::AddDefaultFocusTag`, `EditBox::SetPasswordChar`, `Material::SetRegion`,
`ListBox::UpdateMetrics`). Tighten by decompiling the matching `ReadX` if a label looks wrong:

`Page`, `PageInstance`, `Button`, `CheckBox`, `EditBox`, `ListBox`, `Slider`, `UserData`,
`Material`, `FullLink`, `ActionCaller`, `ActionExecuter`, `StringResourceExternalId`, `Package`,
`StringTable`, `StringResource`, `GenericObject`, `GenericObjectTable`, `FontFamily`.

Vocabulary for them (from the string tables):

| Class | Elements |
|---|---|
| `ActionCaller` | `ACTIONEXECUTER` |
| `ActionExecuter` | `ACTIONNAME`, `ACTIONSNB` |
| `ActionExecuterEvent` | `ACTIONINDEX` |
| `AreaInstance` | `INDEXOFFSET`, `LABEL`, `MATERIALLINK`, `SYNCHRONIZED` |
| `Button` | `STATES`, `TIMINGS` |
| `CheckBox` | `TIMINGS` |
| `Cursor` | `HOTSPOT` (`%hd %hd`) |
| `EditBox` | `CURSORLINK`, `FIELDLINK` |
| `Focusable` | `CONTROLLER`, `INPUTFILTER`, `NEIGHBOR`, `NEIGHBORS` |
| `FullLink` | `LASTOBJECTTYPE` |
| `GenericObjectTable` | `GENERICOBJECT` |
| `Image` | `ADDRESSINGMODEU`, `ADDRESSINGMODEV`, `ALPHABLENDFIRST`, `BLENDINGMODE`, `MATERIALLINK` |
| `ListBox` | `AUTOCENTER`, `BUTTONCOUNT`, `FOOTERLINK`, `HEADERFOOTERPOS`, `HEADERLINK`, `ITEMLINK`, `ITEMSPACING`, `SLIDERLINK`, `SLIDESELITEM`, `VERTICALSPACING`, `WRAPAROUND` |
| `Material` | `STAGE` |
| `Package` | `PAGESIZE`, `DISPLAYOFFSET`, `MATERIALS`, `FONTS`, `FONTSUBST`, `FONTFAMILIES`, `FONTFAMILY`, `CHILDREN`, `STRINGTABLE`, `GENERICOBJECTTABLE`, `LASTACTIVEAREA`, `DEFAULTMATERIAL`, `REPLACES` |
| `Page` | `CONTROLLER`, `DEFAULT_ELEMENT`, `SINGLE_GLOBAL_SELECTION` |
| `PageInstance` | `CONTROLLER`, `DEFAULTFOCUS`, `DEFAULT_FROM_DIRECTION`, `DEFAULT_FROM_DIRECTION_2` |
| `RectShape` | `BLENDINGMODE`, `ISFILLED`, `ISOUTLINED` |
| `Slider` | `FOOTERLINK`, `HANDLELINK`, `HEADERLINK`, `KNOBLINK`, `ORIENTATION`, `RANGEMAX`, `RANGEMIN`, `SURFACELINK`, `TRACKLINK` |
| `StretchableWindowSection` | `STRETCHMODE` |
| `StringResourceExternalId` | `RESOURCEID`, `TABLEID` |
| `StringTable` | `STRINGRESOURCE` |
| `Text` | `ALIGNMENT`, `ALPHABLENDFIRST`, `AUTOSIZED`, `BLENDINGMODE`, `CLIPPING`, `ITALICS`, `SLIDERLINK`, `UNDERLINED`, `WRAPPING` |
| `TextBase` | `ALIGNMENT`, `ALIGNMENTX`, `ALIGNMENTY`, `AUTOSIZED`, `CLIPPING`, `RESOURCEID`, `SLIDERLINK`, `TABLEID`, `WRAPPING` |
| `UserData` | `USERDATANB` |
| `Window` | `TOP_LEFT_CORNER`, `TOP_EDGE`, `TOP_RIGHT_CORNER`, `LEFT_EDGE`, `RIGHT_EDGE`, `BOTTOM_LEFT_CORNER`, `BOTTOM_EDGE`, `BOTTOM_RIGHT_CORNER`, `SINGLECORNERMATERIAL`, `SINGLEEDGEMATERIAL` |
| `WindowSection` | `ALPHABLENDFIRST`, `BLENDINGMODE`, `FLIPHORIZONTAL`, `FLIPVERTICAL`, `MATERIALLINK` |

---

## `Util::GetType`'s tag table — the named value sets

Several fields above are authored in XML as a *name*, not a number: `ReadImage` parses
`BLENDINGMODE` with `Util::GetType(9, text)`, `ADDRESSINGMODEU`/`V` with group 10, `ReadTextBase`
resolves `ALIGNMENTX`/`ALIGNMENTY` with groups 1 and 2, and so on.

`Util::GetType` (`0x0a03ba50`) and its inverse `Util::GetTag` (`0x0a03b831`) are a pair of linear
scans over one static table:

```c
entries = *(Entry**)(ms_tagTable + group * 8 + 4);   // Entry { u32 value; const char* name; }
count   = *(int*)   (ms_tagTable + group * 8);
```

No code path holds any of these names as a literal, so they were read out of `ms_tagTable` at
`0x0a34ba80` in the debug `FarCry2_server` ELF (resolving the vaddr through the PT_LOAD headers).
Values are as stored — note groups 13 and 19-21 are **not** 0-based.

| # | Used by | Values |
|---|---|---|
| 0 | keyframe interpolation | 0 `None`, 1 `Linear`, 2 `Square`, 3 `Root`, 4 `Sin`, 5 `Circle`, 6 `CircleDecel` |
| 1 | `ALIGNMENTX` | 0 `LEFT`, 1 `CENTER`, 2 `RIGHT`, 3 `JUSTIFY` |
| 2 | `ALIGNMENTY` | 0 `TOP`, 1 `CENTER`, 2 `BOTTOM` |
| 3 | `NEIGHBOR` direction | 0 `UP`, 1 `DOWN`, 2 `LEFT`, 3 `RIGHT` |
| 4 | `Button` `STATES`/`TIMINGS` slots | 0 `ENABLED`, 1 `PRESSED`, 2 `DISABLED`, 3 `SELECTED`, 4 `OVER`, 5 `OVER_SELECTED` |
| 5 | `CheckBox` `TIMINGS` slots | groups 4's six, then 6 `CHKENABLED`, 7 `CHKPRESSED`, 8 `CHKDISABLED`, 9 `CHKSELECTED`, 10 `CHKOVER`, 11 `CHKOVER_SELECTED` |
| 6 | `HEADERFOOTERPOS` | 0 `Top and Bottom`, 1 `Left and Right` |
| 7 | `ORIENTATION` | 0 `Horizontal`, 1 `Vertical` |
| 8 | sort direction | 0 `Ascending`, 1 `Descending` |
| 9 | `BLENDINGMODE` | 0 `Normal`, 1 `Negative`, 2 `Plain Color`, 3 `Plain Alpha`, 4 `Silhouette`, 5 `Burn`, 6 `Burn 2X`, 7 `Burn 4X`, 8 `Dodge`, 9 `Dodge 2X`, 10 `Dodge 4X`, 11 `Darken`, 12 `Darken 2X`, 13 `Darken 4X`, 14 `Lighten`, 15 `Lighten 2X`, 16 `Lighten 4X`, 17 `Add`, 18 `Ghost`, 19 `Invert`, 20 `Multiply`, 21 `Modulate`, 22 `Only Alpha`, 23-26 `Custom1`-`Custom4` |
| 10 | `ADDRESSINGMODEU`/`V` | 0 `Wrap`, 1 `Mirror`, 2 `Clamp`, 3 `Border` |
| 11 | `MASKMODE` | 0 `NOMASK`, 1 `SETMASK`, 2 `USEMASK`, 3 `USEMASK_INVERTED` |
| 12 | texture format | 0 `TGA`, 1 `TGA32`, 2 `DXT3`, 3 `PNG` |
| 13 | `CONTROLLER` | 255 `Any Controller`, 0-7 `Controller N only` |
| 14 | button state (display form of 4) | 0 `Enabled`, 1 `Pressed`, 2 `Disabled`, 3 `Selected`, 4 `Over`, 5 `Over && Selected` |
| 15 | checkbox state (display form of 5) | 0-5 `Unchecked - …`, 6-11 `Checked - …` |
| 16 | `UserData` property type | 0 `Area Link`, 1 `Element Link`, 2 `Integer`, 3 `Float`, 4 `String`, 5 `String Resource`, 6 `Pointer`, 7 `Keyframe`, 8 `Bool` |
| 17 | loader result | 0 `Success`, 1 `Out of memory`, 2 `Open file failed`, 3 `XML syntax error`, 4 `Invalid header`, 5 `Invalid version`, 6 `Invalid binary format`, 7 `XML support disabled` |
| 18 | handler phase | 0 `PostLoading`, 1 `PreDraw`, 2 `PostDraw` |
| 19 | input events | 3 `KeyDown`, 4 `KeyUp`, 5 `MouseDown`, 6 `MouseUp`, 7 `MouseDblClick`, 8 `MouseMove`, 9 `MouseEnter`, 10 `MouseLeave` |
| 20 | focus events | 11 `SetFocus`, 12 `KillFocus`, 13 `Activate`, 14 `Escape` |
| 21 | page events | 11 `EnterPage`, 12 `ExitPage`, 13 `Overlapped`, 14 `UnOverlapped`, 15 `Tick` |

Group 16 is worth lining up against [the `UserData` type tags](./mgb.md): the wire tags there are
`0x02` u32, `0x07` float, `0x0c` bool, `0x10` string, `0x11`/`0x12`/`0x15` links, `0x13` string
resource — a different numbering from this authoring-side list, so the two are not interchangeable.

Only six of group 9's 27 appear in the shipped packages: 0 `Normal` (29,870 uses), 15 `Lighten 2X`
(40), 16 `Lighten 4X` (10), 17 `Add` (1,840), 20 `Multiply` (330) and 21 `Modulate` (1,000).

### Which group each field uses

`Util::GetType` is cdecl, so every call site pushes its group as a `push imm8` immediately before the
`call`. Scanning the binary for calls targeting `0x0a03ba50` and decoding that push gives the group
each `ReadX` actually passes — 24 call sites in total, which is what the mapping below rests on
rather than name similarity:

| field | group | call site |
|---|---|---|
| `Keyframe` `INTERPOLATION` | 0 | `ReadKeyframe+0xec` |
| `TextBase` `ALIGNMENTY` | 2 | `ReadTextBase+0x1e7` |
| `TextBase` `ALIGNMENTX` | 1 | `ReadTextBase+0x459` |
| `Element` `MASKMODE` | 11 | `ReadElement+0x125` |
| `RectShape` `BLENDINGMODE` | 9 | `ReadRectShape+0xc6` |
| `Image` `BLENDINGMODE` | 9 | `ReadImage+0x8d` |
| `Image` `ADDRESSINGMODEU`/`V` | 10 | `ReadImage+0x142`, `+0x24e` |

**Correction**: an earlier pass described `INTERPOLATION` as "a timing-strategy type id". It is not —
it is a plain group-0 value (`None`, `Linear`, `Square`, `Root`, `Sin`, `Circle`, `CircleDecel`), and
every one of the 105,160 keyframes in the shipped packages holds a value in 0-5. The timing-strategy
*type slot* is a different field: `AreaLink`'s `TIMING`.

Groups 4/5/14/15 name the **slots** of `Button`/`CheckBox`'s `TIMINGS` array rather than a value any
field holds, and group 16's property-type numbering is not the same as the wire tags `UserData`
stores (see above), so neither is a value picker.
