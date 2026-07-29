---
sidebar_position: 6
---

# `.mgb` / `.mgb.desc` — Magma UI Format

:::info[Verified via reverse engineering]
Traced live via GhidraMCP, primarily against **`FarCry2_server`** (see "Which binary" below) — two
independent binaries were decompiled to produce this page. Corrects an existing community claim: the
[Almost Complete Guide](../modding/guide/file-management.md) (§".mgb and .desc files") says both
formats "can only be edited with a hex editor." That's only true of `.mgb` — see "The file pair" below.
:::

FC2's UI screens are built on **Magma**, an in-house UI engine. Each screen is a pair of files: a
`.mgb.desc` (plain XML — text bindings, nav-bar prompts, and a dependency manifest of the other
resources the screen needs) and a `.mgb` (binary — the actual widget tree: layout, geometry, keyframe
animation, materials, fonts).

## The file pair

Sample: `ui\localized\pc\eng\ui\options.mgb` / `.mgb.desc`, extracted from `patch.fat`.

`.mgb.desc` (2,585 bytes) is plain, well-formed XML — directly Notepad++-editable, no reversing needed:

```xml
<package>
	<configuration>
		<MAINMENU_OPTION_BRIGHTNESS>
			<navbar>
				<default>
					<b_prompt1 show="1" text="Generic;ACCEPT" />
					<b_prompt4 show="1" text="Generic;CANCEL" />
				</default>
			</navbar>
		</MAINMENU_OPTION_BRIGHTNESS>
	</configuration>
	<dependencies>
		<CMagmaConfigUIResource ID="ui\localized\pc\eng\ui\options.mgb.desc" crc_ID="1766041805" version="2">
			<CMagmaUIResource ID="ui\localized\pc\eng\ui\options.mgb" crc_ID="3136939932" />
			<CMagmaConfigUIResource ID="ui\localized\pc\eng\ui\fonts.mgb.desc" crc_ID="615711406" />
			<CMagmaConfigUIResource ID="ui\localized\pc\eng\ui\common.mgb.desc" crc_ID="3065158306" />
			<CTextureResource ID="ui\textures\common\option_sketch.xbt" crc_ID="1106929129" />
			<CTextureResource ID="ui\textures\common\brightness_lines.xbt" crc_ID="238881166" />
		</CMagmaConfigUIResource>
	</dependencies>
</package>
```

This confirms the internal class names directly from shipped data (`CMagmaConfigUIResource`,
`CMagmaUIResource`, `CTextureResource`). The `<dependencies>` tree is a literal, human-readable
manifest of which other `.mgb.desc`, `.mgb`, and `.xbt` files a screen needs loaded. Anyone wanting to
edit UI text bindings, nav-bar prompts, or resource dependencies should edit `.desc` — it needs no
reversing at all. `crc_ID` is not a plain CRC32 of the `ID` path string (several candidate variants
were tried by hand, none matched) — still open, see Unknowns.

`.mgb` (60,697 bytes) is binary, starting:

```
000000  4d 41 47 4d 41 cd 00 00  ab 90 ab 1e 00 00 a7 00   MAGMA...........
000010  00 00 00 00 00 00 00 e3  01 f0 86 ac cb 92 83 29   ................
```

Magic `4D 41 47 4D 41` = ASCII `"MAGMA"` (5 bytes — a different, unrelated serialization convention
from `.xbm`/`.xbg`'s 4-byte reversed-FourCC scheme, in the same engine). Past the header (offset
`0x2A7` for this sample), the body is a sequence of typed reads (floats, ints, u16s, bytes, bools, raw
byte buffers) — field order matters, not struct alignment.

## Which binary

Searching `Dunia.dll` (PC) for a literal `"MAGMA"` string turns up nothing — the magic is compared as a
packed immediate in code, not stored as an ASCII constant. The Linux dedicated-server binary
`FarCry2_server` links the same shared engine UI code and retains real `magma::`-namespaced C++
symbols, so all addresses below are `FarCry2_server` addresses unless stated otherwise. Everything found
lives in the **portable, cross-platform** part of `BinaryLoadVisitor` (the `Nomad` subclass only
overrides platform I/O), so it should apply unchanged to `Dunia.dll` — not independently re-checked
there.

## Header (`magma::BinaryLoadVisitor::ReadHeader` @ `0xa05fef0`)

Found by walking `BinaryLoadVisitor`'s vtable (`PTR_vtable_0xa40945c`; the `Nomad` subclass's own
vtable is at `0xa3d1ca0`). Slot `+0x130` is `Open(FileName const*)` @ `0xa060070`, called from
`magma::Engine::LoadPackage` @ `0xa03fc90` right after the visitor is constructed. Checked directly
against `options.mgb`'s real bytes:

