---
sidebar_position: 6
---

# `.mgb` / `.mgb.desc` — Magma UI Format

:::info[Verified via reverse engineering]
Fully decoded. Every field layout on this page is transcribed from a decompile of the matching
`magma::BinaryLoadVisitor::Visit*` method in the Linux dedicated-server binary `FarCry2_server` (which
retains complete `magma::` C++ symbols), and cross-checked against the PC `Dunia.dll` via live IDA
tracing. A from-scratch reimplementation of this spec parses **all 50 files** of the local `.mgb`
corpus byte-perfectly — see [Validation](#validation).

Corrects an existing community claim: the [Almost Complete Guide](../modding/guide/file-management.md)
(§".mgb and .desc files") says both formats "can only be edited with a hex editor." That's only true
of `.mgb` — see "The file pair" below.
:::

:::tip[Authoring, not parsing?]
This page is the wire format — what a *tool* needs. If you want to build or edit a screen, start at
[Magma UI](../magma-ui/index.md): the model, the XML vocabulary, the patterns shipped screens use,
and the limits of the format.
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
were tried by hand, none matched) — still open, see [Unknowns](#unknowns).

`.mgb` (60,697 bytes) is binary, starting:

```
000000  4d 41 47 4d 41 cd 00 00  ab 90 ab 1e 00 00 a7 00   MAGMA...........
000010  00 00 00 00 00 00 00 e3  01 f0 86 ac cb 92 83 29   ................
```

Magic `4D 41 47 4D 41` = ASCII `"MAGMA"` (5 bytes — a different, unrelated serialization convention
from `.xbm`/`.xbg`'s 4-byte reversed-FourCC scheme, in the same engine). Past the header, the body is
a sequence of typed reads (floats, ints, u16s, bytes, bools, raw byte buffers) — field order matters,
not struct alignment. There is no padding, no alignment, and no length prefix on any record: **the
only way to find the end of a record is to read every field of it**, which is why a single wrong field
width silently corrupts everything downstream.

## Reading this page

Two mechanical facts underpin everything below. Both are easy to get wrong, and each produced a
generation of incorrect models in earlier revisions of this page.

### Reader primitive widths

Every field read dispatches through the serializer object's vtable. The slot number determines the
byte width and nothing else:

| Slot | Bytes | Slot | Bytes | Slot | Bytes |
|---|---|---|---|---|---|
| `+0x08` | 4 | `+0x14` | 2 | `+0x20` | 4 (float) |
| `+0x0c` | 4 | `+0x18` | 1 | `+0x24` | 1 (bool) |
| `+0x10` | 2 | `+0x1c` | 1 | `+0x28` | *n* raw bytes |
| | | | | `+0x2c` | *n* UTF-16 units (2*n* bytes) |

`+0x08`/`+0x0c`/`+0x20` are three distinct slots that all consume 4 bytes — the engine distinguishes
"value"/int/float, the wire format does not. Likewise `+0x14`/`+0x18` are separate slots that behave
identically to `+0x10`/`+0x1c`. Two readers exist: `BinaryReadSerializer` (native little-endian) and
`BinaryInvertReadSerializer`, which byte-swaps every multi-byte read for big-endian console content.

### ELF vtable offsets are shifted by 8

An object's vptr points at virtual slot 0, which sits at offset `+0x08` inside the vtable object
(`+0x00` is offset-to-top, `+0x04` is the typeinfo pointer). So a `CALL [vtable + 0x7c]` in a
decompile resolves to whatever symbol is listed at **`+0x84`** in a vtable dump.

Miss this and every base-class call lands one slot early: it is what made earlier revisions of this
page describe `VisitRectState`'s base call as `VisitRotationState`-but-untrusted, and led to a wholly
invented "per-owner-widget tail" model for keyframe states. `FarCry2_server`'s
`magma::BinaryLoadVisitor::vtable` is at `0x0a3fc300` (the `CBinaryLoadVisitorNomad` subclass, which
overrides only platform I/O, is at `0x0a3d1ca0`); `Dunia.dll`'s equivalent is at `0x10ee7bcc`, where
every slot sits **4 bytes lower** than the corresponding `FarCry2_server` dump offset (MSVC vtables
have no offset-to-top/typeinfo prefix, only the RTTI Complete Object Locator at −4).

### Where the field names come from

Field names on this page are the engine's own authored names, not invented labels. `magma::LoadVisitor`
— the XML loader that reads Magma's `.mgm` source format — is a complete 1:1 mirror of
`BinaryLoadVisitor`: ~55 `ReadX` methods, one per class, that parse *named* XML elements into the
*same object offsets* the binary visitor writes. Joining the two gives each wire field its real name:

- `BinaryLoadVisitor::VisitX` → wire order and width → object offset
- `LoadVisitor::ReadX` → object offset → XML element name

For example `VisitRectState` writes four `u16`s to `+0x24`/`+0x26`/`+0x28`/`+0x2a`, and `ReadRectState`
(`0x0a065130`) reads `LEFT`, `RIGHT`, `TOP`, `BOTTOM` into those same four — which is also how the
non-obvious ordering was caught. Where a field is stored through a named setter rather than a direct
offset write (`Area::SetStaticBox`, `Slider::SetRange`, `Focusable::SetInputController`), the setter
name is used instead.

A handful of classes still carry names inferred from their XML vocabulary rather than the per-field
join — `ListBox`, `Slider`, `EditBox` and a few package-level records. Those are marked in
[the field-names companion page](./mgb-field-names.md), which holds the full per-offset join
including each class's known XML vocabulary. Their *widths and order* are offset-verified like everything else; only the labels are
provisional.

## Header (`magma::BinaryLoadVisitor::ReadHeader` @ `0x0a05fef0`)

Reached from `Open` (`0x0a060070`, vtable slot `+0x130`), called by `magma::Engine::LoadPackage`
(`0x0a03fc90`). Checked directly against `options.mgb`'s real bytes:

| Offset | Bytes (this file) | Field | Meaning |
|---|---|---|---|
| `0-4` | `4D 41 47 4D 41` | magic | `"MAGMA"`, manual 5-byte compare — mismatch → error `4` |
| `5-8` | `CD 00 00 AB` | sentinel | only byte `8` (`0xAB`) is checked — mismatch switches to `BinaryInvertReadSerializer` and re-checks byte `5`, error `6` if that also fails |
| `9-12` | `90 AB 1E 00` → LE u32 `0x1EAB90` | format/build version | must equal `0x1EAB90` (2,010,000) exactly or load fails with error `5` — the same check and error as the XML loader's `magma::LoadVisitor::VisitPackage` (`0x0a06a370`). `.mgb` and `.mgb.desc` share one version epoch |
| `13` | `00` | flag byte | read via a bool read; purpose never pinned down |
| `14` | `A7` = 167 | type-table entry count | a single byte, not a u16 |
| `15 .. 15+4×166` | — | type table | 166 raw LE u32 `Id`s (count −1) |

Header ends at file offset `15 + 166×4 = 679` (`0x2A7`), where `VisitPackage` begins.

**The type table and its off-by-one.** The fill loop runs slots `1 .. count-1`, writing
`this[slot + 0x34] = objecttypemanager::GetTypeIdFromId(id)` for each non-zero `Id`. Every body type
byte then indexes that same array *raw*, with no adjustment. So a **body type byte `B` names table
entry `B-1`** (0-based) — and byte `0`, plus any byte whose table `Id` was `0`, resolves through the
never-written part of the array. The constructor (`0x0a05edf0`) does `memset(this + 0x34, 0, 0xff)`,
so those all land on compact type id `0`.

No CRC32 is *computed* while reading a `.mgb` — `GetTypeIdFromId` is a plain linear exact-match scan
against a table built once at engine startup. But the values being matched are CRC32 output, computed
at class-registration time.

## Type-table IDs are `CRC32(ClassName)`

- **`magma::Id::Hash(char const*)`** @ `0x0a0782a0` — a textbook CRC-32 (polynomial `0xEDB88320`, the
  same IEEE 802.3 CRC-32 the engine uses for `GetNameHash`, per the
  [engine overview](../engine-internals/overview.md)). `Id::Hash(name)` is exactly
  `binascii.crc32(name.encode())` — plain ASCII class name, no namespace, no C++ mangling.
- **`magma::objecttypemanager::Initialize()`** @ `0x0a0767f0` — for every class registered via
  `Register()`, computes `hashMap[Id::Hash(typeInfo->GetName())] = typeIndex` once at startup.
- **`magma::objecttypemanager::Register(ObjectTypeInfo const*)`** @ `0x0a075fe0` — assigns each class's
  compact `typeIndex` as the next free slot in call order, i.e. **this binary's link order**, not a
  portable constant. A parser never needs it: a file's raw type-table `Id`s resolve straight to class
  names via the static CRC32 dictionary below.

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
|BaseObject|`d74fd044`|`SpecificType<ClassType>`|`666476f1`|`SpecificType<void>`|`e5b48b40`|
|CActionSignalBase|`4b4b79cd`|`CActionSignal<S>`|`fbb5d660`|CTextureNomad|`6cd2d1ed`|
|CEditBoxNomad|`c4c4f347`|Handler|`5c2a2c51`|SyncTimingStrategy|`03aa5158`|
|NoTimingStrategy|`a16abc6d`|ExternalFont|`85d6cb26`|TextScrollerPageHandler|`59e4e8df`|
|TextScrollerEventHandler|`13add68b`|TextScrollerDrawHandler|`c6d62aa1`|GenericObjectTable|—|

Three independent methods produced this list: brute-forcing candidate class names against real type
tables; cross-referencing `Dunia.dll`'s retained RTTI class-name strings (`magma::objecttypemanager::
Register`'s ~100 callers each set a scratch global to a real `magma::ClassName::ObjectTypeInfo::vftable`
symbol immediately before calling `Register`, so decompiling a handful reads off dozens of guaranteed-real
names for free); and a live hook on `Register` (`0x10a982b0` on `Dunia.dll`) that resolves each call's
`ObjectTypeInfo*` purely via memory reads — `info` → vtable → `*(vtable+4)` → a trivial
`MOV EAX,imm32 ; RET` accessor → the imm32 → its first field, the real `const char*` name.

**Not every entry needs a name.** A typical file's table has ~128 non-zero entries of which ~35 stay
unresolved, including `0x86F001E3`, historically the most-investigated hash on this page. That no
longer blocks anything: the three Factory dispatchers below accept only a small closed set of classes,
so a parser only has to recognise *those*, and an unresolved hash simply never appears in a slot that
matters. `0x86F001E3` in particular occurs only in type tables, never as a live type byte.

## The three Factory dispatchers — the key to the whole format

Every type byte in the body is resolved to an `ObjectTypeInfo` and handed to one of three `Factory`
methods. Each is an **ancestor walk**: it tests the type and then its base classes against a fixed
list, most-derived match wins, and a type matching nothing produces a null object which the caller
dereferences with no guard. That last part is the important one — **a type outside these sets cannot
appear in that slot in any file the game can actually load**, so a parser can treat it as a hard
error rather than guessing a fallback shape.

### `Factory::MakeArea(Area::ObjectTypeInfo const*)` @ `0x0a0480a0`

Accepts exactly 5 classes: **`Area`, `Page`, `Button`, `Cursor`, `CheckBox`**. Used only for the
package's top-level area list.

### `Factory::MakeElement(Widget::ObjectTypeInfo const*)` @ `0x0a0481a0`

Accepts exactly **14 widget classes**, and picks which `Element` subclass wraps each one:

| Wrapper built | Widget classes |
|---|---|
| `Element` | `Image`, `Text`, `RectShape`, `Placeholder`, `Window`, `AreaInstance`, `AutonomousAreaInstance` |
| `Focusable` | `ButtonInstance`, `ListBox`, `EditBox`, `Slider` |
| `PageFocusable` | `PageInstance` |
| `Checkable` | `CheckBoxInstance` |
| `Radioable` | `RadioButtonInstance` |

`VisitPageFocusable` (`0x0a05dc30`), `VisitCheckable` (`0x0a05dc50`) and `VisitRadioable`
(`0x0a05dc70`) are pure forwards to `VisitFocusable`, so all four non-plain wrappers read the
identical body.

### `Factory::MakeState(Widget::ObjectTypeInfo const*)` @ `0x0a047c30`

Maps the **owning widget's class** to the concrete `State` subclass used by *every* keyframe on that
element. It resolves to a `State::ObjectTypeInfo` which the sibling overload (`0x0a047990`) turns into
a `Factory::MakeXState` call:

| State class | Widget classes |
|---|---|
| `ImageState` | `Image` |
| `TextState` | `Text` |
| `RectShapeState` | `RectShape` |
| `RectState` | `Placeholder`, `Window` |
| `ScaleState` | `AreaInstance`, `AutonomousAreaInstance`, `ButtonInstance`, `CheckBoxInstance`, `RadioButtonInstance`, `PageInstance`, `ListBox`, `EditBox`, `Slider` |

This is a **compile-time 1:1 map** — nothing about it is per-instance, per-file or data-driven. Earlier
revisions of this page modelled keyframe states as a single universal `RectState` plus a mysterious
"per-owner-widget tail" of 24/28/42/51/65 bytes; those five widths are simply
`RectState`/`ScaleState`/`TextState`/`RectShapeState`/`ImageState`.

### `Factory::MakeActionExecuter(ActionExecuter::ObjectTypeInfo const*)` @ `0x0a0483c0`

Accepts 9 classes: bare **`ActionExecuter`**, plus `ActionExecuterEvent`, `…Inputable`, `…Focusable`,
`…Page`, `…PageInstance`, `…Listbox`, `…Editbox`, `…Slider`. Bare `ActionExecuter` reads only the flat
action list; the other 8 each forward through zero-field hops down to `VisitActionExecuterEvent`,
which appends the named-event index table.

## `VisitPackage` (@ `0x0a0619e0`)

The whole file body, in order. Everything past the last line is in-memory post-processing
(`ResolveLinks`, the duplication/instancing passes for repeated template rows, a second
`Allocate*PoolChunk` sweep) and reads zero further bytes.

```
[260 bytes]  65 × 4-byte reads — per-type instance counts feeding the Allocate*PoolChunk family.
             Pure memory-pool pre-reservation; no effect on any later offset.
[variable]   UserData        — the Package's own property list (record format below).
[4 bytes]    PAGESIZE        — u16 width, u16 height
[4 bytes]    DISPLAYOFFSET   — u16 x, u16 y
[8 bytes]    u32 materialCount, u32 distinctTextureCount (a setter argument, not a loop count)
             The second `u32` is **the number of distinct `texture` paths among the materials that
             follow** — it equals that count in all 50 corpus files, while equalling `materialCount`
             in only 45. `ps3.mgb` settles it: 30 materials sharing 1 texture, and the field is 1.
             Materials that differ only in their UV `REGION` share a texture, which is exactly the
             gap in `common.mgb` (56 materials, 54 textures) and `loadout.mgb` (40 / 36).
  × materialCount: Material (VisitMaterial @ 0x0a0606a0)
    [4]        u32 nameHash                         (VisitNamedObject)
    [4+n]      u32 texNameLen + n raw ANSI bytes    → Material::LoadTexture
    [16]       4 × float                            → Material::SetRegion (a UV rect)
[4 bytes]    u32 fontSubstCount
  × fontSubstCount: [u8 typeSlot][u32 len][len bytes: embedded font blob][u32 len][len bytes: subst name]
             The blob goes to Font::Load, which parses it out of the already-read buffer and never
             touches the stream again.
[4 bytes]    u32 fontRefCount
  × fontRefCount: [u8 typeSlot][u32 len][len bytes][u32 len][len bytes]
             Two names: looked up via Package::GetFontSubst, else FontServer::RequestFont.
[4 bytes]    u32 fontFamilyCount
  × fontFamilyCount: FontFamily (VisitFontFamily @ 0x0a0615a0)
    [4]        u32 nameHash
    [4+n]      u32 fontNameLen + n bytes            (LoadFont @ 0x0a061300)
    [4+n]      u32 pkgNameLen + n bytes             — only read if fontNameLen != 0
[4 bytes]    u32 areaCount
  × areaCount: [u8 typeSlot → Factory::MakeArea][the Area record — see below]
[1+ bytes]   bool hasStringTable; if set → StringTable (see below)
[1+ bytes]   bool hasGenericObjectTable; if set → GenericObjectTable (see below)
[4+ bytes]   u32 defaultMaterialNameLen; if != 0 → that many raw ANSI bytes
--- end of file-consuming reads ---
```

**The two trailing tables** were, for a long time, the page's "global focus area" and "second area"
mysteries: `VisitPackage` builds them through hardcoded `Factory` vtable slots rather than the type
table, so no type byte identifies them and static analysis kept dead-ending. Resolving the `Factory`
vtable settles it — slot `+0x18` is `Factory::MakeStringTable` and slot `+0x20` is
`Factory::MakeGenericObjectTable`:

- **`VisitStringTable`** @ `0x0a05e9a0`: `VisitNamedObject` → `u32 count` → `count ×` **StringResource**
  (`VisitStringResource` @ `0x0a0611c0`: `VisitNamedObject` → `u32 charCount` → `charCount` UTF-16
  units). Registered globally afterwards via `StringServer::RegisterStringTable`.
- **`VisitGenericObjectTable`** @ `0x0a05e7c0`: `VisitNamedObject` → `u32 count` → `count ×`
  **GenericObject** (`VisitGenericObject` @ `0x0a05de70`: `VisitNamedObject` → a `FullLink`).

Both are commonly empty (`count == 0`, 8 bytes), which is exactly why an earlier live trace measured
them as "two chained u32 reads" and concluded they were a fixed 8-byte record. `options.mgb`'s string
table holds one entry, the string `"0123456789"`; its generic-object table holds 16 objects.

## Shared records

- **`VisitNamedObject`** @ `0x0a05f840` — one `u32`, the object's name hash (`CRC32` of its authored
  name), not a literal string.
- **`VisitUserData`** @ `0x0a062c90` — the generic property system, used by `Package`, `Area`,
  `Element` and every `Action`. `VisitNamedObject`, then `u32 count`, then per property a `u32` key
  hash, a `u32` type tag, and a payload determined by the tag:

  | Tag | Payload | Tag | Payload |
  |---|---|---|---|
  | `0x02` | `u32` | `0x11`/`0x12`/`0x15` | `FullLink` |
  | `0x07` | `float` | `0x13` | `StringResourceExternalId` (2 × `u32`) |
  | `0x0c` | `bool` | `0x14` | none |
  | `0x10` | `u32 len` + `len` raw ANSI bytes | *anything else* | none |

  The dispatch is a `switch` with a default that consumes nothing. **Any tag not listed above — 
  enumerated in the switch or not — is a legal, payload-less property.** A parser must never treat an
  unknown tag as an error.
- **`VisitFullLink`** @ `0x0a0604d0` — `u16 count`; **if zero the function returns immediately**, with
  no type byte and no ids. Otherwise `u8 typeSlot` then `count × u32` id.
- **`VisitAreaLink`** @ `0x0a0601c0` — `u8 timingTypeSlot`, `u32 packageRef`, `bool hasAreaRef`
  (`u32 areaRef` if set), `bool isDuplicate`.
- **`LoadMaterial`** @ `0x0a0608c0` and **`LoadFontFamily`** @ `0x0a060f20` — byte-identical functions
  differing only in the `Package::Find*` lookup they perform afterwards: `bool present`; if false
  nothing else is read; otherwise `u32 id`, `u32 pkgNameLen`, and `pkgNameLen` raw ANSI bytes naming
  the owning package (empty means the current one).

## Area records

**`VisitArea`** @ `0x0a05f4b0` — the shared body of all five `MakeArea` classes:

```
UserData                        (name hash + property list)
ActionCaller                    (see below)
u32 FRAMERATE                   — the engine stores 1000 / this
u32 CURRENTFRAME
u32 elementCount                — CHILDREN/COUNT in the XML
  × elementCount: [u8 typeSlot → Factory::MakeElement][the Element record — see below]
4 × u16 STATICBOX               — Area::SetStaticBox (a Rect2D)
```

Each concrete subtype then appends its own tail:

| Class | Tail |
|---|---|
| `Area` | — |
| `Page` (`0x0a05fd60`) | `u32 tagCount`, then `tagCount × (u8, u32)` default element tags; `bool globalSelectionMode` |
| `Cursor` (`0x0a05dec0`) | `2 × u16`, stored negated — a hotspot offset |
| `Button` (`0x0a060410`) | `6 × u32` timings |
| `CheckBox` (`0x0a05df20`) | `12 × u32` timings — Button's six plus six for the checked state |

## Element records

**`VisitElement`** @ `0x0a060290`:

```
UserData
ActionCaller
bool HIDDEN                     — inverted into Element::SetVisible
bool ISDUPLICATABLE
u32  MASKMODE                   — low 3 bits kept
u32 keyframeCount               — KEYFRAMES/COUNT in the XML
  × keyframeCount: Keyframe (see below)
widget->Accept(visitor)         — the widget's OWN fields (WIDGET in the XML)
```

That last line is the subtle one. `VisitElement`'s decompile shows no serializer calls after the
keyframe loop, so earlier revisions of this page treated the tail as pure in-memory finalization; in
fact it dispatches straight into `VisitImage`/`VisitText`/`VisitAreaInstance`/… which read real bytes.
And because `VisitFocusable` calls `VisitElement` *first*, a `Focusable`-wrapped element's own tail
comes **after** the widget body:

- **`VisitFocusable`** @ `0x0a05fc80` — `u32` neighbour count, then that many
  `NEIGHBOR` entries of `CONTROLLER` u8, `DIRECTION` u8, `ID` u32, then `INPUTFILTER` bool.

### Widget bodies

| Widget | `Visit*` | Fields |
|---|---|---|
| `Placeholder` | `0x09606ae0` | none — the inherited no-op. A placeholder is a layout slot with zero serialized fields. |
| `RectShape` | `0x0a05db40` | `ISOUTLINED` bool, `ISFILLED` bool, `BLENDINGMODE` u32 (low byte kept) |
| `Image` | `0x0a060e80` | `MATERIALLINK`, `BLENDINGMODE` u32, `ALPHABLENDFIRST` bool, `ADDRESSINGMODEU`/`ADDRESSINGMODEV` (2× u32, packed into one byte's nibbles) |
| `TextBase` | `0x0a0616d0` | gate bool: if set → `TABLEID` u32, `RESOURCEID` u32; else → `STRING` (u32 char count + UTF-16). Then `ALIGNMENTX`, `ALIGNMENTY` (2× u32), `WRAPPING`/`CLIPPING`/`ELLIPSIS`/`AUTOSIZED` (4× bool), gate bool → `SLIDERLINK` u32 |
| `Text` | `0x0a0610e0` | base `TextBase`, then `LoadFontFamily`, `BOLD`/`ITALICS`/`UNDERLINED` (3× bool), `BLENDINGMODE` u32, `ALPHABLENDFIRST` bool |
| `AreaInstance` | `0x0a060a80` | `LABEL` (u32 char count + UTF-16 — the target area's name), `MATERIALLINK`, gate bool → `LINK` (`AreaLink`), `INDEXOFFSET` u32 |
| `AutonomousAreaInstance`, `ButtonInstance`, `CheckBoxInstance`, `RadioButtonInstance` | — | pure forwards; no fields of their own |
| `PageInstance` | `0x0a05f3f0` | base `AreaInstance`, then `u32 count` + that many `DEFAULTFOCUS` entries of `DEFAULT_FROM_DIRECTION` u8, `DEFAULT_FROM_DIRECTION_2` u8, id u32 |
| `ListBox` | `0x0a05f680` | `u8 sortMode`, `4 × bool`, `u8 timingSlot`, `u32 metrics`, `bool` → `SLIDERLINK` u32, then `3 × [bool → AreaLink]` — the first `AreaLink` is the per-item row template (always `isDuplicate`), see below |
| `EditBox` | `0x0a05ec80` | `u32 maxLength`, `bool hasPasswordChar` → 1 UTF-16 unit, then `2 × [bool → AreaLink]` |
| `Slider` | `0x0a05eb10` | `5 × u32` (`SetRange` takes the first two), `bool`, then `4 × [bool → AreaLink]` |
| `Window` | `0x0a060d70` | `2 × bool` (stretch H/V), then nine 9-patch sections: index 0 and 5–8 stretchable, 1–4 plain |

**`ListBox`'s optional `u32` is a `SLIDERLINK`** — the name hash of a sibling `Slider` element that
acts as the list's scrollbar, exactly like `TextBase`'s own `SLIDERLINK`. Confirmed by hash identity
in shipped data: `common.mgb` area `6155790C` (`COMMON_SAVELOADPAGE`, the Save/Load list) has a
`ListBox` whose optional value is `0xEC6561E0`, and the `Slider` element beside it in that same area
is named `0xEC6561E0`.

**A scrollbar is authored, not intrinsic — but scrolling is.** Only `COMMON_SAVELOADPAGE` sets this
field. Every options-family nav list (`common.mgb 36150990`, used by Game/Sound/Display/Network and
the MP menus) leaves it unset and has no `Slider` at all. Confirmed live by appending 30 rows to one:
the list keeps a **viewport** and moves it with the selection, clamped at both ends rather than
wrapping — so a long list scrolls correctly with no scrollbar present, and the `Slider` is purely a
visual indicator plus drag target.

**The first of the three `AreaLink`s is the row template.** It is `isDuplicate` in every `ListBox` in
the corpus, pointing at the small area duplicated once per item (`0x1E77C7D0` for most lists,
`0xCD5A24AE` for `36150990`). The value-list cell `652FD37C` is the only widget in the corpus that
sets all three.

**Window sections** — `ReadWindowSection` (`0x0a060c40`): `LoadMaterial`, `u32 blendingMode`,
`ALPHABLENDFIRST`/`FLIPHORIZONTAL`/`FLIPVERTICAL`/`ROTATED` (4× bool).
`ReadStretchableWindowSection` (`0x0a060d20`) calls that and appends `STRETCHMODE` (u32). The nine
sections, in the engine's own 0-8 order, are `FILL`, `TOP_LEFT_CORNER`, `TOP_RIGHT_CORNER`,
`BOTTOM_LEFT_CORNER`, `BOTTOM_RIGHT_CORNER`, `TOP_EDGE`, `LEFT_EDGE`, `RIGHT_EDGE`, `BOTTOM_EDGE` —
the classic 9-slice border layout, with `FILL` and the four edges stretchable.

## Keyframe and State records

**`VisitKeyframe`** @ `0x0a05ea90`:

```
VisitNamedObject                — u32 name hash
ActionCaller
u32 IDX                         — the frame index; stored truncated to u16, but 4 bytes are consumed
u32 INTERPOLATION               — a timing-strategy type id (the XML authors it as a class name)
state->Accept(visitor)          — the concrete State, chosen by Factory::MakeState from the
                                  OWNING WIDGET's class. Never re-declared per keyframe, and
                                  never read from the stream.
```

The `State` hierarchy, with cumulative on-the-wire sizes:

```
State  (8)  ──▶ RotationState (16) ──┬─▶ PosState  (20) ──▶ ScaleState (28)
                                     └─▶ RectState (24) ──┬─▶ TextBaseState (30) ──▶ TextState (42)
                                                          ├─▶ ImageState (65)
                                                          └─▶ RectShapeState (51)
```

| Class | `Visit*` | Own fields (after its base) |
|---|---|---|
| `State` | `0x0a05dc90` | `INTERPOLATIONFLAGS` u32, `STATECOLOR` u32 (packed ARGB) |
| `RotationState` | `0x0a060460` | `ROTATION` float, `ORIGIN` x/y (2× u16) |
| `PosState` | `0x0a060180` | `POSITION` x/y (2× u16) |
| `ScaleState` | `0x0a05dcd0` | `SCALEX`, `SCALEY` (2× float) |
| `RectState` | `0x0a05fc20` | `LEFT`, `RIGHT`, `TOP`, `BOTTOM` (4× u16) |
| `TextBaseState` | `0x0a05dd20` | `OFFSETY` float, `ABSOFFSETY` u16 |
| `TextState` | `0x0a05fb70` | `SHADOWCOLOR` u32, `HEIGHT` u16, `SHADOWOFFSETX`/`Y` (2× u8), `LEADING`, `TRACKING` (2× u16) |
| `ImageState` | `0x0a05fa20` | `SHADOWCOLOR` u32, `SHADOWOFFSETX`/`Y` (2× u8), `TILING` x/y + `OFFSET` x/y (4× float), `FLIPHORIZONTAL`/`FLIPVERTICAL`/`ACTUALSIZE` (3× bool), `COLOR1`..`COLOR4` (4× u32) |
| `RectShapeState` | `0x0a05f950` | `OUTLINEWEIGHT` u8, `OUTLINECOLOR` u32, `FILLCOLOR1`..`FILLCOLOR4` (4× u32), `SHADOWCOLOR` u32, `SHADOWOFFSETX`/`Y` (2× u8) |

Note `RectState`'s order — **left, right, top, bottom**, not the l/t/r/b anyone would guess. And
`State`'s two `u32`s are not a frame-time range: an earlier revision of this page called them
`start`/`end`, which was a guess that happened to look reasonable.

`TEXTCOLOR` (alias `COLOR`) in the XML writes the inherited `STATECOLOR` field rather than being a
field of its own. When `COLORn` (n>1) is absent from the XML the loader copies `COLOR1`, which is
what identifies those four as the corner colours of a gradient quad.

Every colour word in this hierarchy is **ARGB** (`0xAARRGGBB`), authored `A R G B` — see the
correction under [`State` in the field-name join](./mgb-field-names.md#state--readstate--0x0a066400)
for the packing and the corpus evidence.

Note `PosState` and `RectState` are **siblings** under `RotationState`, not a chain — both write their
own fields starting at object offset `+0x24`, and `RectState` is not `PosState`-derived despite
reading a superset of its widths.

## Action / ActionExecuter family

**`VisitActionCaller`** @ `0x0a05e910` — called by `Area`, `Element` *and* `Keyframe` before their own
fields: `bool hasExecuter`; if false nothing else is read. Otherwise `u8 typeSlot` →
`Factory::MakeActionExecuter` → the executer's own body.

**`VisitActionExecuter`** @ `0x0a05f870` — `u32 actionCount`, then per action a `u32 actionId`
followed by the action's body. That id is a **raw `CRC32(ClassName)` `Id`, read directly** and handed
to `ActionServer::MakeAction` — not a per-file type-table byte.

No concrete `Action` opcode (`ActionContinue`/`ActionStop`/`ActionPopPage`/`ActionPushPage`/
`ActionGotoFrameIndex`/`ActionGotoKeyFrame`) overrides `Visit*`, and `VisitAction` (`0x0a05dd70`)
forwards straight to `VisitUserData`. **Every action's payload is a plain `UserData` record whatever
its opcode**, so a parser never needs to know which opcode a hash names in order to read its bytes.

**`VisitActionExecuterEvent`** @ `0x0a05e840` — the 8 named subtypes append, on top of the flat list:
`u32 groupCount`, then per group `u32 indexCount` followed by `indexCount × u32` — indices into the
flat action list already read, not new actions.

## Validation

`tools/JackAll/src/JackAll.Tools/Format/mgb_parser.py` is a direct transcription of this page.
Against the 50-file corpus in `tmp/menu/` (gitignored):

- **50/50 files parse to exactly `file_size`**, every declared area consumed, zero bytes left over and
  zero unread trailing data.
- **Read-for-read identity with the running game.** `C:\temp\handler_semantic.txt` (produced by
  `reverse/ida/Dunia.dll/trace_handler_semantic.idc`, which breakpoints the shared reader-entry
  primitive at `Dunia.dll 0x10AE7BF0` plus `VisitElement`/`VisitUserData`/`VisitFullLink` directly)
  logs the reader's byte cursor for every primitive read in `controller.mgb`'s `0x0E00–0x1100` window.
  The parser reproduces **all 258 read offsets exactly**, and all 17 visitor entry points land on the
  same cursors.
- **Area boundaries match live ground truth.** `C:\temp\controller_areas.txt` records the real
  per-area cursor positions during a live load; the parser's own seven area offsets
  (`0x3E9`, `0x4EB`, `0xE3F`, `0x1276`, `0x1693`, `0x1879`, `0x1BDD`) match all seven.
- **Two independent implementations agree on every decoded value.** The C# codec and the Python
  reference each emit a canonical dump of the whole corpus — page sizes, material paths, area and
  element types, name hashes, property counts, flags, keyframe counts, state types, text content,
  instance labels, interpolation flags and colours — and the two are identical across all 22,932
  lines. This is what catches a mistake round-tripping cannot: swapping two adjacent fields of the
  same width reproduces the bytes perfectly while labelling both wrong.
- **Every corpus file reserialises byte for byte** under the C# codec (`MgbRoundTripTests`), which
  proves reader and writer simultaneously.
- **Every corpus file survives a round trip through XML byte for byte** (`MgbXmlTests`). This is a
  strictly stronger check than the binary one: a field can read and write correctly as bytes while
  being unrepresentable as text, and float bit patterns, non-text string bytes and null-versus-zero
  each fail here and nowhere else.
- **Decoded content is semantically real**, not merely structurally plausible: material paths match
  the `.desc` sidecar's `<CTextureResource>` entries byte for byte; `options.mgb`'s string table
  decodes to `"0123456789"`; all 72 `AreaInstance` target names across the corpus are printable and
  meaningful (`PARAM_SHAPE`, `PARAM_HARDNESS`, …); all 5,422 elements have a plausible
  `FRAMERATE`; and the 10,949 decoded keyframes distribute across state types exactly as the
  `MakeState` table predicts.

This last point matters because "parses without throwing" is a weak signal for this format — a wrong
model can land on a structurally plausible offset by coincidence and silently under-read thousands of
bytes while reporting success. Several earlier revisions of this page did exactly that.

## Implementation — reading, writing and editing

Two independent implementations of this spec live in the repo, and they check each other:

- **`tools/JackAll/src/JackAll.Tools/Format/mgb_parser.py`** — the reference decoder, and the
  implementation the live-trace validation above was done against. Read-only.
- **`tools/JackAll/src/JackAll.Tools/Format/Mgb/`** — the production C# codec and object model,
  used by JackAll's `.mgb` editor, its `mgb decode`/`mgb encode` CLI verbs, and the XML interchange
  format below.

The C# side describes each record's wire format **once**, in a `Serialize(IMgbCodec, MgbContext)`
method that every codec drives — the binary reader and writer, and both directions of the XML
interchange format below. That is not a stylistic choice: the obvious alternative (a `Read` and a
matching `Write` per record) relies on a human keeping ~40 pairs in step, and this format punishes
one mismatched width by silently corrupting everything after it. With a shared description,
`Write(Read(x)) == x` becomes a property the code either has or doesn't.

`IMgbCodec` carries structural operations (`Scope`, `Item`, `ListScope`, `Gate`) alongside the field
primitives. The binary codecs implement them as no-ops, because in a `.mgb` nesting is implied by
the order of reads and nothing more. They exist so a text codec can recover the tree that the binary
format expresses only through the shape of the call graph.

Three rules make byte-exact round-tripping work, and are worth repeating for anyone writing a third
implementation:

- **Derive counts, don't store them.** List lengths come from the live collection on write, so an
  edited tree can't disagree with its own counts. The one exception is the `u32` after
  `materialCount`, which is a setter argument rather than a loop bound — preserve it rather than
  recomputing, since nothing checks it and a future model of it may be wrong.
- **Keep strings as raw bytes.** ANSI and UTF-16 payloads round-trip as `byte[]`, decoded only for
  display. Going through a `string` risks a non-reversible encoding on unexpected content.
- **Keep floats as raw bits.** Store the `u32` and reinterpret for display, so NaN payloads and
  denormals survive untouched.

Because the whole package decodes, editing is whole-tree mutation plus a reserialise rather than
byte splicing. That makes size-changing edits — adding an element, retyping a string, **declaring a
class the file didn't previously list** — ordinary operations. Growing the type table shifts every
body offset after it, which is precisely what a splice-based editor cannot survive; the ceiling is
the header's single count byte, 254 entries, against the 167 real files use.

The editor derives what it offers from the three `Factory` dispatchers above: the "add" picker for a
package's area list is `MakeArea`'s five classes, and for an area's element list `MakeElement`'s
fourteen. Since those sets are exactly what the engine can construct, an editor constrained to them
cannot produce a package the game would reject.

### The XML interchange format

`jackall mgb decode` renders a package as XML and `mgb encode` builds it back; the JackAll editor
offers the same pair as Export/Import XML. This is the relationship `.fcb` has with the XML Gibbed's
converter produces — **the game never loads it**. It exists so a package can be diffed, reviewed,
and edited with ordinary text tools.

It is deliberately **not** `.mgm`, Magma's own XML source format, even though `LoadVisitor` is linked
into the retail client. `.mgm` is a *source* format that compiles into a `.mgb`, so the two loaders
are siblings rather than inverses, and it cannot round-trip one:

- No construct exists for the per-file **type table** (a link-order-dependent intern table holding
  ~35 ids no name is known for), the 260-byte **pool-count block**, header bytes 5–7 and 13, or the
  **embedded font blobs** in `FONTSUBST`.
- It parses floats through `atof`, losing NaN payloads and denormals, and cannot express the
  discarded high bits of the truncating fields (`IDX` u32→u16, `MASKMODE`'s low 3 bits).
- Most decisively, **`.mgm` authors names as strings that the loader CRC32s, and the binary keeps
  only the hash.** CRC32 does not invert, so exporting a shipped package to strict `.mgm` means
  inventing names — which changes every hash and breaks every cross-reference.

What the format does borrow is `.mgm`'s **vocabulary**: element and attribute names are the engine's
own authored names, from the `BinaryLoadVisitor`↔`LoadVisitor` join in
[the field-names companion page](./mgb-field-names.md). Structure is elements, leaf fields are
attributes:

```xml
<Element slot="74" type="Image" HIDDEN="false" ISDUPLICATABLE="true" MASKMODE="NOMASK">
  <USERDATA name="#0BA6368A"><PROPERTIES /></USERDATA>
  <KEYFRAMES>
    <Keyframe name="#5264ED6A" IDX="0" INTERPOLATION="None">
      <ImageState INTERPOLATIONFLAGS="0" STATECOLOR="FFFFFFFF" LEFT="0" RIGHT="32" TOP="0" BOTTOM="32"
                  TILING.x="1" TILING.y="1" FLIPHORIZONTAL="false" COLOR1="FFFFFFFF" />
    </Keyframe>
  </KEYFRAMES>
  <Image BLENDINGMODE="Normal" ALPHABLENDFIRST="false" ADDRESSINGMODEU="Clamp" ADDRESSINGMODEV="Clamp">
    <MATERIALLINK present="true" id="#C57F6A8E" PACKAGE="\common.mgb" />
  </Image>
</Element>
```

Three substitutions make it readable, and each one **provably reverses** — that is the line, and
nothing that fails it is substituted:

- **Enum names** come from `magma::Util`'s `ms_tagTable`, so they are engine constants. A value the
  table doesn't contain stays a bare number, which is what keeps `BLENDINGMODE` (low byte only) and
  `MASKMODE` (low 3 bits) from silently losing their high bits.
- **Name hashes** render as the recovered name only when re-hashing it reproduces the stored value,
  otherwise as `#XXXXXXXX`. A wrong candidate cannot get written.
- **`type="Image"` beside `slot="74"`** is decoration; **the slot stays authoritative**, because
  several slots can resolve to one class and rebuilding from the name alone would not reproduce the
  file.

The escape hatches matter as much as the pretty forms. A float whose decimal spelling isn't bit-exact
is written as `0x…`; string bytes that can't survive an XML attribute become `base64:…`; and an
absent optional is an omitted attribute, never an empty one, because `null` and present-with-zero are
different bytes. Reading is strict — a misspelled attribute or an undefined element is an error
naming the offender, rather than the silent degradation Magma's own XML loader does.

### Corrections to earlier revisions

Kept short, because the details are no longer useful — but worth recording so they are not
re-derived:

- The vtable-offset shift (see [Reading this page](#elf-vtable-offsets-are-shifted-by-8)) invalidated
  the old vtable map and every base-class relationship inferred from it.
- Keyframe states were modelled as one universal `RectState` plus a per-owner-widget "tail" of
  24/28/42/51/65 bytes. Those are five distinct `State` classes selected by `Factory::MakeState`.
- `Placeholder` was modelled as zero-byte in an element list, with a `container_type_name`-scoped
  exception for `Page`. It is never zero-byte there: like every other widget it is wrapped in a real
  `Element`, whose body it shares. (Its *own* widget body is empty, which is what the original
  observation was really about.)
- `Handler` was believed to be a real element type with a bespoke body — four successive models were
  built for it. It was an artifact: the `Placeholder` bug above desynced `controller.mgb` by one
  element, and the byte that looked like a `Handler` type slot was the middle of an `Image`'s body.
  `Handler` never appears as a live type byte anywhere in the corpus.
- The "global focus area" and "second area" are `StringTable` and `GenericObjectTable`; the earlier
  "two chained u32 reads" measurement was a correct observation of the empty case only.
- `LoadFontFamily` was modelled from a live byte-diff as a fixed 45-byte structure plus a UTF-16
  name. It is byte-identical to `LoadMaterial`; the 45 bytes were several adjacent records.
- Assorted per-file fallbacks (`type_slot == 10` → zero-byte, an `is_empty_type_slot` shortcut,
  hardcoded orphan slot lists) were all compensating for the above and are gone.

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
  parsed `<dependencies>` children, recursing only into nested `CMagmaConfigUIResource` entries, then
  as the final step calls `CMagmaUIResource::LoadPackageInMagma` with a literal `"UI\"` prefix. **The
  `.mgb` binary is always the last thing loaded for a given `.desc`**, after all its declared
  dependencies are satisfied, and resource `ID` paths in the XML are relative to a `UI\` root the
  loader prepends.
- **Two parallel visitor implementations** exist, selected by `magma::CFactoryNomad`:
  `BinaryLoadVisitor` (ctor `0x0a05edf0`, for `.mgb`) and `LoadVisitor` (ctor `0x0a064860`, for XML,
  built on `CMarkupSTL`). `magma::LoadVisitor::ReadPackage` @ `0x0a0688e0` parses a sibling XML schema
  whose keys (`PAGESIZE`, `DISPLAYOFFSET`, `MATERIALS`, `FONTSUBST`, `FONTS`, `REPLACES`, …) describe
  overlapping concepts — the same key names appear in `.mgb`, positionally rather than tag-named.

`CNavBarModule`/`CNavBarLayout`/`CNavBarButton`/`CNavBarPageHandler`/`CNavBarStack` are a **separate,
non-`magma`-namespaced hierarchy** that bridges into Magma rather than being part of it —
`CNavBarModule::OnActionSignal` and `CNavBarButton::SetIcon` both cross into Magma's own action/icon
systems. `CNavBarLayout::SetupNavBarButton` parses yet a third XML representation (`XmlNodeRef`). The
literal strings `"b_prompt1"`–`"b_prompt4"` and `"p_prompts_navbar"` exist in the binary as
element/`CStringID` names, matching the `.desc` sample's `<b_prompt1 show="1" text="Generic;ACCEPT" />`
elements exactly.

`AnonymousType` is **not** a widget/Element-tree class: it is magma's universal type-erased "any value"
property wrapper (a `std::any` equivalent). ~350 mangled symbols confirm every class's every reflected
field goes through `InternalGet*`/`InternalSet*(AnonymousType const&)`, and every `SpecificType<T>`
instantiation has `operator=`/`operator==(AnonymousType const&)`. Where its type byte legitimately
appears is a `FullLink`'s declared target type — a generically-typed reference with no more specific
type available.

## Unknowns

Everything needed to read (or write) a `.mgb` byte-exactly is resolved. What remains is semantic, not
structural:

- **Field semantics.** Many fields are decoded at the right width and position but their meaning is
  inferred from setter names rather than confirmed — e.g. `Button`/`CheckBox`'s 6 and 12 `u32`
  "timings", `Element`'s 3-bit category enum, and `Keyframe`'s `value`.
- **Header byte 13's flag**, and bytes 5–7 (consumed by the sentinel check but never examined).
- **`.desc`'s `crc_ID` attribute** — confirmed not to be a CRC32 that the `.mgb` load path checks
  anywhere, which rules out one hypothesis but not what it actually is. Plausibly a build-time-only
  cache key from Magma's asset pipeline, never re-verified at runtime.
- **~35 unresolved type-table hashes per file**, including `0x86F001E3`. Extensively hunted (≈900
  hand-guessed names, ~190 RTTI-recovered names, and a live `Register` hook capturing 98 real class
  names — no match for any of them). This no longer blocks anything: none of them ever appear as a
  live type byte, so they are table entries the shipped files never use.
- **`Dunia.dll`-only divergence.** All field layouts come from `FarCry2_server`'s portable code and
  were spot-checked live against `Dunia.dll` in one region of one file. The corpus-wide byte-exact
  result makes a divergence unlikely, but only that one window is directly verified.

## Reproducing this

```
tools/DuniaTools/bin/fc2_dunia/Gibbed.Dunia.Unpack.exe -v \
  "<Steam install>/Data_Win32/patch.fat" <output_dir>
```

Uses the `FCCU_FC2` project filelist to resolve real paths automatically. `patch.fat` is small (9.8 MB,
218 files) and contains one full localized set of
`ui\localized\{pc,pcwidescreen}\{eng,fre,ger,ita,spa,cze}\ui\*.mgb[.desc]` — the fastest archive to pull
`.mgb`/`.desc` samples from (`worlds.fat`/`common.fat` also contain UI resources but are far larger).

To find a given `.mgb`'s runtime hash (for an IDA breakpoint script), grep
`tools/JackAll/assets/fc2.hashlist` for `pcwidescreen\eng\ui\<name>.mgb` — that is the same hash space
as the `FileName` object `CMagmaUIResource::LoadPackageInMagma` reads, so no live discovery pass is
needed.
