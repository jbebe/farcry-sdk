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

**How a body type-id byte maps back to a type-table entry (confirmed 2026-08-02, both binaries)**: every
`Area`/`Element` in the body refers to a class by a single byte (0–165), but that byte is **not** a direct
0-based index into the type table above — it's off by one. The loader's own remap-building loop counts
from `1` (`remap[1]` = resolved type-table entry `0`, `remap[2]` = entry `1`, …), and every body read
indexes that same remap array directly by its raw byte with no adjustment. Net effect for a parser, which
never needs to build the remap array at all: **a body type-id byte `B` refers to type-table entry `B-1`**
(0-based). Validated by cross-checking every type-id byte actually observed live in real gameplay against
this formula: byte `44` → type-table entry `43` → `Area`; `68` → entry `67` → `Cursor`; `99` → entry `98`
→ `Button`; `100` → entry `99` → `CheckBox`; `101` → entry `100` → `Page` — a clean match in every case,
including the non-obvious ones (`Cursor` is genuinely `Area`-derived, matching its base-class vtable call
elsewhere in this doc). This also resolves what used to look like a discrepancy between two different
counting conventions for `0x86F001E3`'s position (see Unknowns) — "type-table entry `2`" and "remap slot
`3`" are just this same off-by-one relationship, not conflicting measurements.

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
  × fontFamilyCount, each a FontFamily record (VisitFontFamily @ 0xa0615a0). **Correction (2026-08-02)**:
             previously documented (and implemented) as "just the nameHash - the entire record", found
             wrong by cross-checking `fonts.mgb`'s real bytes against a fresh decompile - every sample
             file previously tested had `fontFamilyCount == 0`, so this never surfaced before. The real
             shape:
    [4 bytes]    reader.+0xc → u32 nameHash
    [4 bytes]    reader.+0xc → u32 nameLen; if != 0: [nameLen bytes] reader.+0x28 → raw ANSI name string
             (`VisitFontFamily` formats the just-read hash as a decimal string in-memory, then calls
             `BinaryLoadVisitor::LoadFont` (`0xa061300`) - despite the name, this reads via the reader
             too, not a pure lookup)
    [4 bytes]    reader.+0xc → u32 secondLen; if != 0: [secondLen bytes] reader.+0x28 → raw ANSI second
             string (a dependency path, resolved via `GetDependency`) — every real sample seen so far has
             `secondLen == 0`, skipping this entirely