| Offset | Bytes (this file) | Field | Meaning |
|---|---|---|---|
| `0-4` | `4D 41 47 4D 41` | magic | `"MAGMA"`, manual 5-byte compare — mismatch → error `4` |
| `5-8` | `CD 00 00 AB` | sentinel | only byte `8` (`0xAB`) is actually checked — mismatch triggers a fallback reader, error `6` if that also fails. Bytes `5-7` are consumed but not examined |
| `9-12` | `90 AB 1E 00` → LE u32 `0x1EAB90` | format/build version | must equal `0x1EAB90` (2,010,000) exactly or load fails with error `5` — **the same check and error message** as the XML loader's `magma::LoadVisitor::VisitPackage` @ `0xa06a370`. `.mgb` and `.mgb.desc` share one version epoch |
| `13` | `00` | flag byte | read via `ReadBool`; purpose not pinned down, plausibly compression/format-variant |
| `14` | `A7` = 167 | type-table entry count | a single byte, not a u16 — offset `15`'s `00` is the *first* byte of the type table, not a second count byte |
| `15 .. 15+4×166` | — | type table | 166 raw LE u32 IDs (count `-1`) — each ID is `CRC32(ClassName)`, see below — looked up via `objecttypemanager::GetTypeIdFromId` into a 255-byte remap array at `this+0x34`. An `Id == 0` entry is left unresolved/skipped |

Header ends at file offset `15 + 166×4 = 679` (`0x2A7`), where the widget/record body begins.

**Reader-interface vtable** (relative to the reader object, not `BinaryLoadVisitor` itself — used
throughout header and body reads): `+0x8` `ReadValue` (generic 4-byte/float), `+0xc` `ReadInt` (u32),
`+0x10` `ReadU16`, `+0x1c` `ReadByte`, `+0x20` `ReadReal` (float — a separate slot from `+0x8` despite
both being 4 bytes on the wire), `+0x24` `ReadBool`, `+0x28` `ReadBytes(buf, len)` (paired with a
`RequestBuffer`/`ReleaseBuffer` pattern), `+0x2c` `ReadUTF16Chars(buf, charCount)`. `+0x14`/`+0x18`
behave identically to `+0x10`/`+0x1c` at every call site seen — likely template-overload duplicates of
the same underlying read, not proven.

No CRC32 is *computed* while reading a `.mgb` file — `GetTypeIdFromId`'s lookup is a plain linear
exact-match scan against a table built once at engine startup. But the *values* being matched are CRC32
output, computed ahead of time at class-registration time (see below).

## `VisitPackage` — the preamble (@ `0xa0619e0`)

The first thing read after the header/type-table, before any `Area`/`Page`/`Element` tree data — get
this wrong and everything after it desyncs. The ~80 `this->vtable+0x140..+0x27c` calls scattered
through the function are **not** reads; they forward already-buffered locals to `Package` property
setters and consume zero bytes. Byte-exact order:

```
[260 bytes]  65× reader.+0x8, chained — a fixed config block (the binary counterpart of the .desc
             XML's <configuration> section, positional here instead of tag-named).
[variable]   VisitUserData(this) — Package's own generic key/value property list (record format
             below).
[4 bytes]    PAGESIZE:      2× reader.+0x10 (u16 width, u16 height)
[4 bytes]    DISPLAYOFFSET: 2× reader.+0x10 (u16 x, u16 y)
[8 bytes]    2× reader.+0xc: u32 materialCount, u32 <forwarded to a setter, not a loop count>
  × materialCount, each a Material record (VisitMaterial @ 0xa0606a0):
    [4 bytes]    reader.+0xc  → u32 nameHash        (VisitNamedObject)
    [4 bytes]    reader.+0xc  → u32 texNameLen
    [texNameLen] reader.+0x28 → raw ANSI bytes, only if texNameLen != 0 → Material::LoadTexture
    [16 bytes]   4× reader.+0x20 (float×4) → Material::SetRegion (a UV/region rect)
[4 bytes]    reader.+0xc → u32 fontSubstCount
  × fontSubstCount, each: [byte typeId][u32 len1][len1 bytes][u32 len2][len2 bytes]
             (a Font::Accept recursion happens between the two strings, but reads zero bytes — no
             VisitFont override exists, see below)
[4 bytes]    reader.+0xc → u32 fontDeclCount
  × fontDeclCount, each: same shape as fontSubst, but both strings come before the recursion
[4 bytes]    reader.+0xc → u32 fontFamilyCount
  × fontFamilyCount, each a FontFamily record (VisitFontFamily @ 0xa0615a0):
    [4 bytes]    reader.+0xc → u32 nameHash — the entire record
[4 bytes]    reader.+0xc → u32 areaCount
  × areaCount, each: [byte typeId][full VisitArea/VisitPage record — see below]
             ← handoff point from "package preamble" into the widget tree
[1+ bytes]   reader.+0x24 (bool "has global focus area?") → if true: [byte typeId][Area/Page record]
             — a named special area, not counted in areaCount above
[1+ bytes]   reader.+0x24 (bool "has second area?") → if true: same shape again
[4+ bytes]   reader.+0xc → u32 defaultMaterialNameLen; if != 0: reader.+0x28 → raw ANSI bytes →
             Package::FindMaterial/SetDefaultMaterial
--- end of file-consuming reads ---
```

