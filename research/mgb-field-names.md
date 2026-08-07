# `.mgb` field names, recovered from `magma::LoadVisitor`

Working notes for the field-name recovery pass (2026-08-07). Method: `BinaryLoadVisitor::VisitX`
gives *(wire order, width) → object offset*; `LoadVisitor::ReadX` gives *XML element name → object
offset*; joining on the object offset names each wire field. Addresses are `FarCry2_server`.

Fields are listed in **wire order**. "off" is the object offset both visitors agree on.

---

## State hierarchy

### `State` — `ReadState` @ `0x0a066400`
| # | wire | off | name | notes |
|---|---|---|---|---|
| 0 | u32 | `+0x08` | `INTERPOLATIONFLAGS` | `%u`; defaults to `ALL_INTERPOLATION_FLAGS` when absent |
| 1 | u32 | `+0x10` | `STATECOLOR` | `%d %d %d %d` → packed RGBA |

**Correction**: earlier passes called these `start`/`end` (a time range). They are not.

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
| 19-22 | u32 ×4 | `+0x44`..`+0x50` | `COLOR1`..`COLOR4` (packed RGBA) |

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
| 1 | u32 | `+0x1c` | `INTERPOLATION` — a timing-strategy **type id** (XML resolves a class name via `Util::GetType`) |

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