[4 bytes]    reader.+0xc → u32 areaCount
  × areaCount, each: [byte typeId][full VisitArea/VisitPage record — see below]
             ← handoff point from "package preamble" into the widget tree. **Correction (2026-08-02)**:
             this loop's type-id byte resolves through `Factory::MakeArea` (`0xa0480a0` on
             `FarCry2_server`), **not** the general, ~11-category `Factory::MakeElement` every other
             typed slot in this format uses (including an `Area`'s own children, just below). `MakeArea`'s
             decompile is an **ancestor-walk** (like `MakeElement`'s own), not a flat switch: 4 specific
             type markers (`Page`/`CheckBox`/`Button`/`Cursor`, matching every real type-id byte
             previously observed live at this exact consumption point — `44/68/99/100/101`) **plus a
             generic fallback branch**, checked first in the loop, whose own type marker
             (`PTR_Type_0xa405b88`) is cross-referenced by totally unrelated subsystems (`Init`, `Find`,
             `GetMapperObject`, `ReadPackage`, `FetchMagmaElements`) — strong evidence it's a universal
             root-type marker, not a 6th specific widget category. **This was verified empirically, not
             just inferred**: a real shipped file's own top-level area list includes a byte that resolves
             to `Placeholder` (confirmed `Widget`-derived via its own constructor call chain — not
             `Area`-derived at all), and it only decodes correctly if treated as this generic fallback (a
             plain `Area` wire shape, with the resolved class name kept only as a display label) — an
             earlier, narrower "only these 5 exact classes" framing of this correction (this page's own
             prior text) was too strict and rejected real content that the actual game loads fine.
[1+ bytes]   reader.+0x24 (bool "has global focus area?") → if true: **no type-id byte is read at all**
             (correction, 2026-08-02) — directly calls a *fixed* `Factory` vtable slot (`+0x18`, no
             `ObjectTypeInfo` argument) construct a hardcoded concrete class, then recurses `Accept`.
             Which concrete class isn't identified yet. Not counted in areaCount above.
[1+ bytes]   reader.+0x24 (bool "has second area?") → if true: same shape again, but via `Factory`
             vtable slot `+0x20` (a different fixed class than the focus-area slot above)
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

This table covers every widget/state/base class documented below.

### Additional matches found via RTTI cross-reference (2026-07-31)

The methodology above (extract real RTTI class-name strings, CRC32 them, match against the type table)
was completed properly this session, using `Dunia.dll`'s own retained RTTI class list (`list_classes` in
Ghidra — recovers real names like `magma::TextBase` even though function names in this build are
stripped, the reverse asymmetry from `FarCry2_server`) rather than hand-guessed candidate strings. Cross-
referencing ~190 real class names against every non-zero entry in `options.mgb`'s type table resolved
**~40 additional entries in one pass** — the whole previously-ambiguous `ActionExecuter` family turned out
to have exactly the spelling first guessed, plus many classes not previously tried at all:

| Class | CRC32 | Class | CRC32 | Class | CRC32 |
|---|---|---|---|---|---|
|Acceptor|`8150c9c3`|EngineObject|`10604df5`|IScrollable|`7d69d774`|
|AreaLink|`b053f5df`|PageFocusable|`3cf1bbc0`|Checkable|`874df6dc`|
|Radioable|`d1d535d7`|AnonymousType|`202b3a09`|GlyphFont|`51cc13d1`|
|StringResource|`c49da7c2`|PixmapFont|`54123a01`|EngineObjectGroup|`47beed7d`|
|DisplayConfiguration|`be22749b`|Action|`406089a4`|Texture|`4ddb34ee`|
|FullLink|`a0ae5731`|WindowSection|`4e3572f7`|ActionExecuter|`9765104c`|
|UserDataItem|`7bcc34b6`|StretchableWindowSection|`322cce09`|ActionExecuterEvent|`4769d262`|
|ActionExecuterInputable|`4b25a3f8`|ActionExecuterFocusable|`bc2036f4`|ActionExecuterEditbox|`6e954785`|
|ActionExecuterListbox|`6dc39eab`|ActionExecuterPage|`0f3dc4ee`|ActionExecuterPageInstance|`2ffadce1`|
|ActionExecuterSlider|`24b5e1e0`|AreaHandler|`450eb587`|PageHandler|`4c53b21b`|
|DrawHandler|`d3617065`|GenericObject|`ff7bb1a1`|EventTriggeredTimingStrategy|`882145b8`|
|TickTimingStrategy|`33164c9f`|EventHandler|`8fb16778`|TimingStrategy|`842008d0`|
|ActionContinue|`891b59cd`|ActionStop|`26a83de2`|ActionPopPage|`a3c83096`|
|ActionPushPage|`5c524786`|ActionGotoFrameIndex|`d20a8194`|ActionGotoKeyFrame|`cb1c1757`|

Method, reproducible for any future `.mgb` sample: `magma::objecttypemanager::Register`'s ~100+ callers
(`0x10dcb430`-`0x10dcf5xx` on `Dunia.dll`, e.g. `magma_TextBase_EnsureHierarchyRegistered` @ `0x10dcb430`)
each set a scratch global to a real `magma::ClassName::ObjectTypeInfo::vftable` symbol immediately before
calling `Register` — Ghidra's RTTI analysis already recovered these as real, demangled names. Decompiling
any handful of these callers (they're all the same "register this class's whole base chain" boilerplate,
just for a different leaf class each) reads off dozens of guaranteed-real class names for free, no
string-guessing required. `AnonymousType` (`202b3a09`) was already known by name from a previous session
(via `Dunia.dll`'s MSVC type-descriptor strings) but is included here since this pass reconfirms it
independently.

### Additional matches found via live `Register` hook (2026-08-02)

A different technique from both passes above: rather than statically guessing or cross-referencing
RTTI names, this breaks live on `magma::objecttypemanager::Register` (`0x10a982b0` on `Dunia.dll`) and
resolves each call's `ObjectTypeInfo*` argument purely via memory reads (`info` → `*info` = vtable →
`*(vtable+4)` = a trivial `MOV EAX,imm32 ; RET` accessor → the imm32 operand → its first field = the
real `const char*` class name), no code execution required. 98 distinct classes were captured this way
across a full boot-to-main-menu session plus navigating every main-menu screen; these 14 were not
already present in either table above:

| Class | CRC32 | Class | CRC32 | Class | CRC32 |
|---|---|---|---|---|---|
|BaseObject|`d74fd044`|`SpecificType<ClassType>`|`666476f1`|`SpecificType<void>`|`e5b48b40`|
|CActionSignalBase|`4b4b79cd`|`CActionSignal<S>`|`fbb5d660`|CTextureNomad|`6cd2d1ed`|
|CEditBoxNomad|`c4c4f347`|Handler|`5c2a2c51`|SyncTimingStrategy|`03aa5158`|
|NoTimingStrategy|`a16abc6d`|ExternalFont|`85d6cb26`|TextScrollerPageHandler|`59e4e8df`|
|TextScrollerEventHandler|`13add68b`|TextScrollerDrawHandler|`c6d62aa1`|||

`BaseObject` registers first, before even `AnonymousType` — consistent with it being a core-engine
bootstrap type rather than a menu-specific one. None of these 14 match `0x86F001E3` either.

**`0x86F001E3` was checked against all ~190 of these real names and still does not match anything.**
Combined with the ~900 hand-guessed candidates tried previously, this is now strong negative evidence
that it isn't a plain top-level class name reachable via RTTI at all. See Unknowns below for the
concrete next step (live-hooking `Register`).

37 of 128 non-zero entries in the sample's type table remain unmatched after both passes — mostly small
numeric-looking hashes with no obvious RTTI-recoverable name candidate; `0x86F001E3` is the one actually
blocking parsers, the rest are lower priority.

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
`1×+0x1c` (byte type-id, resolved via the same per-file type-table remap scheme as element type-ids) →
`Factory::MakeActionExecuter` → recurse `Accept` → `ActionCaller::SetActionExecuter`. This is the hook
that attaches an action-dispatcher handler to any `Area`/`Element`/`Keyframe` — all three call it
before their own fields. Vtable slots `+0xf0`–`+0x120` resolve to the **Action-dispatch/reflection
subsystem** (`VisitActionExecuter` and one override per concrete `ActionExecuter` subtype, plus
`VisitUserDataItem`/`VisitGenericObjectTable`/`VisitGenericObject`/`VisitTickTimingStrategy`) — a
different format from `.mgb`'s widget geometry, but its field layout **is now decoded** (2026-08-02,
see below).