Everything after this (`ResolveLinks`, duplication/instancing passes for repeated template rows) is
purely in-memory post-processing over the already-parsed tree — reads zero further bytes, so a parser
can stop the moment the optional default-material string is consumed. The same key names (`PAGESIZE`,
`DISPLAYOFFSET`) appear in the sibling XML schema parsed by `magma::LoadVisitor::ReadPackage` — `.mgb`
and its XML-config sibling schema describe overlapping concepts even though parsed by unrelated code.

## Type-table IDs are `CRC32(ClassName)`

The actual blocker for building a parser was knowing which type-table ID maps to which field layout,
resolved by decompiling three more functions:

- **`magma::Id::Hash(char const*)`** @ `0xa0782a0` — a textbook CRC-32 (polynomial `0xEDB88320`, the
  same IEEE 802.3 CRC-32 used elsewhere in the engine for `GetNameHash`/`CRC32_Hash`, per [engine
  overview](../engine-internals/overview.md)). `Id::Hash(name)` is exactly
  `binascii.crc32(name.encode())` — plain ASCII class name, no namespace, no C++ mangling.
- **`magma::objecttypemanager::Initialize()`** @ `0xa0767f0` — for every class registered via
  `Register()`, computes `hashMap[Id::Hash(typeInfo->GetName())] = typeIndex` once at startup. This is
  what `GetTypeIdFromId` scans at load time.
- **`magma::objecttypemanager::Register(ObjectTypeInfo const*)`** @ `0xa075fe0` — assigns each class's
  compact `typeIndex` (the byte stored in the `this+0x34[]` remap array) as simply the next free slot in
  call order — this binary's own link order, **not** a portable constant. A parser never needs it: a
  file's raw type-table IDs resolve straight to class names via the static CRC32 dictionary below,
  independent of any particular build's link order.

**Verified two ways**: cross-checked against `options.mgb`'s own real type table — 91 of 128 non-zero
entries matched (71%), including every widget/state class this page documents a field layout for; and
independently re-implemented the same CRC-32 from scratch, reproducing every hash in the table below
byte-for-byte.

| Class | CRC32 | Class | CRC32 | Class | CRC32 |
|---|---|---|---|---|---|
|RectShape|`d298edef`|Text|`9bb908f9`|Image|`04fc2b5b`|
|RectShapeState|`accd7ac1`|TextBase|`c72106f7`|ImageState|`821064aa`|
|ListBox|`a4b6e4fd`|TextBaseState|`00194330`|Window|`8c48fceb`|
|EditBox|`a7e03dd3`|TextState|`baf87172`|Slider|`c86b1531`|
|Placeholder|`737a10d5`|AreaInstance|`d4ef80f0`|AutonomousAreaInstance|`d269cbcb`|
|ButtonInstance|`d52917dc`|CheckBoxInstance|`bfea8d14`|RadioButtonInstance|`0e9c7df0`|
|PageInstance|`2d5f0298`|Area|`77a69256`|Page|`b438191e`|
|Button|`3daaa90b`|CheckBox|`df402c5b`|Cursor|`c2d36fb8`|
|Element|`8efd67a5`|Keyframe|`97acf549`|State|`6252fdff`|
|RotationState|`c5210873`|PosState|`643c652e`|ScaleState|`7859fae3`|
|RectState|`ac3bc0cc`|Focusable|`21d0b275`|UserData|`52ca467a`|
|NamedObject|`858143e8`|ActionCaller|`7f278949`|Widget|`82551be6`|
|Package|`11d55e09`|EngineRoot|`3f16415e`|Font|`70a6a7ec`|
|FontFamily|`3d3a929b`|StringTable|`5b3a20ed`|Material|`85c817c3`|

