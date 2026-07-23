---
sidebar_position: 5
---

# `.mgb` / `.mgb.desc` — Magma UI Format

Part of the file-formats note set — see [the engine overview](../engine-internals/overview.md)
for binary identification. Goal: reverse the menu/UI resource format that the [Almost Complete
Guide](../modding/guide/file-management.md) (§".mgb and .desc files") dismisses as "can only be
edited with a hex editor" — that claim turns out to be **wrong for `.desc`**, and this note corrects
it and traces the loader for both halves.

:::info[Verified via reverse engineering]
Two independent binaries were decompiled to produce this page — see the "cross-binary trick" note
below.
:::

## Status: `.mgb` format is now parseable — header, type dispatch, and widget/animation field layouts all resolved from `FarCry2_server`

Two independent investigations converged here: (1) unpacking real `patch.fat` UI files and reading
them directly (hex + text), and (2) decompiling the shared engine code in `FarCry2_server` (see
below — same cross-binary trick used in [the savegame format page](./savegame.md)). A series of
follow-up passes walked `BinaryLoadVisitor`'s vtable end-to-end: `ReadHeader` resolved every byte of
the header against the real `options.mgb` sample (see "Header — confirmed byte-for-byte"), and **every
format-relevant `Visit*` override was decompiled into a field-by-field record layout** — the full
base-class chain (`NamedObject`/`Area`/`Page`/`Element`/`Focusable`/`AreaInstance`), every leaf widget
type (`RectShape`, `Text`, `Image`, `ListBox`, `CheckBox`, `Window`, `Slider`, `EditBox`,
`PageInstance`, `Button`, `Cursor`, the `*Instance` forwarders), `UserData`'s generic property system,
and the keyframe/animation-`*State` records (see "Widget/record body" below). **`FarCry2_server` was
never exhausted in the technical sense** — every requested address decompiled cleanly across four
passes, no stripping/inlining was hit anywhere — but the investigation reached a natural scope
boundary instead: the remaining unmapped vtable region (`+0xf0`+) turned out to be a *different*
subsystem (Action-dispatch/reflection, not widget geometry), so it was deliberately not pursued. Two
functions (`VisitPosState`/`VisitRectState`) decompiled as decompiler-mangled this-adjustor thunks
rather than clean code, but their field data was still extractable. Everything found lives in the
**portable, cross-platform** part of `BinaryLoadVisitor` (the `Nomad` subclass only overrides platform
I/O), so it should apply unchanged to `Dunia.dll` if that binary is examined later.

**The last hard blocker — knowing which type-table ID in a file maps to which widget layout — is now
resolved too**: a file's raw type-table IDs turned out to be plain `CRC32(ClassName)` (see "Type-table
IDs are `CRC32(ClassName)`" below), independently re-verified byte-for-byte and cross-checked directly
against `options.mgb`'s real bytes (91/128 non-zero type-table entries matched by name, covering every
widget class this note documents a field layout for).

**`VisitPackage`'s own preamble — the very first thing read after the header, before any widget-tree
data — is now also decoded to full byte precision** (see "`VisitPackage` — full byte-exact preamble"
below): the config block, `Package`'s own generic `UserData` properties, `PAGESIZE`/`DISPLAYOFFSET`,
and full per-entry records for materials, font substitutions, font declarations, and font families,
down to the exact byte where the top-level `Area`/`Page` list begins. This was found mid-implementation
— while writing an actual parser against the previously-"shape only" description of `VisitPackage`,
it became clear the loop bodies weren't documented precisely enough to consume correctly, which would
have desynced every widget-tree byte after it even though the widget layouts themselves were right.

**A working implementation now exists and has been run against multiple real files** —
`tools/JackAll`'s `JackAll.Core.Format.Mgb*` classes (`MgbReader`, `MgbHeader`, `MgbBody`, tested in
`JackAll.Core.Tests/Mgb{Header,Body}Tests.cs` against the real `options.mgb` sample this whole note
uses, plus manually run against `360.mgb`, `common.mgb`, and `ingameeditor.mgb` pulled from a real
install). Writing and then running it against real files surfaced three real corrections to this spec,
and one strong independent confirmation:

- **Bug found in the implementation, not the spec**: `VisitUserData`'s own `NamedObject` base-call
  (documented in "Widget/record body" below) was initially missed when wiring up `VisitPackage`'s call
  into it, making `Package`'s own `UserData` count field read a stray hash as a property count instead
  — a good example of how even a fully-documented format is easy to get wrong in a first implementation.
- **New finding, not previously documented**: a type-index byte referencing a type-table slot whose raw
  `Id` is `0` (the header's own "left unresolved/skipped" case, see "Header" above) is a legitimate
  **null/empty element placeholder** when it appears in an `Area`'s element list or `Package`'s
  top-level area list — it consumes zero further bytes and constructs nothing. Confirmed empirically:
  `options.mgb`'s own top-level area list uses exactly this for 2 of its first 3 entries.
- **Bug found in the spec itself, not just the implementation**: `VisitUserData`'s `0x11`/`0x12`/`0x13`/
  `0x15` property types were originally documented as calling an opaque helper with unknown byte cost —
  an implicit "probably reads nothing" assumption that turned out to be wrong. Confirmed by running the
  parser against `ingameeditor.mgb`, whose `UserData` list genuinely uses one of these types and
  desynced immediately after it. Both helpers are now fully decompiled (`VisitFullLink`,
  `VisitStringResourceExternalId` — see "Reader-vtable slots" above) and the fix confirmed against the
  same file, which now parses its entire preamble cleanly.
- **Independent confirmation the spec is correct**: the parser decoded `PAGESIZE` as `1024 x 768` and
  the package's two `Material` records' texture paths as `\textures\common\option_sketch.png` and
  `\textures\common\brightness_lines.png` — **matching, byte-for-byte, the two `<CTextureResource>`
  entries in this exact file's own `.mgb.desc` sidecar** (`option_sketch.xbt`/`brightness_lines.xbt`,
  see "The file pair" above), found completely independently by two different parsing paths (XML text
  vs. decoded binary). `common.mgb` corroborated this further at larger scale: 54/54 of its own
  materials decoded with real, sensible texture names and zero garbage.