### The `ActionExecuter`/`Action` family — decoded 2026-08-02

Decompiled `magma::BinaryLoadVisitor::VisitActionExecuter` (`0xa05f870`, the shared base every concrete
`ActionExecuterXxx` either forwards to unchanged or extends) and every `VisitActionExecuterXxx`
override, plus `VisitAction` (`0xa05dd70`) and `VisitUserData` (`0xa062c90`, needed to understand what
`VisitAction` forwards to).

- **Base shape** (`VisitActionExecuter`, used unmodified by `ActionExecuterPage`/`Focusable`/`Editbox`/
  `PageInstance`/`Slider`/`Listbox` — none of them add fields of their own, confirmed by each being a
  1-line forward to the same shared implementation): `1×+0xc` (u32 `actionCount`) → loop: `1×+0xc`
  (u32 `actionTypeHash` — a **raw `CRC32(ClassName)` `Id`, read directly, not a per-file type-table
  byte** — `Factory::MakeAction` takes the hash straight, confirmed via decompile) → construct the
  `Action` → recurse `Accept`.
- **`ActionExecuterEvent`/`ActionExecuterInputable`** (`VisitActionExecuterEvent` @ `0xa05e840`, a real
  non-forwarding implementation — `Inputable` extends `Event`, confirmed by its own thunk calling the
  identical vtable slot): the same base flat action list first, then a named-event index table on top:
  `1×+0xc` (u32 `eventCount`) → per event: `1×+0xc` (u32 `indexCount`) → `indexCount ×` `1×+0xc` (u32
  `actionIndex`, a reference into the flat list already read, not a new action).
- **Every concrete `Action` opcode** (`ActionContinue`/`ActionStop`/`ActionPopPage`/`ActionPushPage`/
  `ActionGotoFrameIndex`/`ActionGotoKeyFrame`) has **no vtable override of its own** — an exhaustive
  `VisitAction*` name search turned up nothing beyond the ones already in this table. `VisitAction`
  itself forwards straight to `this->vtable[0x10]` (`VisitUserData`), so **every action's payload is
  the plain `UserData` property-list shape** (`NamedObject` + property count/loop — see below)
  regardless of which opcode it is. This means a parser never needs to know which specific opcode a
  hash names to read its bytes correctly — only the shared shape matters.

Implemented in `tools/JackAll`'s `MgbBody.ReadActionCallerField`/`ParseActionExecuterFlat`/
`ParseActionExecuterEvent`/`ParseAction`.

**The `ActionExecuter` family can also appear as a plain tree element, not just attached via
`ActionCaller`** (confirmed 2026-08-02: a real file's own `Page` - `loading_pre_loader.mgb` - has an
`ActionExecuterListbox` directly as one of its children, resolved through the ordinary
`Factory::MakeElement` widget-tree dispatch, not the `ActionCaller`-specific `Factory::MakeActionExecuter`
path). `Accept()` dispatches to the identical `VisitActionExecuterXxx` slot either way, so the shape is
exactly the one documented above - no new field layout needed, just exposing the same parse functions
from the general element dispatch too.

### Per-type field sequences

Base classes first, then leaf widget types. "Chained" reads use the pattern `reader->Read(&a)->Read(&b)`
(every `Read*` returns `this`).

- **`VisitNamedObject`** (base of everything, `0xa05f840`): `1×+0xc` (u32) → the object's **name-hash
  ID** (same `GetNameHash`/CRC32 scheme used elsewhere), not a literal string.
- **`VisitArea`** (base of Page, `0xa05f4b0`): **not** a bare `VisitNamedObject` call, despite this
  page previously stating so — corrected 2026-08-02 via direct decompile: its first call is
  `this->vtable[0x10]`, i.e. **`VisitUserData`**, not `vtable[0xc]` (`VisitNamedObject`). Since
  `VisitUserData` itself starts with the identical `NamedObject` read (confirmed via its own
  decompile — see the `VisitUserData` entry below), the net wire shape is `NamedObject` immediately
  followed by a full `UserData` property count/loop — previously undocumented and unimplemented,
  silently desyncing any real `Area`/`Page`/`Button`/`CheckBox`/`Cursor` with 1+ properties attached.
  After that: `VisitActionCaller` (`+0xec`) → `AssignActionsParent` → 3× chained `+0x8`
  (**ticks-denominator, duration-multiplier, elementCount**) → loop `elementCount` times: `1×+0x1c`
  (u8 type-id) → `Factory::MakeElement` → child's own `Accept` (recursion into the matching `Visit*`
  below) → after the loop: 4× chained `+0x10` (u16 each — a static bounding-box `Rect2D`,
  `Area::SetStaticBox`). `VisitKeyframe` (below) is **not** affected by this correction — confirmed
  calling the bare `vtable[0xc]` directly, no embedded `UserData`.
- **`VisitPage`** (`0xa05fd60`): `VisitArea` (base) → `1×+0xc` (u32 tag count) → loop: `1×+0x1c` (byte
  tag), `1×+0xc` (u32 value) → `Page::AddDefaultElementTag` → `1×+0x24` (bool) →
  `Page::SetGlobalSelectionMode`.
- **`VisitWidget`** — pure no-op; `Widget` adds zero serialized fields, all real widget data flows
  through `VisitElement`.