This table covers every widget/state/base class documented below. 37 of 128 non-zero entries in the
sample's type table remain unmatched by name — the whole `ActionExecuter` family and an action-scripting
opcode set (`Action`, `ActionStop`, `ActionPushPage`, ...) are confirmed present in the binary but exact
hashes weren't computed (candidate names have ambiguous exact spelling, e.g. `ActionExecuterListbox` vs.
`ActionExecuter_Listbox`, and shouldn't be hashed without confirming the literal string). The methodology
to close this is mechanical — extract more RTTI name strings, CRC32 them, match — just not completed.

## Widget/record body — vtable map and field layouts

From decompiling `magma::BinaryLoadVisitor`'s full vtable (base object vptr `0xa3fc308`; `Nomad`
subclass vptr `0xa3d1ca8`). The `Nomad` subclass overrides only platform I/O — every field-reading
method below lives in the portable base class.

### Vtable map

| Offset | Method | Address | Offset | Method | Address |
|---|---|---|---|---|---|
|`+0x08`|`VisitEngineRoot` (no-op)|`0x0970b4e0`|`+0x54`|`VisitPageInstance`|`0xa05f3f0`|
|`+0x0c`|`VisitNamedObject`|`0xa05f840`|`+0x58`|`VisitArea`|`0xa05f4b0`|
|`+0x10`|`VisitUserData`|`0xa062c90`|`+0x5c`|`VisitAreaLinkTags`|`0x09606b00`|
|`+0x14`|`VisitPackage`|`0xa0619e0`|`+0x60`|**`VisitPage`**|`0xa05fd60`|
|`+0x18`|`VisitWidget` (no-op)|`0x09606aa0`|`+0x64`|`VisitButton`|`0xa060410`|
|`+0x1c`|`VisitTextBase`|`0xa0616d0`|`+0x68`|`VisitCheckBox`|`0xa05df20`|
|`+0x20`|`VisitText`|`0xa0610e0`|`+0x6c`|`VisitCursor`|`0xa05dec0`|
|`+0x24`|`VisitImage`|`0xa060e80`|`+0x70`|`VisitElement`|`0xa060290`|
|`+0x28`|`VisitRectShape`|`0xa05db40`|`+0x74`|`VisitKeyframe`|`0xa05ea90`|
|`+0x2c`|`VisitListBox`|`0xa05f680`|`+0x78`|`VisitState`|`0xa05dc90`|
|`+0x30`|`VisitEditBox`|`0xa05ec80`|`+0x7c`|`VisitRotationState`|`0xa060460`|
|`+0x34`|`VisitPlaceholder`|`0x09606ae0`|`+0x80`|`VisitPosState`|`0xa060180`|
|`+0x38`|`VisitWindow`|`0xa060d70`|`+0x84`|`VisitScaleState`|`0xa05dcd0`|
|`+0x3c`|`VisitSlider`|`0xa05eb10`|`+0x88`|`VisitRectState`|`0xa05fc20`|
|`+0x40`|`VisitAreaInstance`|`0xa060a80`|`+0x8c`|`VisitTextBaseState`|`0xa05dd20`|
|`+0x44`|`VisitAutonomousAreaInstance`|`0xa05dbb0`|`+0x90`|`VisitTextState`|`0xa05fb70`|
|`+0x48`|`VisitButtonInstance`|`0xa05dbd0`|`+0x94`|`VisitImageState`|`0xa05fa20`|
|`+0x4c`|`VisitCheckBoxInstance`|`0xa05dbf0`|`+0x98`|`VisitRectShapeState`|`0xa05f950`|
|`+0x50`|`VisitRadioButtonInstance`|`0xa05dc10`|`+0x9c`|`VisitFocusable`|`0xa05fc80`|

`+0xec` = **`VisitActionCaller`** @ `0xa05e910`: `1×+0x24` (bool "has action executer?") → if true:
`1×+0x1c` (byte type-id) → `Factory::MakeActionExecuter` → recurse `Accept` →
`ActionCaller::SetActionExecuter`. This is the hook that attaches an action-dispatcher handler to any
`Area`/`Element`/`Keyframe` — all three call it before their own fields. Vtable slots `+0xf0`–`+0x120`
resolve cleanly to the **Action-dispatch/reflection subsystem** (`VisitActionExecuter` and one override
per concrete `ActionExecuter` subtype, plus `VisitUserDataItem`/`VisitGenericObjectTable`/
`VisitGenericObject`/`VisitTickTimingStrategy`) — a different format from `.mgb`'s widget geometry, not
documented further here.

### Per-type field sequences

Base classes first, then leaf widget types. "Chained" reads use the pattern `reader->Read(&a)->Read(&b)`
(every `Read*` returns `this`).