As expected given `MgbTypeTable`'s incomplete coverage (43/166 class names, see "Type-table IDs"
above), a full real file doesn't parse end-to-end yet. The parser is built to degrade gracefully at the
first unrecognized class (return everything decoded so far plus a clear "stopped here, this class isn't
recognized" marker) rather than throw the whole result away — see the parser's own doc comments for the
reasoning.

**A survey of all 21 real `.mgb` files shipped with the base game** (`tmp/mgbs/*.mgb`, not checked
into the repo — real game assets) found the gaps are extremely concentrated: two unresolved classes
together blocked 16/21 files (76%). Both were chased down:

- **`CRC32(name) = 0x202B3A09` → confirmed `AnonymousType`.** Found via a completely different angle
  than the string-matching that failed against `FarCry2_server`: `Dunia.dll` (the PC build) turned out
  to retain full **MSVC RTTI type-descriptor strings** (`.?AV<Class>@magma@@`) even though its function
  *names* are stripped — the opposite asymmetry from the Linux ELF build, and enough to extract a
  confirmed-complete list of 179 `magma::` class names. `AnonymousType` was in it; independently
  re-verified by CRC32 from scratch. Its exact field layout (byte-for-byte) is still **unconfirmed** —
  `MgbBody` currently guesses it's a bare `Element`-equivalent (no extra fields), which got one real
  file (`loading_sp_12.mgb`) parsing several areas deeper than before, but produced a different stopping
  error in another (`hud_mp.mgb`) and a new crash in a third (`loading_pre_loader.mgb`) — so this
  hypothesis is directionally right but likely not byte-exact. Worth revisiting with a real decompile of
  whatever `Visit*` override actually handles it, if one exists (it may not have its own — the "bare
  Element" guess implies it doesn't).
- **`CRC32(name) = 0x86F001E3` — still unidentified**, and this is now a confirmed dead end for static
  analysis: ~900 distinct candidate class names tried across three sessions and two binaries (the full,
  confirmed-complete 179-name `Dunia.dll` RTTI list included), zero matches. This is the single biggest
  remaining lever (13/21 files) but the RE agent's own assessment is that static Ghidra analysis is
  exhausted for this specific hash — the next step would be **live instrumentation**: patching/hooking
  the running game to print the class name at the point it resolves this ID (similar in spirit to the
  binary hex-patches already documented elsewhere in this repo), not more decompiling.

**What this investigation still hasn't done**: identify `0x86F001E3`, confirm `AnonymousType`'s exact
field layout, expand `MgbTypeTable`'s coverage closer to the 91/128 the RE investigation matched against
a larger, less-certain candidate list, or hand-simulate the full
widget-tree body logic against a real file's bytes by hand the way the header/preamble were — the
JackAll parser itself running further into real files (and, ideally, matching its own byte-consumption
to a file's
exact length) is now the practical substitute for that manual check.

## Correction to the community guide

`research/modding_guide.md` groups `.mgb` and `.desc` together as both requiring a hex editor.
**This is only true of `.mgb`.** The paired `.mgb.desc` file is **plain, well-formed XML** — confirmed
by extracting `ui\localized\pc\eng\ui\options.mgb.desc` from `patch.fat` (via
`tools/DuniaTools/bin/fc2_dunia/Gibbed.Dunia.Unpack.exe` against the `FCCU_FC2` project filelist) and
reading it directly: it opens with `<package>`, is Notepad++-editable, and needs no reversing at all.
Any modder wanting to edit UI *text bindings, nav-bar button prompts, or resource dependencies* should
edit `.desc`, not touch `.mgb` — this alone is a real, immediately actionable modding path that the
guide's "hex editor only" framing was hiding.

## IMPORTANT: the class hierarchy required a second binary — `FarCry2_server`, not `Dunia.dll`

Same situation as [the savegame format page](./savegame.md): searching `Dunia.dll` (PC, `0x10xxxxxx`
load range) for a literal `"MAGMA"` string turns up nothing — the magic is compared as a packed
32/40-bit immediate in code, not stored as an ASCII constant, so it can't be string-searched. The
**Linux dedicated-server binary `FarCry2_server`** (same Ghidra project, ELF, `0x08xxxxxx`–`0x0axxxxxx`
load range, GCC/Itanium-mangled, largely unstripped — see `overview.md`'s "second binary" section)
links the same shared engine UI code and retains real `magma::`-namespaced C++ symbols. All addresses
below are `FarCry2_server` addresses unless stated otherwise. **This session did not re-check the
PC-side `Dunia.dll` addresses** — the Ghidra CodeBrowser tab active during this investigation was
`FarCry2_server`; there was no tool available to switch tabs. A future session with the `Dunia.dll`
tab active could try to locate the equivalent `0x10xxxxxx` functions directly.

## The file pair, from a real sample (`ui\localized\pc\eng\ui\options.mgb` / `.mgb.desc`)

`.desc` (2,585 bytes) — full plain-text XML, e.g.:

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
		<!-- ...more per-screen navbar/button-prompt overrides... -->
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

This confirms the internal class names directly from shipped data: `CMagmaConfigUIResource`,
`CMagmaUIResource`, `CTextureResource`. The `<dependencies>` tree is a literal, human-readable
dependency manifest — `.desc` doesn't just describe text, it also declares which other `.mgb.desc`,
`.mgb`, and `.xbt` files this screen needs loaded. **`crc_ID` values were not confirmed to be a plain
CRC32 of the `ID` path string** — several candidate CRC32 variants (standard IEEE poly, ASCII path,
with/without backslash normalization) were tried by hand and none matched; this is an open question,
not a solved one (see "Not yet traced" below).

`.mgb` (60,697 bytes) — binary, starts:

```
000000  4d 41 47 4d 41 cd 00 00  ab 90 ab 1e 00 00 a7 00   MAGMA...........
000010  00 00 00 00 00 00 00 e3  01 f0 86 ac cb 92 83 29   ................
```

- Magic: `4D 41 47 4D 41` = ASCII `"MAGMA"` (5 bytes, not a 4-byte FourCC like `.xbm`/`.xbg`'s
  reversed-FourCC scheme — this is a different, unrelated serialization convention in the same engine).
- The header layout is now confirmed byte-for-byte — see "Header — confirmed byte-for-byte" below.
- Past the header (file offset `0x2A7` for this sample), the widget/record body is a mix of IEEE-754
  floats (e.g. `00 00 80 3F` = `1.0`, `CD CC 4C 3E` = `0.2`, `00 00 B4 42` = `90.0` — plausible widget
  alpha/position/angle values), occasional runs of `00` bytes, and embedded content near the tail
  including **UTF-16LE digit-tick strings** (`"0" "1" "2" ... "9"`, matching a brightness slider's
  numeric labels — consistent with the `MAINMENU_OPTION_BRIGHTNESS` XML section above) and a raw
  back-reference path string `\common.mgb` (i.e. `.mgb` files can reference sibling `.mgb` files by
  path, not just by the `.desc` dependency tree). The reader interface confirmed in `VisitPackage`
  (below) explains this mix: it's a sequence of typed reads (4-byte values, ints, u16s, bytes, bools,
  raw byte buffers), not a fixed C struct — field order matters, not alignment.

## Header — confirmed byte-for-byte (`magma::BinaryLoadVisitor::ReadHeader` @ `0xa05fef0`)

Found by walking `BinaryLoadVisitor`'s vtable (`PTR_vtable_0xa40945c`, a GOT-style indirection to the
real vtable array at `0xa3fc300`; the `Nomad` subclass's own vtable is at `0xa3d1ca0`, and per the
Itanium ABI the object's vptr slot N sits at `vtable_addr + 8 + N`). Slot `+0x130` is
`Open(FileName const*)` @ `0xa060070`, called from `magma::Engine::LoadPackage` @ `0xa03fc90`
immediately after the visitor is constructed; it allocates a `ReadFileArchive` and calls
`ReadHeader()`. Decompiled and checked directly against `options.mgb`'s real bytes
(`4d 41 47 4d 41 cd 00 00 ab 90 ab 1e 00 00 a7 00 ...`):

| Offset | Bytes (this file) | Field | Confirmed meaning |
|---|---|---|---|
| `0-4` | `4D 41 47 4D 41` | magic | `"MAGMA"`, compared with a manual 5-byte loop — mismatch → error code `4` |
| `5-8` | `CD 00 00 AB` (read as one 4-byte value) | sentinel | only byte `8` (`0xAB`) is actually checked — `!= 0xAB` triggers a fallback reader path, error `6` if that also fails. Bytes `5-7` (`CD 00 00`) are consumed but not examined by this check — meaning still open |
| `9-12` | `90 AB 1E 00` → LE u32 `0x1EAB90` | **format/build version** | must equal exactly `0x1EAB90` (2,010,000 decimal) or load fails with error `5` — **the same check, and the same error message** ("This package was saved using a more recent version of Magma...") as the *XML* loader's `magma::LoadVisitor::VisitPackage` @ `0xa06a370`. `.mgb` and `.mgb.desc` share one version epoch, not two independent ones |
| `13` | `00` | flag byte | read via the reader-vtable's `+0x24` (`ReadBool`) slot; `0x00` in this sample. Purpose not pinned down — plausibly a compression/format-variant flag, unconfirmed |
| `14` | `A7` = 167 | **type-table entry count** | a single byte, not a u16 — the byte at offset `15` (`00`) is actually the *first* byte of the type table that follows, not a second count byte (this corrects the original hex-shape guess of "u16 `0x00A7`") |
| `15 .. 15+4×166` | — | type table | 166 raw LE u32 IDs (count `-1`) — **each ID is `CRC32(ClassName)`, see "Type-table IDs" below** — looked up via `objecttypemanager::GetTypeIdFromId` (a flat linear scan, exact match, against a table built at startup from every registered class) into a 255-byte remap array at `this+0x34` (zeroed in the `BinaryLoadVisitor` ctor). An `Id == 0` entry is left unresolved/skipped |

Header ends at file offset `15 + 166×4 = 679` (`0x2A7`), where the widget/record body begins (read by
`VisitPackage` and the per-widget-type `Visit*` overrides below).

**Reader-interface vtable** (used throughout `ReadHeader`/`VisitPackage`, offsets relative to the
reader object, not `BinaryLoadVisitor` itself): `+0x8` = `ReadValue` (generic 4-byte/float read),
`+0xc` = `ReadInt`, `+0x10` = `ReadU16`, `+0x1c` = `ReadByte`, `+0x24` = `ReadBool`, `+0x28` =
`ReadBytes(buf, len)` (paired with a `RequestBuffer`/`ReleaseBuffer` pattern for string/blob reads).

### `VisitPackage` — full byte-exact preamble (@ `0xa0619e0`)

This is the section that stood between "documented format" and "working parser" — it's the very first
thing read after the header/type-table, before any `Area`/`Page`/`Element` tree data, so getting it
wrong desyncs everything after it. Traced every reader-touching call in strict execution order
(confirmed the ~80 `this->vtable+0x140..+0x27c` calls scattered through the function are **not**
reads — they forward already-buffered locals to `Package` property setters and consume zero bytes).
Byte-exact, in order:

```
[260 bytes]  65× reader.+0x8, chained — a fixed config block (the binary counterpart of the .desc
             XML's <configuration> section: MAINMENU_OPTION_BRIGHTNESS etc. — positional here instead
             of tag-named). Forwarded afterward to ~39 Package setters; opaque/skippable if per-field
             semantics aren't needed.
[variable]   VisitUserData(this) — Package's own generic key/value property list, via the
             already-documented VisitUserData record format (see "Widget/record body" below).
[4 bytes]    PAGESIZE:      2× reader.+0x10 (u16 width, u16 height)
[4 bytes]    DISPLAYOFFSET: 2× reader.+0x10 (u16 x, u16 y)
[8 bytes]    2× reader.+0xc: u32 materialCount, u32 <unknown - forwarded to a setter, not a loop count>
  × materialCount, each a full Material record (VisitMaterial @ 0xa0606a0, newly decompiled):
    [4 bytes]    reader.+0xc  → u32 nameHash        (VisitNamedObject)
    [4 bytes]    reader.+0xc  → u32 texNameLen
    [texNameLen] reader.+0x28 → raw ANSI bytes, ONLY IF texNameLen != 0 → Material::LoadTexture
    [16 bytes]   4× reader.+0x20 (float×4) → Material::SetRegion (a UV/region rect)
[4 bytes]    reader.+0xc → u32 fontSubstCount
  × fontSubstCount, each: [byte typeId][u32 len1][len1 bytes][u32 len2][len2 bytes]
             (byte via +0x1c; both strings via +0xc length + +0x28 bytes; a Font-type recursion
             happens between the two strings but reads zero further bytes - see below)
[4 bytes]    reader.+0xc → u32 fontDeclCount
  × fontDeclCount, each: [byte typeId][u32 len1][len1 bytes][u32 len2][len2 bytes]
             (same shape as fontSubst, but both strings come BEFORE any recursion, not between)
[4 bytes]    reader.+0xc → u32 fontFamilyCount
  × fontFamilyCount, each a FontFamily record (VisitFontFamily @ 0xa0615a0, newly decompiled):
    [4 bytes]    reader.+0xc → u32 nameHash — the ENTIRE record, nothing else is read
[4 bytes]    reader.+0xc → u32 areaCount
  × areaCount, each: [byte typeId][full VisitArea/VisitPage record - see "Widget/record body" below]
             ← this is the exact handoff point from "package preamble" into the widget tree
[1+ bytes]   reader.+0x24 (bool "has global focus area?") → if true: [byte typeId][VisitArea/VisitPage
             record] - a named special area, NOT counted in areaCount above
[1+ bytes]   reader.+0x24 (bool "has second area?") → if true: same shape again
[4+ bytes]   reader.+0xc → u32 defaultMaterialNameLen; if != 0: reader.+0x28 → raw ANSI bytes →
             Package::FindMaterial/SetDefaultMaterial
--- end of file-consuming reads ---
```

Everything after this (`ResolveLinks`, `InstanceCountVisitor`/`AreaCloningCountProcess`/
`DuplicateRecursiveVisitor`/`DuplicateAreaVisitor`, ~40 more setter calls with a literal `0`) is a
**purely in-memory** post-process over the already-parsed tree (element/area duplication for repeated
template rows) — reads **zero** further bytes, so a parser can stop the moment the optional
default-material string above is consumed.

**Confirmed byte-inert recursion**: both the font-substitution and font-declaration loops above
recurse into `Font::Accept` (`BinaryLoadVisitor` has no `VisitFont` override — only the no-op
`magma::Visitor::VisitFont` is reachable), so that step reads nothing; a parser can skip it entirely
without losing sync.

**Reader-interface vtable** (used throughout `ReadHeader`/`VisitPackage`, offsets relative to the
reader object, not `BinaryLoadVisitor` itself): `+0x8` = `ReadValue` (generic untyped 4-byte read),
`+0xc` = `ReadInt` (u32), `+0x10` = `ReadU16`, `+0x1c` = `ReadByte`, `+0x20` = `ReadReal` (float),
`+0x24` = `ReadBool` (1 byte — confirmed by the header's byte-13 gap), `+0x28` =
`ReadBytes(buf, len)` (paired with a `RequestBuffer`/`ReleaseBuffer` pattern for string/blob reads).
`+0x14`/`+0x18` (probable `ReadU16`/`ReadByte` overloads) only appear in leaf widget/state records,
not here — see "Widget/record body" below.

The **same key names** (`PAGESIZE`, `DISPLAYOFFSET`) appear in the sibling XML schema parsed by
`magma::LoadVisitor::ReadPackage` (see "Load flow" below) — confirming `.mgb` and its XML-config
sibling schema describe overlapping concepts even though they're parsed by unrelated code paths.

**No CRC32 is *called* while reading a `.mgb` file.** `GetTypeIdFromId`'s own lookup at load time is a
plain linear exact-match scan against a table built once at engine startup, not a hash computed
per-read. But (see immediately below) the *values* being matched **are** CRC32 output, computed
ahead of time — at class-registration time, not at file-parse time. This doesn't resolve the separate
open question below about `.desc`'s `crc_ID` attribute — that's a different value (a path-string hash
in the XML sidecar), and the algorithm/target string for it are still unconfirmed even though the
primitive itself (standard CRC32) is now known to exist in this exact subsystem.

## Type-table IDs are `CRC32(ClassName)` — the widget-class dispatch, resolved

This was the actual blocker for building any parser: field layouts for every widget type were known
(see "Widget/record body" below), but not which type-table ID corresponds to which layout. Resolved by
decompiling three more functions in `FarCry2_server`:

- **`magma::Id::Hash(char const*)`** @ `0xa0782a0` — a textbook CRC-32 (`crc_table` built from
  polynomial `0xEDB88320`, the same IEEE 802.3 CRC-32 already used elsewhere in the engine for
  `GetNameHash`/`CRC32_Hash`, per `overview.md`). **`Id::Hash(name)` is exactly `binascii.crc32(name.encode())`**
  — plain ASCII class name, no namespace prefix, no C++ mangling.
- **`magma::objecttypemanager::Initialize()`** @ `0xa0767f0` — for every class registered via
  `Register()`, computes `hashMap[Id::Hash(typeInfo->GetName())] = typeIndex` once at startup. This is
  what `GetTypeIdFromId` scans at file-load time.
- **`magma::objecttypemanager::Register(ObjectTypeInfo const*)`** @ `0xa075fe0` — assigns each class's
  compact `typeIndex` (the byte later stored in `BinaryLoadVisitor`'s `this+0x34[]` remap array) as
  simply **the next free slot, in call order** (`typeArray[registeredCount++] = param_1`) — i.e.
  `typeIndex` is this one binary's link order (C++ static-initializer / `.init_array` order), **not**
  a portable constant. Good news: a parser never needs it — a file's raw type-table `Id`s can be
  resolved straight to class names via the static CRC32 dictionary below, without caring what
  `typeIndex` any particular build assigned internally.
- **`Factory::MakeElement`** @ `0xa0481a0` dispatches by **`ObjectTypeInfo*` pointer identity**
  (walking the class-ancestor chain against ~13 fixed category sentinels), not a literal
  index/switch table — so there's no separate numeric dispatch table to extract here either; the
  CRC32(name) lookup is the whole story.

**Consequence for a parser**: read each of the file's 166 raw type-table `u32` IDs, look each up in a
static `{CRC32(name): name}` dictionary, and that directly names the element's class — no dependence
on any particular build's link order, so it works unchanged across every `.mgb` file and (should)
carry over to `Dunia.dll`'s PC build too.

**Verified two ways**: (1) cross-checked directly against `options.mgb`'s own real type table (extracted
from `FarCry2_server`'s RTTI name strings, CRC32'd, and matched against the file's 166 raw IDs at file
offset `0x0F`–`0x2A6`) — **91 of 128 non-zero entries matched (71%), including every widget/state class
this whole investigation named from the `BinaryLoadVisitor` vtable**, confirming the CRC32 hypothesis
and the earlier vtable-slot naming in one shot. (2) Independently re-implemented the same CRC-32
(IEEE, poly `0xEDB88320`, ASCII name bytes) from scratch and reproduced all of the hashes in the table
below byte-for-byte.

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

This table alone covers every widget/state/base class documented in "Widget/record body" below — enough
to build a working parser for everything this note set has already reverse-engineered.

**37 of 128 non-zero entries in the sample file's type table remain unmatched by name.** The whole
`ActionExecuter` family was confirmed present in the binary (matching the `+0xf0`+ vtable region from
the previous pass — `ActionExecuter`, and per-subtype variants for `Event`/`Inputable`/`Focusable`/
`Page`/`Editbox`/`Listbox`/`PageInstance`/`Slider`) along with a whole action-scripting opcode set
(`Action`, `ActionStop`, `ActionContinue`, `ActionPushPage`, `ActionPopPage`, `ActionGotoKeyFrame`,
`ActionGotoFrameIndex`) and assorted infrastructure classes (`EngineObject`, `AnonymousType`,
`PageFocusable`, `WindowSection`, `StretchableWindowSection`, `AreaLink`, `DrawHandler`,
`EventHandler`, `Handler`, `TimingStrategy` and its `Tick`/`Sync`/`No`/`EventTriggered` variants,
`GenericObject`, `UserDataItem`, `GlyphFont`, `PixmapFont`, `ExternalFont`, `Texture`,
`StringResource`, three `TextScroller*Handler` classes, `DisplayConfiguration`, `IScrollable`,
`Checkable`, `Radioable`, `Acceptor`, `FullLink`) — but exact hashes for these weren't computed this
pass (the concatenated `ActionExecuter*` names in particular have ambiguous exact spelling from the
report that named them, e.g. `ActionExecuterListbox` vs. `ActionExecuter_Listbox`, and shouldn't be
hashed without confirming the literal string first). **The methodology to close this gap is proven and
mechanical** — extract more RTTI name strings from the binary (or confirm exact spelling via
`list_strings`), CRC32 them, match against remaining unmatched IDs — just not exhaustively completed.

## Widget/record body — vtable map and field layouts (`FarCry2_server`)

Everything below is from decompiling `magma::BinaryLoadVisitor`'s full vtable (base object vptr
`0xa3fc308`; the `Nomad` subclass's vptr is `0xa3d1ca8`). **The Nomad subclass overrides only
platform I/O** (`Open`/`ReadHeader`/`ReadFileArchive`) — every widget-field-reading method below lives
in the portable base class and is shared, unmodified, with the PC build (i.e. this vtable map should
transfer directly to `Dunia.dll` once/if that binary is examined). Confirmed by walking `get_xrefs_from`
at `vptr + slot_offset` for every 4-byte slot from `+0x00` to `+0x9c`.

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

The vtable continues past `+0x9c` — `+0xec` is now resolved (see below); there is more real headroom
beyond `+0x9c` in general, not a dead end, but `+0xec` itself is the one slot `VisitElement`, `VisitArea`,
and `VisitKeyframe` all actually call.

**`+0xec` = `VisitActionCaller`** @ `0xa05e910`: `1×+0x24` (bool "has action executer?") → if true:
`1×+0x1c` (byte type-id, resolved via the header's type table) → `Factory::MakeActionExecuter` →
recurse `Accept` → `ActionCaller::SetActionExecuter`. This is the hook that attaches one of the
engine's action-dispatcher handlers to any `Area`/`Element`/`Keyframe` — all three call it before
their own fields, which is why it kept showing up unmapped in earlier passes.

### Reader-vtable slots (the interface each `Visit*` reads through, not `BinaryLoadVisitor`'s own vtable)

Confirmed so far: `+0x8` = `ReadValue` (generic 4-byte/float), `+0xc` = `ReadInt` (u32), `+0x10` =
`ReadU16`, `+0x1c` = `ReadByte` (u8), `+0x24` = `ReadBool`, `+0x28` = `ReadBytes(buf, len)` (raw/ANSI
bytes). Newly found this pass:

- **`+0x2c` = `ReadUTF16Chars(buf, charCount)`** — distinct from `+0x28`'s narrow-byte read;
  confirmed in `VisitTextBase`'s inline-literal-text path, paired with `StringUtil::ToUnicode`. This
  is the counterpart to the UTF-16LE digit-tick strings spotted by hand in the raw hex dump.
- **`+0x18`** — seen in `VisitListBox` (single byte) and later in `VisitTextState`/`VisitImageState`/
  `VisitRectShapeState` (also byte-sized). Byte-stream-identical to `+0x1c`/`+0x24` as far as could be
  told — most likely a separate compiler-generated overload of one templated `Read<T>` rather than a
  semantically distinct operation. Flagged, not asserted.
- **`+0x20` = likely a dedicated `ReadReal`/`ReadFloat`** — the original guess ("wider 8-byte
  int/double read", from its single `VisitUserData` type-tag-`7` appearance) was superseded once this
  slot turned up repeatedly for genuinely float-shaped fields while decoding the keyframe/`*State`
  records below (`RotationState`'s angle, both `ScaleState` components, `ImageState`'s 4-value block).
  It's a *separate* slot from `+0x8`'s untyped-POD read despite both being 4 bytes on the wire —
  evidence the reader interface distinguishes typed float reads from generic/int reads at the API
  level, not just by call-site casting.
- **`+0x14`** — seen in `VisitTextState`/`VisitImageState`/`VisitRectShapeState`, writing a u16-sized
  destination. Byte-stream-identical to `+0x10` as far as could be told; same "probably a template
  overload" caveat as `+0x18` above. **Confirmed as u16 by a second, independent call site** — see
  `VisitFullLink` immediately below.

Two **visitor-own** vtable slots (on `BinaryLoadVisitor` itself, not the reader) also showed up,
both called only from inside `VisitUserData` for its "external string resource" property types
(`0x11`/`0x12`/`0x13`/`0x15`) — **initially assumed to read nothing further** (a guess, not a
decompiled fact), which turned out to be wrong and cost real desync bugs once an actual C# parser
(`tools/JackAll`) hit a real file using these property types (see the "implementation" note under
"Status" above). Both are now fully decompiled:

- **`this->vtable+0xb0` = `magma::BinaryLoadVisitor::VisitFullLink`** @ `0xa0604d0` — for property
  types `0x11`/`0x12`/`0x15`. Reads `1×+0x14` (u16 `entryCount`) → `1×+0x1c` (byte `typeId`, resolved
  via `objecttypemanager::GetType`, reserving the link's capacity) → loop `entryCount` times: `1×+0xc`
  (u32 `id`) → `FullLink::AddId`. **Wire record: `[u16 count][byte typeId][count × u32 id]`
  = `3 + 4×count` bytes** — not the 0 bytes originally assumed.
- **`this->vtable+0xe4` = `magma::BinaryLoadVisitor::VisitStringResourceExternalId`** @ `0xa05feb0` —
  for property type `0x13`. Unconditional `2× +0xc` (u32, u32), no branching. **Wire record: `8`
  bytes** — also not the 0 bytes originally assumed.

**Reading pattern**: every `Read*` method returns `this`, so multi-field reads compile to one chained
expression (`reader->Read(&a)->Read(&b)->Read(&c)`) — worth recognizing at a glance in any further
decompiles of this binary.

### Per-type field sequences

Base classes first (most types build on these), then leaf widget types:

- **`VisitNamedObject`** (base of everything, `0xa05f840`): `1×+0xc` (u32) → stored at `param+8` —
  the object's **name-hash ID** (same `GetNameHash`/CRC32 scheme used elsewhere in the engine), not a
  literal string.
- **`VisitArea`** (base of Page, `0xa05f4b0`): `VisitNamedObject` → bookkeeping → unmapped `+0xec`
  slot → `AssignActionsParent` → 3× chained `+0x8` (**ticks-denominator, duration-multiplier,
  elementCount**) → `Area::SetTime`/`ReserveNbElement` → loop `elementCount` times: `1×+0x1c` (u8
  type-id, resolved through the header's type table) → `Factory::MakeElement` → child's own
  `Accept(visitor)` (double-dispatch recursion into the matching `Visit*` below) → after the loop: 4×
  chained `+0x10` (u16 each — a static bounding-box `Rect2D`, `Area::SetStaticBox`).
- **`VisitPage`** (`0xa05fd60`): `VisitArea` (base) → `1×+0xc` (u32 tag count) → loop: `1×+0x1c`
  (byte tag) then `1×+0xc` (u32 value) → `Page::AddDefaultElementTag` → `1×+0x24` (bool) →
  `Page::SetGlobalSelectionMode`.
- **`VisitWidget`** — confirmed a pure no-op; `Widget` itself adds zero serialized fields, all real
  widget data flows through `VisitElement` instead.
- **`VisitElement`** (true base of RectShape/Text/Image/etc., `0xa060290`): `VisitNamedObject` →
  unmapped `+0xec` slot → `AssignActionsParent` → 2× chained `+0x24` (bool: hidden-flag, inverted into
  `SetVisible`; and a second flag OR'd into `param[0x2e]` bit 0) → `1×+0x8` (u32, low 3 bits →
  `param[0x2d]` bits 4-6, an enum/category) → `1×+0x8` (u32 keyframe count) → loop: construct + recurse
  `Accept` per `Keyframe` (`VisitKeyframe`, decoded below in "Keyframe / animation-state records").
- **`VisitFocusable`** (`0xa05fc80`): base-slot `+0x70` (`VisitElement`) → `1×+0xc` (u32 neighbor-tag
  count) → loop: `1×+0x1c`, `1×+0x1c`, `1×+0xc` per entry → `Focusable::AddNeighborTag` → `1×+0x24`
  (bool/byte) → `Focusable::SetInputController`.
- **`VisitAreaInstance`** (`0xa060a80`): base-slot `+0x18` (`VisitWidget`, no-op) → `1×+0xc` (u32
  string length) → raw UTF-16 via reader-slot `+0x2c` → `ToUnicode` → assigned as an instance
  name/label → `LoadMaterial(this)` helper (material/texture reference) → `1×+0x24` (bool) →
  optional nested `Accept` recursion → `1×+0xc` (u32) → `param->vtable+0xa8`.
- **`VisitAutonomousAreaInstance`**, **`VisitButtonInstance`**, **`VisitCheckBoxInstance`**,
  **`VisitRadioButtonInstance`** are all **pure forwarders** — each calls exactly one base-vtable
  slot and reads no fields of its own (`ButtonInstance → AutonomousAreaInstance → AreaInstance`,
  `RadioButtonInstance → CheckBoxInstance`). Concretely: `b_prompt1`-style navbar button widgets carry
  no binary payload beyond plain `AreaInstance`'s.
- **`VisitRectShape`** (`0xa05db40`): 2× chained `+0x24` (bool, packed into `param[0x19]` bits 0-1) →
  `1×+0x8` (u32/float → `param[0x18]`, single scalar — likely rotation or corner-radius).
- **`VisitTextBase`** (`0xa0616d0`, the richest one): base-slot `+0x18` (`VisitWidget`, no-op) →
  `1×+0x24` (bool **mode flag**): if `1` → 2× chained `+0xc` (u32, u32 = **StringTable ID + key
  hash**, i.e. a localized-string reference via `param->vtable+0xb8`); else → `1×+0xc` (u32 charCount)
  → raw UTF-16 via reader-slot `+0x2c` → `ToUnicode` → literal inline text assigned to `param+0x18`.
  Then unconditionally: 2× chained `+0x8` (an offset pair, `param+0x1c`/`param+0x20`) → 4× chained
  `+0x24` (bool ×4, packed into `param[0x38]` bits 0-3 — alignment/style flags) → `1×+0x24` (bool,
  "has explicit width?"): if true → `1×+0xc` (u32 → `param->vtable+0xa4`, likely wrap-width/max-length).
- **`VisitText`** (`0xa0610e0`): base-slot `+0x1c` (`VisitTextBase`) → `LoadFontFamily(this)` helper →
  3× chained `+0x24` (bool ×3, packed into `param[0x48]` bits 0-2) → `1×+0x8` (→ `param[0x50]`) →
  `1×+0x24` (→ `param[0x51]` bit 0).
- **`VisitImage`** (`0xa060e80`): `LoadMaterial(this)` helper (texture reference) → `1×+0x8` (→
  `param[0x18]`) → `1×+0x24` (→ `param[0x1a]` bit 0) → 2× chained `+0x8` (packed into `param[0x19]`).
- **`VisitListBox`** (`0xa05f680`): base-slot `+0x18` (`VisitWidget`, no-op) → `1×+0x18` (new/
  unconfirmed slot) → 4× chained `+0x24` (bool ×4) → `1×+0x1c` (byte) → `1×+0x8` — flags packed into
  `param[0x19]`/`param[0xd8]`, scalar into `param[0x18]` → `ListBox::UpdateMetrics` → `1×+0x24`: if
  true → `1×+0xc` (u32) → `param->vtable+0xa4` → 3 more independent `+0x24` bool checks, each guarding
  an optional nested `Accept` recursion at `param+0x34`/`param+0x50`/`param+0x6c` (three optional
  sub-objects — plausibly header row, scrollbar, footer).
- **`VisitCheckBox`** (`0xa05df20`): base-slot `+0x58` (`VisitArea`) → a fixed loop of **12× `+0x8`**
  reads into `param+0x54[0..0x2c]` — a 12-float array (plausibly 3 states × RGBA, or icon-state
  geometry — unconfirmed which).
- **`VisitUserData`** (`0xa062c90`, the generic key/value property system —
  `magma::genericproperty::Type`): base-slot `+0xc` (`VisitNamedObject`) → `1×+0xc` (u32 property
  count) → loop: `1×+0xc` (u32 property-name-hash key) → `1×+0xc` (u32 type tag, 0-0x15) →
  `switch(tag)`: types `{0,1,3,4,5,6,8,9,10,0xb,0xd,0xe,0xf}` read no extra payload; type `2` →
  `1×+0x8` (float); type `7` → `1×+0x20` (a second, differently-typed float/real read — see
  "Reader-vtable slots" above); type `0xc` → `1×+0x24` (bool); type `0x10` →
  `1×+0xc` (length) then either an external-string-table reference or `+0x28` raw ANSI bytes; types
  `0x11`/`0x12`/`0x15` → `VisitFullLink` (`[u16 count][byte typeId][count × u32 id]`, see
  "Reader-vtable slots" above — this and the next were originally assumed to read nothing, corrected
  after a real implementation hit a real file using them); type `0x13` → `VisitStringResourceExternalId`
  (`[u32][u32]`, unconditional); type `0x14` → null. Result: `UserData::AddUserData(param, key,
  variant)`.

### Remaining leaf widgets

- **`VisitWindow`** @ `0xa060d70`: base `+0x18` (`VisitWidget`, no-op) → `2×+0x24` (bool, bool →
  `param[0x100]`, `param[0x101]` — likely "stretch horizontally"/"stretch vertically") → calls
  `Window::GetWindowSection(param_1, N)` for `N=0..8` (9 sections — a classic **9-slice/9-patch**
  border layout: 4 corners + 4 edges + center), each fed to an unexplored
  `ReadStretchableWindowSection`/`ReadWindowSection` helper (sections 0, 5, 6, 7, 8 stretchable; 1-4
  plain) — the helper bodies weren't decompiled, but their names alone confirm the 9-patch structure.
- **`VisitSlider`** @ `0xa05eb10`: base `+0x18` (`VisitWidget`, no-op) → **5× chained `+0x8`** → `1×
  +0x24` (bool) → `Slider::SetRange(param, v1, v2)` (**min, max**), `v3` → likely step/increment,
  `v4`/`v5` → `param+0x10c`/`param+0x18`, bool → `param[0x1c]` bit 0. Then 3 independent `+0x24` bool
  checks, each guarding an optional nested `Accept` recursion at `param+0x54`/`+0x70`/`+0x8c` — same
  optional-child pattern as `ListBox`'s header/scrollbar/footer (track/thumb/button-style children).
- **`VisitEditBox`** @ `0xa05ec80`: base `+0x18` (`VisitWidget`, no-op) → `1×+0x8` (u16-sized →
  `param+0x18`, likely max-length) → `1×+0x24` (bool "has password char?"): if true → `1×+0x2c`
  (`ReadUTF16Chars`, count 1 → single wchar) → `EditBox::SetPasswordChar` → 2 more independent `+0x24`
  bool checks guarding optional `Accept` recursion at `param+0x58`/`+0x74` (text-display area and a
  cursor/caret element, presumably).
- **`VisitPlaceholder`** @ `0x09606ae0` and **`VisitAreaLinkTags`** @ `0x09606b00` — both confirmed
  **not overridden** (`magma::Visitor::VisitPlaceholder`/`VisitAreaLinkTags`, pure no-ops). A
  placeholder is just a layout slot with zero serialized fields.
- **`VisitPageInstance`** @ `0xa05f3f0`: base `+0x40` (`VisitAreaInstance`) → `1×+0xc` (u32 count) →
  loop: `2×+0x1c` (byte, byte) + `1×+0xc` (u32) per entry → `PageInstance::AddDefaultFocusTag` —
  structurally identical to `VisitPage`'s own tag loop, just calling `AddDefaultFocusTag` instead of
  `AddDefaultElementTag`.
- **`VisitButton`** @ `0xa060410` (non-instance): base `+0x58` (`VisitArea`) → a fixed loop of
  **6× `+0x8`** into `param+0x54[0..0x14]` — the same fixed-float-array pattern as `VisitCheckBox`'s
  12-float block, half the size (6 vs 12); plausibly 2 states × RGBA-minus-2, or a smaller per-state
  color/geometry set specific to plain `Button`.
- **`VisitCursor`** @ `0xa05dec0`: base `+0x58` (`VisitArea`) → 2× chained `+0x10` (u16, u16, stored
  **negated** → `param+0x5a`, `param+0x58`) — a signed X/Y **hotspot offset**, negated on load
  (matches "cursor hotspot": the offset subtracted from the cursor's click point).

**`VisitFont`/`VisitStringTable` thread closed**: neither exists as a real override inside
`BinaryLoadVisitor`'s vtable range — both names exist in the binary, but attached to unrelated visitor
classes (`TextCompatibilityVisitor`, `CloningVisitor`, etc.); the one same-neighborhood candidate
(`VisitFont @0x09606ba0`) decompiles to the generic no-op base. Confirms fonts/font-substitution are
read inline inside `VisitPackage`'s own font loop (see "Header" section above), not via double-dispatch.

**Vtable slots `+0xf0`–`+0x120` swept and resolved, but out of scope**: `VisitActionExecuter` and one
override per concrete `ActionExecuter` subtype (`Inputable`/`Focusable`/`Page`/`Editbox`/`Listbox`/
`PageInstance`/`Slider`), plus `VisitUserDataItem`/`VisitGenericObjectTable`/`VisitGenericObject`/
`VisitTickTimingStrategy`. All resolved cleanly (no gaps) — but this is the **Action-dispatch/reflection
subsystem** feeding `ActionCaller`/`ActionExecuter`, a different format from `.mgb`'s widget geometry.
Not pursued further; flagged here so a future session doesn't re-discover the same boundary.

### Keyframe / animation-state records

The payload of `VisitElement`'s per-element keyframe loop (see above) — this is the actual
position/size/color **animation data** attached to a widget.

- **`VisitKeyframe`** @ `0xa05ea90`: `VisitNamedObject` → `VisitActionCaller` (`+0xec`) →
  `AssignActionsParent` → 2× chained `+0x8`: first → `param+0x18` (u16, **time**), second →
  `param+0x1c` (u32, **value**) → dispatches `Accept(visitor)` on a nested object at `param+0x14` — a
  `State` sub-record. **The concrete `State` subtype isn't chosen by a byte read inside `VisitKeyframe`
  itself** — it comes from `Factory::MakeKeyframe`'s `ObjectTypeInfo` argument, already resolved by
  `VisitElement`'s caller before the `Keyframe` is even constructed. In other words: **the animated
  property's type is fixed by the Element's own metadata, not re-declared per keyframe.**
- **`VisitState`** (shared base) @ `0xa05dc90`: 2× chained `+0xc` (u32, u32 → `param+8`, `param+0x10`).
  Every concrete `*State` below calls this first. Notably reads via `+0xc` (`ReadInt`), not `+0x8` —
  purpose not namable from this function alone; likely interpolation/ease metadata rather than time,
  since time already lives on the owning `Keyframe` at `+0x18`.
- **`VisitRotationState`** @ `0xa060460`: base `+0x78` (`VisitState`) → `1×+0x20` (float →
  `param+0x18`, **rotation angle**) → 2× chained `+0x10` (u16, u16 → `param+0x1c`, `param+0x1e`,
  **pivot X/Y**). Matches expectation exactly: rotation-shaped (angle + pivot point).
- **`VisitScaleState`** @ `0xa05dcd0`: base `+0x80` (`VisitState`) → 2× `+0x20` (float, float →
  `param+0x2c`, `param+0x30`, **scaleX, scaleY**). Exactly scale-shaped, as predicted.
- **`VisitPosState`** @ `0xa060180` and **`VisitRectState`** @ `0xa05fc20` — **both decompiled as
  this-adjustor thunks** (`__regparm1` calling convention, raw `in_stack_NNNNNNNN` artifacts) rather
  than clean `__thiscall` pseudocode, unlike every other function in this note — a genuine (if minor)
  Ghidra decompiler limitation on these two multiple-inheritance vtable slots, not a stripped/inlined
  function; the bytes and call targets are still present. Byte-level signal was still extractable:
  `VisitPosState` reads **2× u16 via `+0x10`** (X, Y — matches "position" exactly), `VisitRectState`
  reads **4× u16 via `+0x10`** (matches "rect" — 4 components, likely left/top/right/bottom or
  x/y/w/h). Both call *some* base visitor first; the offset Ghidra reported (`+0x7c` for both) is
  suspect given the corruption and shouldn't be trusted numerically — `ImageState`/`RectShapeState`
  cleanly call `VisitRectState` at `+0x88`, so `RectState`'s own base call is probably `VisitState`
  (`+0x78`) with a register-misattribution shifting the apparent offset by one slot.
- **`VisitTextBaseState`** @ `0xa05dd20`: base `+0x88` (`VisitRectState`) → `1×+0x20` (float →
  `param+0x30`) → `1×+0x10` (u16 → `param+0x34`). Extends rect (position/size) with one extra
  float+u16 — plausibly a text-scale or line-height field.
- **`VisitTextState`** @ `0xa05fb70`: base `+0x8c` (`VisitTextBaseState`) → `1×+0xc` (u32 →
  `param+0x44`) → `1×+0x14` (u16) + `2×+0x18` (byte, byte) packed into one 4-byte temp →
  `(float)(u16 part)` → `param+0x40`, byte 3 → `param+0x48`, byte 2 → `param+0x49` (looks like an
  alpha/scale-as-float plus 2 raw color-channel bytes) → `2×+0x10` (u16, s16 → `param+0x3c`,
  `param+0x3e`, likely a shadow/outline offset pair).
- **`VisitImageState`** @ `0xa05fa20`: base `+0x88` (`VisitRectState`) → `1×+0xc` (u32 → `param+0x54`)
  + `2×+0x18` (byte, byte → `param[0x58]`, `param[0x59]`) → **4× chained `+0x20`** (float×4 →
  `param+0x38, +0x3c, +0x30, +0x34` — non-sequential storage order, a Rect/UV-style struct with a
  different member order than read order) → `3×+0x24` (bool×3, packed flags in `param[0x40]`) → **4×
  `+0xc`** in a loop (u32×4 → `param+0x44..+0x50`, most likely an **RGBA color**).
- **`VisitRectShapeState`** @ `0xa05f950`: base `+0x88` (`VisitRectState`) → `1×+0x1c` (byte →
  `param[0x30]`) + `1×+0xc` (u32 → `param+0x34`) → **4× `+0xc`** in a loop (u32×4 → `param+0x38..
  +0x44`, same RGBA-shaped block as `ImageState`) → `1×+0xc` (u32 → `param+0x48`) + `2×+0x18` (byte,
  byte → `param[0x4c]`, `param[0x4d]`). **Confirms the "matches the corresponding non-State visitor"
  pattern directly**: `RectShapeState`'s shape mirrors `VisitRectShape`'s own 2-flags-plus-scalar
  pattern, just with an added 4-float/int color block layered on top.

## Confirmed class hierarchy (`FarCry2_server`, `magma::` namespace)

```
CResource → CResourceContainer → CMagmaResourceContainer → { CMagmaUIResource, CMagmaConfigUIResource }
```

Built at runtime via **hand-rolled RTTI** (`ClassHierarchyInfo` / `CStringID::SetContent`), not
compiler RTTI — visible directly inside `CMagmaConfigUIResource::LoadResourceInMagma`. Related classes
present in the same binary: `CMagmaElementFactory`, `CMagmaActionDispatcher`, `CMagmaFacade` (has an
`EMagmaIcons` enum), `CMagmaInputListener`, `CMagmaBinkHandler` (Magma can host Bink video playback —
see `research/file_manifest.md` §8), `IMagmaDebugTextService`, and widget-level classes:
`magma::Element`, `Widget`, `Page`, `Focusable`, `AreaInstance`, `Image`, `Font`, `CheckBox`,
`RadioButtonInstance`, `StringTable`, `ListBox`.

## Load flow, decompiled

- **`CMagmaUIResource::LoadPackageInMagma(char const*)`** @ `0x0961ee70` (`__thiscall`) — the `.mgb`
  loader entry point. Early-returns if already cached (checks `this+0x44`). Locks the
  `magma::CEngineNomad` singleton, builds a `magma::CFileNameNomad` identifier from the path, calls a
  virtual at vtable+0x14 (`LoadPackage`) that returns a `magma::Package*`, then notifies
  `CMagmaBinkHandler::OnLoadPackage`.
- **`CMagmaConfigUIResource::LoadResourceInMagma()`** @ `0x096077a0` — the `.desc` loader. Walks a
  `std::vector` at `this+0x28/+0x2c` — this is the parsed `<dependencies>` children list from the XML
  above. Recurses only into child entries that are themselves `CMagmaConfigUIResource` (i.e. nested
  `.desc` deps, like `fonts.mgb.desc`/`common.mgb.desc` in the sample), and as the **final step**
  after the dependency walk, calls `CMagmaUIResource::LoadPackageInMagma` on `this+0x4c` with a
  literal `"UI\"` prefix. **This confirms the `.mgb` binary is always the last thing loaded for a
  given `.desc`, after all of its declared `.desc`/`.mgb.desc` dependencies are satisfied first**, and
  that resource `ID` paths in the XML are relative to a `UI\` root the loader prepends.
- **Two parallel "visitor" implementations** exist for reading package data, selected by
  `magma::CFactoryNomad::MakeBinaryLoadVisitor` vs. `MakeLoadVisitor` vs.
  `MakeTextCompatibilityVisitor`:
  - `BinaryLoadVisitor` (ctor @ `0xa05edf0`) — for `.mgb`. Constructor allocates a per-instance
    scratch buffer and `memset`s it to `0xFF` (this becomes the 255-byte type-remap table filled by
    `ReadHeader`'s type-table walk, see below). Its `Open`/`ReadHeader`/`VisitPackage` chain is now
    resolved — see "Header — confirmed byte-for-byte" below for the full byte-level breakdown.
  - `LoadVisitor` (ctor @ `0xa064860`) — for XML, built on `CMarkupSTL`.
- `magma::LoadVisitor::ReadPackage` @ `0xa0688e0` is a large (~1,300-line decompiled) `CMarkupSTL`-
  driven XML parser, **but its keys (`PAGESIZE`, `DISPLAYOFFSET`, `MATERIALS`, `FONTSUBST`, `FONTS`,
  `REPLACES`, ...) don't match the `<dependencies>`/`crc_ID` schema seen in the sample `.desc`** — this
  is a sibling/different config schema (font substitution, material remap), not the exact parser for
  the dependency-tree style `.desc` shown above. The actual `<dependencies>`/`crc_ID` XML parser was
  not separately located.

## The nav-bar layer sits *on top of* Magma, not inside it

`CNavBarModule` / `CNavBarLayout` / `CNavBarButton` / `CNavBarPageHandler` / `CNavBarStack` are a
**separate, non-`magma`-namespaced hierarchy** that bridges into Magma rather than being part of it:
`CNavBarModule::OnActionSignal(CStringID const&, magma::Action*)` and
`CNavBarButton::SetIcon(CMagmaFacade::EMagmaIcons)` both cross from nav-bar code into Magma's own
action/icon systems. Notably, `CNavBarLayout::SetupNavBarButton(XmlNodeRef, magma::Page*)` parses
**yet another, third XML representation** (`XmlNodeRef`, distinct from both `CMarkupSTL` above and
whatever parses `.desc`'s `<dependencies>` tree). The literal strings `"b_prompt1"`–`"b_prompt4"` and
`"p_prompts_navbar"` exist in the binary as element/`CStringID` names, matching the `.desc` sample's
`<b_prompt1 show="1" text="Generic;ACCEPT" />` elements exactly — real confirmation that the `.desc`
XML we read by hand is genuinely consumed by this code path, even though the exact parsing function
wasn't pinned down.

## Not yet traced

Everything below is deliberately **not** a Ghidra-coverage gap in `FarCry2_server` — the widget/keyframe
format itself is now specified end-to-end (header → package → area/page → element → widget-type fields
→ keyframe → state). What's left is verification and precision work:

- **No byte-level cross-check of the body** (file offset `0x2A7` onward) against `options.mgb`'s real
  bytes has been done — unlike the header, every field sequence in "Widget/record body" is
  decompiled-logic-only, verified against the code but not yet hand-simulated against the hex dump the
  way the header table was. Doing that for at least one full `Page`/`Widget` would be the strongest
  remaining confidence check, and the natural prerequisite before writing an actual parser.
- **`VisitPosState`/`VisitRectState`'s exact base-vtable-slot offset** — both decompiled as
  this-adjustor thunks with corrupted-looking pseudocode (see "Keyframe / animation-state records"),
  so the `+0x7c` base-call offset Ghidra reported for both is not trusted; their field reads
  themselves (2×u16 and 4×u16 respectively) were still extractable and are trusted.
- **`+0x14`/`+0x18` are still only guessed to be template-overload duplicates of `+0x10`/`+0x1c`**
  (byte-stream-identical at every call site seen so far) — plausible but not proven.
- **Byte 13's flag purpose** (read via `ReadBool`, value `0x00` in the sample) and **bytes 5-7**
  (`CD 00 00`, consumed by the offset-8 sentinel read but never separately examined) — both present in
  every `.mgb` but their semantics weren't identified.
- **Several helper functions named but not opened**: `Window::ReadStretchableWindowSection`/
  `ReadWindowSection` (the 9-patch section reader), `LoadMaterial`, `LoadFontFamily` — their call
  sites and purpose are confirmed, their own internals are not.
- **The Action-dispatch/reflection subsystem** (`VisitActionExecuter` and its per-subtype overrides,
  `VisitUserDataItem`, `VisitGenericObjectTable`/`VisitGenericObject`, `VisitTickTimingStrategy`, all
  at vtable `+0xf0`+) is fully resolved to real functions but was deliberately not decompiled — it's a
  different format (script/action bindings) layered on top of `.mgb`'s widget geometry, not part of it.
- **`Dunia.dll` (PC, `0x10xxxxxx`) was never checked** — every finding above comes from
  `FarCry2_server`'s portable/shared code, which should carry over unchanged, but this hasn't been
  verified directly. No tool was available this session to switch Ghidra's active program tab.
- **`.desc`'s `crc_ID` attribute is still unexplained.** Confirmed *not* a CRC32 checked anywhere in
  the `.mgb` binary load path (see above) — but that only rules out one hypothesis. Could be a
  build-time-only cache key (Magma's own asset pipeline, never re-verified at runtime) rather than
  anything the game itself computes; worth checking the (unexplored) XML-side `.desc` parser instead of
  the binary side if this is revisited.

## Reproducing this

```
tools/DuniaTools/bin/fc2_dunia/Gibbed.Dunia.Unpack.exe -v \
  "<Steam install>/Data_Win32/patch.fat" <output_dir>
```

Uses the `FCCU_FC2` project filelist (`tools/DuniaTools/bin/fc2_dunia/projects/FCCU_FC2/files/FCCU_FC2.filelist`)
to resolve real paths automatically — no manual hash lookup needed. `patch.fat` is small (9.8 MB, 218
files) and contains one full localized set of `ui\localized\{pc,pcwidescreen}\{eng,fre,ger,ita,spa,cze}\ui\*.mgb[.desc]`,
making it the fastest archive to pull `.mgb`/`.desc` samples from (`worlds.fat`/`common.fat` also
contain UI resources but are far larger).