- **`VisitElement`** (true base of RectShape/Text/Image/etc., `0xa060290`): same correction as
  `VisitArea` above — its first call is also `this->vtable[0x10]` (`VisitUserData`, i.e. `NamedObject`
  + a full property count/loop), not a bare `NamedObject`. Then: `VisitActionCaller` (`+0xec`) →
  `AssignActionsParent` → 2× chained `+0x24` (bool: hidden-flag, inverted into `SetVisible`; a second
  flag) → `1×+0x8` (u32, low 3 bits → category enum) → `1×+0x8` (u32 keyframe count) → loop: construct
  + recurse `Accept` per `Keyframe`.
- **`VisitFocusable`** (`0xa05fc80`): base `+0x70` → `1×+0xc` (u32 neighbor-tag count) → loop: `1×+0x1c`,
  `1×+0x1c`, `1×+0xc` per entry → `Focusable::AddNeighborTag` → `1×+0x24` →
  `Focusable::SetInputController`.
- **`VisitAreaInstance`** (`0xa060a80`): base `+0x18` (no-op) → `1×+0xc` (u32 string length) → raw
  UTF-16 via `+0x2c` → instance name/label → `LoadMaterial(this)` helper (texture reference, resolved —
  see below) → `1×+0x24` → optional nested `Accept` recursion → `1×+0xc` (u32) → `vtable+0xa8`.
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
- **`VisitImage`** (`0xa060e80`): `LoadMaterial(this)` (resolved — see below) → `1×+0x8` → `1×+0x24` →
  2× chained `+0x8`.
- **`LoadMaterial`** (`0x10acb900` on `Dunia.dll`, resolved 2026-08-02): `1×+0x24`-equivalent bool
  "has explicit material?" → if false, done (1 byte total). If true: `1×+0xc`-equivalent u32 (purpose
  unclear, doesn't gate further reads) → `1×+0xc`-equivalent u32 `nameLen` → if `nameLen != 0`,
  `nameLen` raw ANSI bytes (the texture path); if `0`, falls back to an already-set default material
  pointer on the object, no further bytes. Used by `VisitAreaInstance` and `VisitImage`. **This exact
  shape was already correctly documented here, but the decoder's own `ReadResourceRef` helper never
  actually implemented it** (it read a plain length-prefixed string with no leading bool/unclear-u32) -
  found and fixed 2026-08-02 by cross-checking a real file's `Image` element (`weapon_bazaar.mgb`),
  whose material-name length was being misread as a huge/negative byte count from what was actually
  this bool+u32 pair.
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
decoded the package's two `Material` records' texture paths as `\textures\common\option_sketch.png` and
`\textures\common\brightness_lines.png` — matching, byte for byte, the two `<CTextureResource>` entries
in this exact file's `.mgb.desc` sidecar, found completely independently by two different parsing paths.
`common.mgb` corroborated further: 54/54 of its own materials decoded with real, sensible texture names.
**`PAGESIZE` correction (2026-08-02)**: this section previously stated JackAll decoded `PAGESIZE` as
`1024 x 768` for `options.mgb`, then (in the same 2026-08-02 session, before the confusion was resolved)
guessed that was a transcription error and claimed `1280 x 800` instead. **Both readings are correct** —
they're two different real files. `tools/JackAll/src/JackAll.Core.Tests/Fixtures/Patch/patch.fat`'s
`options.mgb` decodes to `1024 x 768`; a separately-sourced `options.mgb` in `tmp/menu/` (a different
game build/version) decodes to `1280 x 800`. Both are self-consistent and byte-validated against their
own `Materials` texture paths. No transcription error occurred; two different real inputs just have
different real content, as expected.

**Major correctness pass (2026-08-02)**, prompted by extending `JackAll.App`'s `.mgb` file handler:
found and fixed four real bugs, none previously caught because the four locally-available sample files
all stopped early enough (at the `0x86F001E3` wall — see below) that none of these ever manifested:

1. **The body type-id-byte off-by-one was documented as "confirmed" above but never actually
   implemented.** `MgbBody`'s type dispatch indexed the type table directly by the raw byte, with no
   `-1` — silently reading the *wrong neighboring table entry* for every single typed element in every
   file, the entire time. Now applied via a single `ResolveTypeTableEntry` helper used everywhere a
   type-id byte is resolved.
2. **`VisitPackage`'s top-level `areaCount` loop was dispatched through the same broad
   `Factory::MakeElement`-equivalent switch as element children**, when it actually needs
   `Factory::MakeArea` (see `VisitPackage — the preamble` above) — the previous code could silently
   resolve a top-level area to a class `Factory::MakeArea` could never really construct. Now a dedicated
   `ParseTypedTopLevelArea` dispatcher, matching `MakeArea`'s own ancestor-walk shape (4 specific
   categories plus a generic root-marker fallback — see below).
3. **`Area`/`Element`'s own base call was documented and implemented as a bare `VisitNamedObject`
   read** — actually `VisitUserData` (`NamedObject` + a full property count/loop), per direct decompile
   of `VisitArea`/`VisitElement` (see their entries above). Silently dropped every property attached to
   every `Area`/`Element` and desynced everything after it whenever one had 1+ properties.
4. **The `ActionExecuter`/`Action` family was entirely undecoded**, throwing on any element with an
   attached action. Now decoded (see the dedicated section above).

A **fifth bug**, found and fixed immediately after the above (same session): `ParseTypedTopLevelArea`'s
first version modeled `Factory::MakeArea` as exactly 5 disjoint classes (`Area`/`Page`/`Button`/
`CheckBox`/`Cursor`), inferred purely from previously-live-observed real bytes — this rejected a real
file's own top-level area 7, which resolves to `Placeholder` (confirmed `Widget`-derived, not
`Area`-derived). Re-examining `MakeArea`'s decompile showed it's an ancestor-walk with a generic
fallback branch, not a flat 5-way switch (see `VisitPackage — the preamble` above) — treating any
otherwise-unmatched class as that generic fallback (a plain `Area` shape, real class name kept only as
a label) is what actually lets real files parse further.