- **`VisitNamedObject`** (base of everything, `0xa05f840`): `1×+0xc` (u32) → the object's **name-hash
  ID** (same `GetNameHash`/CRC32 scheme used elsewhere), not a literal string.
- **`VisitArea`** (base of Page, `0xa05f4b0`): `VisitNamedObject` → `VisitActionCaller` (`+0xec`) →
  `AssignActionsParent` → 3× chained `+0x8` (**ticks-denominator, duration-multiplier, elementCount**)
  → loop `elementCount` times: `1×+0x1c` (u8 type-id) → `Factory::MakeElement` → child's own `Accept`
  (recursion into the matching `Visit*` below) → after the loop: 4× chained `+0x10` (u16 each — a
  static bounding-box `Rect2D`, `Area::SetStaticBox`).
- **`VisitPage`** (`0xa05fd60`): `VisitArea` (base) → `1×+0xc` (u32 tag count) → loop: `1×+0x1c` (byte
  tag), `1×+0xc` (u32 value) → `Page::AddDefaultElementTag` → `1×+0x24` (bool) →
  `Page::SetGlobalSelectionMode`.
- **`VisitWidget`** — pure no-op; `Widget` adds zero serialized fields, all real widget data flows
  through `VisitElement`.
- **`VisitElement`** (true base of RectShape/Text/Image/etc., `0xa060290`): `VisitNamedObject` →
  `VisitActionCaller` (`+0xec`) → `AssignActionsParent` → 2× chained `+0x24` (bool: hidden-flag,
  inverted into `SetVisible`; a second flag) → `1×+0x8` (u32, low 3 bits → category enum) → `1×+0x8`
  (u32 keyframe count) → loop: construct + recurse `Accept` per `Keyframe`.
- **`VisitFocusable`** (`0xa05fc80`): base `+0x70` → `1×+0xc` (u32 neighbor-tag count) → loop: `1×+0x1c`,
  `1×+0x1c`, `1×+0xc` per entry → `Focusable::AddNeighborTag` → `1×+0x24` →
  `Focusable::SetInputController`.
- **`VisitAreaInstance`** (`0xa060a80`): base `+0x18` (no-op) → `1×+0xc` (u32 string length) → raw
  UTF-16 via `+0x2c` → instance name/label → `LoadMaterial(this)` helper (texture reference) → `1×+0x24`
  → optional nested `Accept` recursion → `1×+0xc` (u32) → `vtable+0xa8`.
- **`VisitAutonomousAreaInstance`**, **`VisitButtonInstance`**, **`VisitCheckBoxInstance`**,
  **`VisitRadioButtonInstance`** are pure forwarders — no fields of their own (`ButtonInstance →
  AutonomousAreaInstance → AreaInstance`, `RadioButtonInstance → CheckBoxInstance`). Nav-bar button
  widgets (`b_prompt1`, ...) carry no binary payload beyond plain `AreaInstance`'s.
- **`VisitRectShape`** (`0xa05db40`): 2× chained `+0x24` (bool, packed) → `1×+0x8` (float — likely
  rotation or corner-radius).
- **`VisitTextBase`** (`0xa0616d0`, the richest one): base `+0x18` (no-op) → `1×+0x24` (bool mode
  flag): if `1` → 2× chained `+0xc` (StringTable ID + key hash — a localized-string reference); else →
  `1×+0xc` (u32 charCount) → raw UTF-16 via `+0x2c` → literal inline text. Then: 2× chained `+0x8` (an
  offset pair) → 4× chained `+0x24` (alignment/style flags) → `1×+0x24` "has explicit width?": if true
  → `1×+0xc` (u32, wrap-width/max-length).
- **`VisitText`** (`0xa0610e0`): base `+0x1c` → `LoadFontFamily(this)` helper → 3× chained `+0x24` →
  `1×+0x8` → `1×+0x24`.
- **`VisitImage`** (`0xa060e80`): `LoadMaterial(this)` → `1×+0x8` → `1×+0x24` → 2× chained `+0x8`.
- **`VisitListBox`** (`0xa05f680`): base `+0x18` → `1×+0x18` → 4× chained `+0x24` → `1×+0x1c` →
  `1×+0x8` → `ListBox::UpdateMetrics` → `1×+0x24`: if true → `1×+0xc` → 3 more independent `+0x24`
  checks, each guarding an optional nested `Accept` recursion (plausibly header row, scrollbar, footer).
- **`VisitCheckBox`** (`0xa05df20`): base `+0x58` → a fixed loop of **12× `+0x8`** — a 12-float array
  (plausibly 3 states × RGBA, or icon-state geometry).