Combined, these took all 4 locally-available real files (`common.mgb`, `common_mp.mgb`, `options.mgb`,
`sp_menus.mgb`) from stopping at top-level area index 3 (the `0x86F001E3` wall) to reaching *into* area
index 7's own body (an unrecognized `UserData` property type tag partway through it — the current
frontier, not yet chased further). **Cross-file validation**: area 0 in every one of these 4 files (plus
the separately-sourced `patch.fat` fixture's `options.mgb` — 5 files total, 2 different game builds) now
resolves identically to `Cursor` (matching this page's own previously-live-observed `byte 68 → Cursor`
mapping), with byte-identical `NameHash`/`TicksDenominator`/`StaticBox`/`Hotspot` values and the same 2
child elements (`Placeholder`/`Handler`) — strong evidence this region parses correctly, not
coincidentally. `MgbTypeTable`'s dictionary also gained the 14 classes from the "live `Register` hook"
table below, which were confirmed there but never actually added to the dictionary until now (a
previous-session omission).

An older survey of all 21 `.mgb` files shipped with the base game found two unresolved type-table
classes blocking 16/21 files (76%): `AnonymousType` and `0x86F001E3`. **That framing is now stale for
`AnonymousType`** (see Unknowns below) - re-parsing all 4 locally available real files (`common.mgb`,
`common_mp.mgb`, `options.mgb`, `sp_menus.mgb`) this session found `AnonymousType` present in every
type table but never actually causing a stop; `0x86F001E3` alone was the stop reason in all 4, every
time (**before** the fixes above — see the top-level-area-index correction just above). `CRC32(name) =
0x86F001E3` remains fully unidentified — ~900 hand-guessed candidate class names tried across both
binaries plus ~190 real RTTI-recovered names cross-referenced this session (see above), zero matches;
the next step would be live instrumentation (hooking `magma::objecttypemanager::Register`,
`0x10a982b0` on `Dunia.dll`, to read each registered class's real name off its own vtable at
registration time) rather than more static guessing. A full real file doesn't parse end-to-end yet given
`MgbTypeTable`'s partial coverage; the parser degrades gracefully at the first unrecognized class rather
than discarding everything decoded so far.

**Major correctness pass #2 (2026-08-03): a real bug found and fixed via direct Dunia.dll disassembly,
not just decompile.** Prompted by a push to fully reverse `options.mgb` specifically. Decompiled
pseudocode for `VisitArea`/`VisitUserData`/`VisitElement` on `Dunia.dll` turned out to be unreliable in
places (heavy register/parameter misattribution on these thiscall-heavy, MSVC-optimized functions) -
raw disassembly plus a from-scratch vtable-slot derivation was needed instead:

- **Vtable-slot numbers are not transplantable between `FarCry2_server` and `Dunia.dll` at face value,
  but the *relative* layout is.** Ground truth for a slot's real address: `get_xrefs_to` a named
  function returns the vtable *data* addresses that store it; the vtable's true slot-0 base is 4 bytes
  *after* that (the preceding slot holds the MSVC RTTI Complete Object Locator pointer - confirmed by
  reading it). Cross-checking `VisitUserData` (`0x10aca130`) against `VisitArea` (`0x10ac9520`) and
  `VisitElement` (`0x10ac9360`) this way, on **both** of `BinaryLoadVisitor`'s vtables (base + `Nomad`
  subclass), reproduced the *exact* relative slot gaps this page already documents from
  `FarCry2_server` (`VisitUserData` +0x10, `VisitArea` +0x58, `VisitElement` +0x70 - all three gaps
  matched byte-for-byte). This confirms `VisitArea`/`VisitElement` really do call `VisitUserData`
  first (not a bare `VisitNamedObject`) on `Dunia.dll` too - the existing "Area/Element base = full
  UserData record" correction (above) holds on the real PC binary, not just the Linux server build.
- **The real bug**: `VisitUserData`'s real disassembly (`0x10aca130`) shows its per-property dispatch
  is `if ((uint)(tag - 2) > 0x13) goto noPayloadCase;` - a plain range check against `[2, 0x15]`, not
  an exhaustive enumeration. **Any tag outside that range is real, legal, no-payload content** - the
  game never throws or treats it as an error. `MgbBody.ReadUserDataProperties` previously threw
  `NotSupportedException` for any tag outside a hand-enumerated set (`{0,1,3,4,5,6,8,9,10,0xb,0xd,0xe,
  0xf}` plus the ones with real payloads) - silently wrong for the (apparently common) case of a tag
  that's simply some other small-but-unenumerated value, or outright garbage/reserved. Fixed to match
  the confirmed real dispatch: only `{2,0xc,0x10,0x11,0x12,0x13,0x15}` read a payload; everything else,
  enumerated or not, is no-payload.
- **Effect of the fix, all 4 locally available real files, before → after**: `options.mgb` 478 → 3,657
  body bytes consumed (still stops at area 7, see below); `common.mgb` reaches area 7 and now stops on
  a new, well-defined gap (`Element class 'PixmapFont' isn't implemented by this decoder yet` - a
  missing-class TODO, not a mystery); `sp_menus.mgb` reaches 11,865 of 11,868 body bytes (99.97%) before
  running out of bytes by 4 while reading one more field past area 7; **`fonts.mgb` now parses
  end-to-end**, stopping only on the already-documented, unrelated `GlobalFocusArea` gap (a fixed,
  non-type-table-driven construction, not yet identified - see Unknowns). This was, by a wide margin,
  the single highest-value fix found this session - it was silently breaking every file with a property
  using a tag this decoder didn't already know about, which turns out to be common.
- **`options.mgb`'s own area 7 (type-id byte 77 → `Placeholder`, `NameHash=0x01D427AD`,
  `UserData` property count `389`) is still not fully resolved, but hypothesis (a) from below is now
  ruled out and two more real bugs were found and fixed chasing it (2026-08-03, later same day).**
  A live debugger trace (breaking inside the real `VisitUserData` for the entire game-startup
  menu-parse burst, `tmp/menu/`'s own build, not this page's `patch.fat` fixture) never once saw a
  property count anywhere near 389 (max observed: 15, across 3,114 real calls) - which first looked
  like proof the static decode was landing at the wrong offset entirely. It isn't: manual hex
  verification against the raw file bytes (independent of this decoder's own arithmetic) confirmed
  `NameHash=0x01D427AD` and `count=389` sit at exactly the position the decoder computes, and the
  entire 389-entry property loop parses with **zero errors and plausible-looking values throughout**
  (a `FullLink<CheckBox>` and a float, the only two in-range tags among the 389, both decode
  sensibly) - strong evidence the property loop itself is reading real, correctly-aligned structure,
  not misaligned garbage. So hypothesis (a) below (some in-range tag being a false positive) is
  **ruled out** for this specific file. The live-trace-vs-static-decode contradiction remains
  unexplained - candidate explanations: the live game's actual installed `options.mgb` isn't
  byte-identical to either sample analyzed here, or this exact `Placeholder` object is only
  constructed lazily (e.g. first real menu navigation) rather than during the initial parse burst,
  contrary to what was assumed going in.

  Two real, disassembly-confirmed bugs were fixed along the way (both correct fixes, **neither
  explains this file's own desync** - confirmed this file's area 7 has zero tag-`7` properties and
  all 4 of its `FullLink` properties have count `2`, never `0`):
  - `UserData` property type `7` was silently treated as no-payload; real disassembly (`MOVSS` at
    Dunia.dll `0x10aca237`/`0x10aca24c`) shows it reads a real 4-byte float via a distinct reader
    vtable slot (`+0x8`, vs type `2`'s `+0x20`) - a 4-byte overread on every tag-7 property.
  - `VisitFullLink` (Dunia.dll `0x10ac9ef0`, ported from FarCry2_server `0xa0604d0`) short-circuits to
    an immediate return when its entry count is `0` (`CMP word ptr[...],0x0; JZ <epilogue>`) - no
    `typeId` byte, no ids. This decoder always read the extra `typeId` byte regardless of count - a
    1-byte overread on every empty `FullLink`.

  The actual desync, whatever it is, now provably lives at or after `ReadActionCaller`/
  `ticksDenom`/`durationMult`/`elementCount` (the fields read immediately after the 389-property
  loop, which are implausible: `ticksDenom=7680`, `durationMult=10240`, `elementCount=12800` - not a
  real widget tree) - not inside the property loop itself. Two explanations remain open: (a) the
  `Placeholder`-as-generic-`Area`-fallback framing is right about *construction* (confirmed via
  disassembly - `Factory::MakeArea`'s fallback unconditionally builds a plain `Area`, and `Area::Accept`
  always calls `VisitArea`) but wrong about the *exact tail shape* read after `VisitUserData` in this
  context; or (b) `ReadActionCaller`'s own bool/executer read has a narrower bug not yet found. Next
  step if resumed: hex-verify the bytes immediately following the 389th property against what
  `ReadActionCaller`+`ticksDenom`+`durationMult`+`elementCount` assume, the same way `NameHash`/`count`
  were just verified.

**Next direction (2026-08-03): a real editor, not just an inspector.** Motivated by
[the menu-system doc](../engine-internals/magma-menu-system.md)'s "Path B" (intercepting `.mgb` loading
to inject a real Mods page, sidestepping `CGameMenu`'s blocked hashtable insert) - `tools/JackAll` has
no writer at all yet (`MgbBody` only has `ParsePackage`) and `JackAll.App`'s file handler is read-only
(a `TextBlock`). The scoped-out approach doesn't require first resolving every remaining format unknown
above: a byte-preserving design (parse what's understood into an editable tree, keep anything not
understood as an opaque blob passed through verbatim, splice edits back in) can support well-scoped
edits - like adding a new `Button` to an existing `Page`'s element list, one of the best-validated parts
of the whole format - without needing the `Keyframe` state-selection mechanism or the
`Placeholder`-as-`Factory::MakeArea`-fallback shape resolved first. Not started this session.

## Unknowns

- **How a `Keyframe`'s concrete `*State` subtype is actually chosen isn't just "one type per owning
  element class"** (2026-08-02 finding, real-file cross-check). `VisitElement`'s own keyframe loop
  (`0xa060290`) queries `(*(param_1+0x14))`'s own virtual call once per keyframe index - an in-memory
  call against a per-element-instance object (not a file read) - whose return value feeds
  `Factory::MakeKeyframe`. This means a *single* `RectShape` (or any element) can legitimately cycle
  through *different* state types across its own keyframes (e.g. one animating color →
  `RectShapeState`, another animating position → `PosState`), not always the same type as this
  decoder's current `MgbBody.ParseKeyframe` assumes (`ownerClassName + "State"`, unconditionally).
  Confirmed as the actual cause of a real parse failure in `videos.mgb`: its first `RectShape`'s
  keyframe #1 decodes as `RectShapeState` byte-perfectly (independently verified against a fresh
  decompile of `VisitRectShapeState`, confirming that per-type field layout is *not* the bug), but
  keyframe #2 hits a type-id byte (`255`) that's structurally out of range for the file's own
  166-entry type table — strong evidence it's actually a different, smaller state type
  (`PosState`/`RotationState`/`ScaleState`/`RectState`) that this decoder never attempts. Next step:
  decompile whatever object lives at an `Element`'s own `+0x14` (likely a per-class, compile-time-fixed
  "animatable property" descriptor table) to learn the real per-keyframe-index type sequence for each
  widget class - a substantial standalone investigation, not a quick byte-shape guess.