- **`VisitUserData`** (`0xa062c90`, the generic key/value property system): base `+0xc` → `1×+0xc`
  (u32 property count) → loop: `1×+0xc` (property-name-hash key) → `1×+0xc` (u32 type tag, 0–0x15) →
  switch: types `{0,1,3,4,5,6,8,9,10,0xb,0xd,0xe,0xf}` read no extra payload; type `2` → `1×+0x8`
  (float); type `7` → `1×+0x20` (a second, differently-typed float read); type `0xc` → `1×+0x24`
  (bool); type `0x10` → `1×+0xc` (length) then either an external-string reference or `+0x28` raw ANSI
  bytes; types `0x11`/`0x12`/`0x15` → `VisitFullLink` (`[u16 count][byte typeId][count × u32 id]`, `3 +
  4×count` bytes); type `0x13` → `VisitStringResourceExternalId` (`8` bytes, unconditional); type `0x14`
  → null.

### Remaining leaf widgets

- **`VisitWindow`** @ `0xa060d70`: base `+0x18` → `2×+0x24` (stretch-horizontal/vertical flags) → calls
  `Window::GetWindowSection(this, N)` for `N=0..8` — a classic **9-slice/9-patch** border layout (4
  corners + 4 edges + center; sections 0, 5, 6, 7, 8 stretchable, 1-4 plain). Section helper bodies
  weren't decompiled, but their names confirm the 9-patch structure.
- **`VisitSlider`** @ `0xa05eb10`: base `+0x18` → 5× chained `+0x8` (min, max, step/increment, two
  more fields) → `1×+0x24` (bool) → `Slider::SetRange`. Then 3 independent `+0x24` checks, each
  guarding an optional nested child recursion — same optional-child pattern as `ListBox`'s
  header/scrollbar/footer (track/thumb/button-style children).
- **`VisitEditBox`** @ `0xa05ec80`: base `+0x18` → `1×+0x8` (u16-sized, likely max-length) → `1×+0x24`
  "has password char?": if true → `1×+0x2c` (single wchar) → `EditBox::SetPasswordChar` → 2 more
  independent `+0x24` checks guarding optional child recursion (text-display area, cursor/caret).
- **`VisitPlaceholder`** @ `0x09606ae0` and **`VisitAreaLinkTags`** @ `0x09606b00` — confirmed
  not overridden (pure no-ops). A placeholder is a layout slot with zero serialized fields.
- **`VisitPageInstance`** @ `0xa05f3f0`: base `+0x40` → `1×+0xc` (u32 count) → loop: `2×+0x1c`, `1×+0xc`
  per entry → `PageInstance::AddDefaultFocusTag` — structurally identical to `VisitPage`'s own tag loop.
- **`VisitButton`** @ `0xa060410` (non-instance): base `+0x58` → a fixed loop of **6× `+0x8`** — the
  same fixed-float-array pattern as `VisitCheckBox`'s 12-float block, half the size.
- **`VisitCursor`** @ `0xa05dec0`: base `+0x58` → 2× chained `+0x10` (u16, u16, stored **negated**) — a
  signed X/Y **hotspot offset**, matching "cursor hotspot: the offset subtracted from the click point."

`VisitFont`/`VisitStringTable` are not real overrides inside `BinaryLoadVisitor`'s vtable — both class
names exist in the binary but attach to unrelated visitor classes. Fonts/font-substitution are read
inline inside `VisitPackage`'s own font loop, not via double-dispatch.

### Keyframe / animation-state records

The payload of `VisitElement`'s per-element keyframe loop — the actual position/size/color animation
data attached to a widget.

- **`VisitKeyframe`** @ `0xa05ea90`: `VisitNamedObject` → `VisitActionCaller` (`+0xec`) →
  `AssignActionsParent` → 2× chained `+0x8`: **time** (u16), **value** (u32) → dispatches `Accept` on a
  nested `State` sub-record. The concrete `State` subtype isn't chosen by a byte read inside
  `VisitKeyframe` itself — it comes from `Factory::MakeKeyframe`'s `ObjectTypeInfo` argument, already
  resolved by `VisitElement`'s caller before the `Keyframe` is constructed: **the animated property's
  type is fixed by the Element's own metadata, not re-declared per keyframe.**
- **`VisitState`** (shared base) @ `0xa05dc90`: 2× chained `+0xc` (u32, u32). Every concrete `*State`
  calls this first. Reads via `+0xc` (`ReadInt`), not `+0x8` — likely interpolation/ease metadata rather
  than time, since time already lives on the owning `Keyframe`.