- A related, still-open mystery in the same area: `weapon_bazaar.mgb`'s own `Image` element's single
  keyframe decodes as `ImageState` with several suspicious-looking repeated `0xFFEAFDD7`-style values in
  its `Color`/`Value` fields, and the subsequent `LoadMaterial` read (now correctly shaped - see its own
  entry below) still hits a huge/garbage length immediately after. Whether this is the same
  keyframe-state-selection issue (an `Image` cycling through a state type this decoder doesn't attempt)
  or a distinct `ImageState` field-layout bug (its own fields were flagged as never independently
  decompile-verified) isn't resolved yet.
- `AnonymousType` is NOT a widget/Element-tree class — a major reframe (2026-07-31, via
  `FarCry2_server`). It's magma's universal type-erased "any value" property wrapper (a
  `std::any`/`boost::any` equivalent): ~350 mangled symbols confirm every class's every reflected field
  goes through `InternalGet*`/`InternalSet*(AnonymousType const&)` (e.g.
  `magma::RectShape::InternalGetFilledFlag`, `magma::TypedProperty<Button>::GetProperty(EngineObject*,
  AnonymousType const&)` — literally every widget/state class), and every `SpecificType<T>`
  instantiation has `operator=`/`operator==(AnonymousType const&)`, confirming it's the common "any" box
  every strongly-typed `SpecificType<T>` converts to/from.

  **Where its type-id byte actually gets read, found this session**: `BinaryLoadVisitor`'s vtable slot
  `+0xb0` (an undocumented gap between `Focusable` at `+0x9c` and `ActionCaller` at `+0xec`) is
  `VisitFullLink` (`0xa0604d0` on `FarCry2_server`) — confirmed by decompile to resolve its `byte typeId`
  through the exact same per-file remap array (`this+0x34`) and `objecttypemanager::GetType` that
  `Factory::MakeElement` uses for Area/Element children. `VisitGenericObject` (`0xa05de70`) calls this
  slot directly on its own `+0xc` field, meaning a `GenericObject`'s internal link list is a `FullLink`
  whose declared target type can legitimately be `AnonymousType` (a generically-typed reference, no more
  specific type available) — this is almost certainly the real mechanism, not the widget tree.

  **Confirmed empirically** (all 4 locally available real files - `common.mgb`, `common_mp.mgb`,
  `options.mgb`, `sp_menus.mgb` - re-parsed with today's 84-name dictionary): `AnonymousType` is present
  in every one of their type tables, but **none of them ever stop on it** - `MgbBody.cs`'s
  `"AnonymousType" => bare Element-equivalent` switch case is never actually exercised for these files.
  `JackAll.Tools`'s existing `ReadFullLink` already resolves a `FullLink`'s type-id byte through
  `MgbTypeTable` but never throws on an unresolved/generic one, so encountering `AnonymousType` there is
  silently harmless either way - consistent with it being a `FullLink`-target type tag, not a genuine
  Element type. The doc's older "AnonymousType blocks 16/21 files" survey claim (below) looks stale
  against current behavior; needs re-running against all 21 files, not just these 4, to confirm.
- `CRC32(name) = 0x86F001E3` — the single biggest remaining gap, and the most heavily investigated item
  in this whole doc (static guessing across two sessions, then a full day of live IDA debugging plus
  static re-analysis on 2026-08-02). Current state, facts only:

  **Where it lives in the file**: type-table entry `2` (0-based, confirmed identically in all four real
  sample files — the first two entries are literal zero/unused, this is the third). Per the off-by-one
  formula documented above, that means the body's own type-id byte for it is **`3`**. It's referenced by
  a real body element (not just present-but-unused in the table) — the older survey's "4th top-level
  `Area`" framing and this session's "remap slot 3" finding are the *same fact*, not a discrepancy (see
  the off-by-one note above; this doc previously flagged them as possibly conflicting, which was wrong).

  **It has to be a real, currently-registered class — not stale/removed, not a dead reference.** Traced
  the exact consequence of an unresolved type-table entry all the way through both binaries:
  `GetType(unsigned char)` (`Dunia.dll` `0x10AC9140`, `FarCry2_server` `0xa075fa0` — identical logic on
  both, confirmed by decompile: `return TypeArray[index];`, no bounds check) returns whatever sits at
  `TypeArray[0]` for an unresolved lookup. That slot is **permanently reserved for `BaseObject`**, the
  root of the whole class hierarchy — confirmed directly on `FarCry2_server`, where
  `Register`'s sentinel special-case (the branch that always writes straight to `TypeArray[0]`) is
  `BaseObject::ms_typeInfo`, proven by its constructor writing the literal string `"BaseObject"` right
  before building it (**not** `AnonymousType`, despite the superficial analogy to that class's own special
  handling elsewhere in this doc). `Factory::MakeElement` (`Dunia.dll` `0x10ABF0E0`/`0x10ABED20` depending
  on call site, `FarCry2_server` `0xa0481a0` — same ancestor-walk logic on both) walks a type's inheritance
  chain against ~11 hardcoded leaf-widget categories and **returns `NULL`** if nothing matches — which is
  exactly what happens for `BaseObject`, the root, with no further ancestors to check. And the real
  `Area`/`Element`-constructing loops (`VisitArea` on both binaries, `VisitPackage`'s own `areaCount`
  loop, `VisitAreaLink`, `VisitFullLink`, and the bool-gated "global focus area"/"second area" slots — all
  independently confirmed on `Dunia.dll` this session) call `Factory::MakeElement` and **immediately
  dereference the result's vtable with zero NULL check**. Chained together: a real body element that
  actually resolved to typeIndex `0` at construction time would crash the game, every time, on every
  affected file. It doesn't, in normal play. So `0x86F001E3` is a real, live-registered class in this
  build — the "stale hash from a removed class" theory is now considered unlikely for this reason, not
  just unconfirmed.

  **Despite that, no live capture has ever caught it registering or resolving successfully**, across an
  exhaustive session: `Register` hook (98 distinct real class names captured, full boot-to-main-menu
  session, every menu screen) — no match. `GetTypeIdFromId` hook — one real "not found" catch during an
  actual file's header parse (confirmed genuinely exhaustive: the scan walked every registered class and
  reached the table's end pointer), but this was very likely an unrelated file, since it was never
  followed by a matching `Register` call. A follow-up session confirmed, directly from the user, that (a)
  the debugger is always attached before the game process starts (ruling out "registered before we could
  observe it"), and (b) the in-game pause menu (the likely home of `sp_menus.mgb`, one of the four files
  known to reference this hash) is always opened. Even so, a comprehensive live capture covering **all
  four** real consumption points of a resolved type-id byte (`VisitPackage`'s `areaCount` loop, the
  bool-gated focus-area slot, `VisitAreaLink`, `VisitFullLink`) across a full session — thousands of
  hits — never once produced type-id byte `3`. Distinct byte values actually observed:
  `VisitPackage/areaCount`: `44,68,99,100,101` · focus-area slot: `133,138–143` · `VisitFullLink`:
  `44,66,68,69,71,72,99,100,101,126` · `VisitAreaLink`: `150,151,152`.

  **A from-scratch static re-implementation of the format (Python, this session) got partway toward
  settling this independently**, and is the most promising unfinished thread: correctly parses the
  header, type table, and `VisitPackage`'s preamble through materials byte-exact (validated against real
  texture paths — see the `PAGESIZE` correction above), and resolved `LoadMaterial`'s previously-open byte
  format along the way (see above). It reached the first top-level `Area` (resolves to `Cursor`) but hit
  an undiagnosed field-layout error somewhere past `elementCount` before it could walk far enough to check
  whether byte `3` appears anywhere in real file content (see the byte-level cross-check note above) —
  genuinely unresolved, not abandoned by choice.

  **Net assessment**: this is very likely a real class that registers extremely early — in the same
  boot-time bootstrap phase as `BaseObject`/`AnonymousType`/`SpecificType<T>` (all three register before
  anything menu-related, see the live-capture table above) — and/or is only ever touched by a body path
  this session's live captures didn't cover (a fifth consumption point not yet found, or a file/screen
  genuinely never loaded despite the pause-menu check). The concrete next steps, in order of promise: (1)
  finish the static re-implementation past the `Area` field-layout bug — this settles the question with
  zero further live debugging and was very close to working; (2) if that's inconclusive, get all 21
  shipped `.mgb` files (only 4 are available locally) and re-run the same static check against the other
  17, in case the affected files aren't the ones already tested.
- `AnonymousType`'s exact field layout — a guess, not confirmed.
- Byte-level cross-check of the widget/record body (file offset `0x2A7` onward) against real bytes is
  **partial**, not the "not done at all" it used to be: the config block → `VisitUserData` → `PAGESIZE`/
  `DISPLAYOFFSET` → materials chain is now hand-simulated and validated byte-exact against all four real
  sample files (materials decode to real, correct texture paths for every one — see `PAGESIZE` correction
  above). What's still purely decompiled-logic-only, not hand-verified against real bytes: everything from
  `fontSubstCount` onward, and the entire `Area`/`Element` tree — a from-scratch re-implementation this
  session correctly parsed a top-level `Area` resolved as `Cursor` through its `NamedObject`/`ActionCaller`
  fields and `elementCount`, but its own trailing fields (`Area::SetStaticBox`'s 4-`u16` box, `Cursor`'s
  own 2-`u16` hotspot) produced implausible values, meaning `VisitArea`'s exact field layout past
  `elementCount` still has an undiagnosed error somewhere — genuinely unresolved, not just unattempted.
- `+0x14`/`+0x18` reader-vtable slots are only guessed to be template-overload duplicates of
  `+0x10`/`+0x1c` — plausible, not proven.
- Header byte 13's flag purpose, and bytes 5-7 (consumed by the sentinel check but never examined).
- `Window::ReadStretchableWindowSection`/`ReadWindowSection`, `LoadFontFamily` — call sites and purpose
  confirmed, internals not decompiled. (`LoadMaterial` was resolved 2026-08-02 — see its own entry above.)
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