- **`VisitRotationState`** @ `0xa060460`: base `+0x78` → `1×+0x20` (float — **rotation angle**) → 2×
  chained `+0x10` (u16, u16 — **pivot X/Y**).
- **`VisitScaleState`** @ `0xa05dcd0`: base `+0x80` → 2× `+0x20` (**scaleX, scaleY**).
- **`VisitPosState`** @ `0xa060180` and **`VisitRectState`** @ `0xa05fc20` — both decompiled as
  this-adjustor thunks rather than clean pseudocode (a Ghidra limitation on these two
  multiple-inheritance vtable slots, not a stripped function — bytes and call targets are still
  present). Field reads are still trusted: `VisitPosState` reads **2× u16 via `+0x10`** (X, Y),
  `VisitRectState` reads **4× u16 via `+0x10`** (likely left/top/right/bottom or x/y/w/h). The
  `+0x7c` base-call offset Ghidra reported for both is not trusted numerically —
  `ImageState`/`RectShapeState` cleanly call `VisitRectState` at `+0x88`, so `RectState`'s own base call
  is probably `VisitState` (`+0x78`) with a register-misattribution shifting the apparent offset.
- **`VisitTextBaseState`** @ `0xa05dd20`: base `+0x88` → `1×+0x20` (float) → `1×+0x10` (u16) —
  extends rect (position/size) with one extra float+u16, plausibly text-scale or line-height.
- **`VisitTextState`** @ `0xa05fb70`: base `+0x8c` → `1×+0xc` → `1×+0x14` + `2×+0x18` packed into one
  4-byte temp (an alpha/scale-as-float plus 2 raw color-channel bytes) → `2×+0x10` (u16, s16 — likely a
  shadow/outline offset pair).
- **`VisitImageState`** @ `0xa05fa20`: base `+0x88` → `1×+0xc` + `2×+0x18` → **4× chained `+0x20`**
  (a Rect/UV-style struct, non-sequential storage order) → `3×+0x24` (flags) → **4× `+0xc`** in a loop
  (most likely **RGBA**).
- **`VisitRectShapeState`** @ `0xa05f950`: base `+0x88` → `1×+0x1c` + `1×+0xc` → **4× `+0xc`** in a
  loop (same RGBA-shaped block as `ImageState`) → `1×+0xc` + `2×+0x18`. Mirrors `VisitRectShape`'s own
  2-flags-plus-scalar pattern, with an added color block.

## Class hierarchy and load flow

`FarCry2_server`'s `magma::` namespace: `CResource → CResourceContainer → CMagmaResourceContainer →
{ CMagmaUIResource, CMagmaConfigUIResource }`, built via hand-rolled RTTI (`ClassHierarchyInfo` /
`CStringID::SetContent`), not compiler RTTI. Related classes: `CMagmaElementFactory`,
`CMagmaActionDispatcher`, `CMagmaFacade` (has an `EMagmaIcons` enum), `CMagmaInputListener`,
`CMagmaBinkHandler` (Magma can host Bink video playback), `IMagmaDebugTextService`.

- **`CMagmaUIResource::LoadPackageInMagma`** @ `0x0961ee70` — the `.mgb` loader entry point.
  Early-returns if already cached. Locks the `magma::CEngineNomad` singleton, builds a
  `magma::CFileNameNomad` identifier, calls a virtual (`LoadPackage`) returning a `magma::Package*`,
  then notifies `CMagmaBinkHandler::OnLoadPackage`.
- **`CMagmaConfigUIResource::LoadResourceInMagma()`** @ `0x096077a0` — the `.desc` loader. Walks its
  parsed `<dependencies>` children, recursing only into nested `CMagmaConfigUIResource` entries, then as
  the final step calls `CMagmaUIResource::LoadPackageInMagma` with a literal `"UI\"` prefix. **The `.mgb`
  binary is always the last thing loaded for a given `.desc`, after all its declared dependencies are
  satisfied**, and resource `ID` paths in the XML are relative to a `UI\` root the loader prepends.
- **Two parallel visitor implementations** exist, selected by `magma::CFactoryNomad`: `BinaryLoadVisitor`
  (ctor `0xa05edf0`, for `.mgb` — allocates a scratch buffer memset to `0xFF`, becomes the type-remap
  table) and `LoadVisitor` (ctor `0xa064860`, for XML, built on `CMarkupSTL`).
- `magma::LoadVisitor::ReadPackage` @ `0xa0688e0` is a large `CMarkupSTL`-driven XML parser, but its
  keys (`PAGESIZE`, `DISPLAYOFFSET`, `MATERIALS`, `FONTSUBST`, `FONTS`, `REPLACES`, ...) don't match the
  `<dependencies>`/`crc_ID` schema seen in `.mgb.desc` — a sibling/different config schema (font
  substitution, material remap). The actual `<dependencies>` XML parser wasn't separately located.

`CNavBarModule`/`CNavBarLayout`/`CNavBarButton`/`CNavBarPageHandler`/`CNavBarStack` are a **separate,
non-`magma`-namespaced hierarchy** that bridges into Magma rather than being part of it —
`CNavBarModule::OnActionSignal` and `CNavBarButton::SetIcon` both cross into Magma's own action/icon
systems. `CNavBarLayout::SetupNavBarButton` parses yet a third XML representation (`XmlNodeRef`,
distinct from both `CMarkupSTL` above and whatever parses `.desc`'s `<dependencies>` tree). The literal
strings `"b_prompt1"`–`"b_prompt4"` and `"p_prompts_navbar"` exist in the binary as element/`CStringID`
names, matching the `.desc` sample's `<b_prompt1 show="1" text="Generic;ACCEPT" />` elements exactly.

## Implementation status

`tools/JackAll`'s `JackAll.Core.Format.Mgb*` classes (`MgbReader`, `MgbHeader`, `MgbBody`) implement
this spec and have been run against `options.mgb`, `360.mgb`, `common.mgb`, and `ingameeditor.mgb`. It
decoded `PAGESIZE` as `1024 x 768` and the package's two `Material` records' texture paths as
`\textures\common\option_sketch.png` and `\textures\common\brightness_lines.png` — matching, byte for
byte, the two `<CTextureResource>` entries in this exact file's `.mgb.desc` sidecar, found completely
independently by two different parsing paths. `common.mgb` corroborated further: 54/54 of its own
materials decoded with real, sensible texture names.

A survey of all 21 `.mgb` files shipped with the base game found two unresolved type-table classes block
16/21 files (76%): `CRC32(name) = 0x202B3A09` resolves to `AnonymousType` (found via `Dunia.dll`'s
retained MSVC RTTI type-descriptor strings, `.?AV<Class>@magma@@`, even though its function names are
stripped — the opposite asymmetry from the Linux build) — but its exact field layout is unconfirmed;
`MgbBody` currently guesses "bare `Element`-equivalent," which parses some files deeper but produces a
different stopping error or a new crash in others. `CRC32(name) = 0x86F001E3` remains fully unidentified
— ~900 candidate class names tried across both binaries, zero matches; the next step would be live
instrumentation (hooking the running game to print the class name at resolution time) rather than more
static analysis. A full real file doesn't parse end-to-end yet given `MgbTypeTable`'s partial coverage;
the parser degrades gracefully at the first unrecognized class rather than discarding everything decoded
so far.

## Unknowns

- `CRC32(name) = 0x86F001E3` — the single biggest remaining gap (blocks 13/21 shipped files).
- `AnonymousType`'s exact field layout — a guess, not confirmed.
- No byte-level cross-check of the widget/record body (file offset `0x2A7` onward) against
  `options.mgb`'s real bytes has been done — unlike the header, every field sequence above is
  decompiled-logic-only, verified against the code but not hand-simulated against the hex dump.
- `+0x14`/`+0x18` reader-vtable slots are only guessed to be template-overload duplicates of
  `+0x10`/`+0x1c` — plausible, not proven.
- Header byte 13's flag purpose, and bytes 5-7 (consumed by the sentinel check but never examined).
- `Window::ReadStretchableWindowSection`/`ReadWindowSection`, `LoadMaterial`, `LoadFontFamily` — call
  sites and purpose confirmed, internals not decompiled.
- `Dunia.dll` (PC) was never directly checked — every finding comes from `FarCry2_server`'s
  portable/shared code, which should carry over unchanged but isn't independently verified there.
- `.desc`'s `crc_ID` attribute — confirmed not a CRC32 checked anywhere in the `.mgb` binary load path,
  but that only rules out one hypothesis. Could be a build-time-only cache key from Magma's asset
  pipeline, never re-verified at runtime.

## Reproducing this

```
tools/DuniaTools/bin/fc2_dunia/Gibbed.Dunia.Unpack.exe -v \
  "<Steam install>/Data_Win32/patch.fat" <output_dir>
```

Uses the `FCCU_FC2` project filelist to resolve real paths automatically. `patch.fat` is small (9.8 MB,
218 files) and contains one full localized set of
`ui\localized\{pc,pcwidescreen}\{eng,fre,ger,ita,spa,cze}\ui\*.mgb[.desc]` — the fastest archive to pull
`.mgb`/`.desc` samples from (`worlds.fat`/`common.fat` also contain UI resources but are far larger).
