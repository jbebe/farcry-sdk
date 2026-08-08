---
sidebar_position: 6
---

# Limits and silent failures

:::info[What this page is for]
The format is fully decoded, so "can I write it" is answered by
[the reference](./reference.md). This page answers the other two questions: **what can Magma not
express at all**, and **what will it accept and then quietly not do**. Numbers come from the wire
format; frequencies from the 50-package vanilla corpus.
:::

## Hard limits of the format

| Limit | Value | Consequence |
|---|---|---|
| Type-table entries | **254** (a single count byte); shipped files use 167 | Not reachable in practice — every constructible class is already declared |
| Geometry | **`u16`** everywhere: `LEFT`/`RIGHT`/`TOP`/`BOTTOM`, `POSITION`, `ORIGIN`, `STATICBOX`, `HOTSPOT` | Coordinates are −32,768…32,767 in whole pixels. No sub-pixel positioning; a negative is written `65536 + v` |
| Keyframe `IDX` | read as `u32`, **stored truncated to `u16`** | Frame numbers above 65,535 silently alias |
| `MASKMODE` | u32 on the wire, **low 3 bits kept** | Only 4 legal values |
| `BLENDINGMODE` | u32 on the wire, **low byte kept** | 27 defined modes |
| `EditBox` `maxLength` | u32 on the wire, **low 16 bits kept** | |
| Colour | 32-bit ARGB, 8 bits per channel | No HDR, no per-channel blend factors beyond the blend mode |
| `Slider` range | `RANGEMIN`/`RANGEMAX` u32; 24 of 33 use 0–10 | Integer only |

## Closed sets you cannot extend

Each of these is an ancestor walk against a hardcoded list in the engine. A type outside the set
produces a null object the caller dereferences **without a null check**, so it is a crash, not a
degradation — and the JackAll encoder rejects it before you get there.

- **5 area types**: `Area`, `Page`, `Button`, `Cursor`, `CheckBox`.
- **14 widget types**. There is no way to define a fifteenth. Every custom-looking control in the
  game — the value spinner, the tab strip, the weapon-shop grid — is a composition of these.
- **9 action-executer types**, and the event array each one carries is fixed-length.
- **The action registry.** `ACTIONNAME` is `CRC32(name)` resolved by `ActionServer::MakeAction`
  against names the game binary registers at startup — 6 from
  `magma::ActionServer::RegisterStandardActions` and 81 from
  `CMagmaActionDispatcher::RegisterCustomActions`, all hardcoded C++ string literals. **You cannot
  define a new action from data**, and an unregistered name gives you nothing to hang behaviour on.
  The full registry is [on the interop page](./engine-interop.md#the-81-game-actions) and the 68
  names shipped packages actually use are
  [catalogued in the reference](./reference.md#the-action-catalogue); four of them (`Stop`,
  `Continue`, `GotoFrameIndex`, `GotoKeyframe`) are general-purpose timeline control, and everything
  else calls into compiled code for one specific screen.
- **The keyframe state class**, decided by the widget class alone. An `Image` cannot have a
  `ScaleState`; to scale a picture, instance an area that contains it.

## What Magma has no concept of

- **Layout.** There is no anchoring, no percentage sizing, no flow, no auto-layout of any kind.
  Every coordinate is absolute within its area. The only relative mechanism is that an instanced
  area's contents move with the instance's `POSITION`, and `ScaleState`'s `SCALEX`/`SCALEY`.
- **Resolution independence.** `PAGESIZE` is a fixed design canvas per aspect variant, and the two
  shipped variants have *different geometry*, not a scale factor. See
  [aspect variants](./patterns.md#aspect-variants).
- **Scripting or expressions.** No conditionals, no arithmetic, no data binding. A value that
  changes at runtime is written by native code into a named widget; the package's job is to provide
  the widget and the name.
- **Text measurement or rich text.** `WRAPPING`, `CLIPPING`, `ELLIPSIS` and `AUTOSIZED` are the
  whole layout vocabulary for a string. One font family and one `HEIGHT` per `Text` element — no
  runs, no inline colour, no markup. This is why templates carry `Wwwwwwwwwww` placeholders: sizing
  is done by eye at author time.
- **New fonts without a font package.** Font families resolve out of `\fonts.mgb`; the corpus uses
  exactly two. Embedding a font blob is possible (`<FONTSUBST>` carries one), but no shipped package
  does it, so that path is untested.
- **Vector drawing.** `RectShape` (optionally outlined, four corner colours) is the only untextured
  primitive. Everything else is a textured quad.

## Decoded but never exercised

These are writable and structurally understood, but **zero shipped packages use them** — treat them
as unexplored rather than supported:

| Feature | Corpus uses |
|---|---|
| `Window` (the 9-patch border widget) | 0 elements |
| `AutonomousAreaInstance` | 0 |
| `RadioButtonInstance` | 0 |
| `<STRINGTABLE>` / `useStringTable="true"` on `Text` | 0 packages |
| `MASKMODE="USEMASK_INVERTED"` | 0 |
| `ISDUPLICATABLE="false"` | 0 of 5,422 elements |
| `<DEFAULTFOCUSES>` entries on a `PageInstance` | 0 |
| `INTERPOLATION="CircleDecel"` | 0 keyframes |
| `INPUTFILTER` or `CONTROLLER` other than `255` | 0 |
| 21 of 27 blending modes | 0 |

## Failures that are silent

The engine's own XML loader treats every element as optional and degrades quietly. The binary
loader has no validation at all past the header. So most authoring mistakes produce *a screen that
draws the wrong thing*, not an error:

| Mistake | What happens |
|---|---|
| Page name not in the `GenericObjectTable` | `CUIPageBase::Init` finds nothing, binds nothing. Package loads; page is blank. An empty name short-circuits `Init` entirely — harmlessly |
| `p_menu_nav` / `l_menu_nav_list` / `a_title_bar` / `t_page_title` missing or misnamed | `FetchMagmaElements` misses, `AddButton` returns −1, rows silently never appear |
| A `SETTING_*` `UserData` key the native side asks for is absent | `GetUserDataElement` misses; that row has no value control |
| Material `PACKAGE` written as `common` instead of `\common.mgb` | Material does not resolve; the image draws as an **untextured white quad**. A full-screen one washes out the whole page |
| Package identified by a path other than the one its texture paths resolve against | Same white-quad failure, for every image at once — this is a real FCSE bug, fixed by naming the package `UI\fcse.mgb` |
| `AreaLink` `PACKAGE` written as a path instead of a bare-name hash | Instance resolves to nothing; the sub-tree is missing |
| `ISUSINGDUPLICATEDAREA="false"` on a list row template or a repeated button | All copies share one playhead and animate together |
| More list items than `BUTTONCOUNT` | Fine for the list (it scrolls), but absolutely positioned siblings do not scroll with it |
| Element with zero keyframes | No geometry; nothing drawn |
| Wrong `RectState` field order (l/t/r/b instead of **l/r/t/b**) | A plausible rectangle in the wrong place |
| Colour read as RGBA instead of ARGB | `00FFFFFF` looks like opaque cyan instead of transparent white |
| A name that collides under CRC32 within a scope | Undefined which object a reference finds |

## Failures that crash

Short list, and all of them are prevented by using the encoder rather than splicing bytes:

- A type slot resolving to a class outside `MakeArea` / `MakeElement` / `MakeState` /
  `MakeActionExecuter` — the result is dereferenced with no guard.
- A truncated or mis-sized record. The format has no lengths and no sentinels, so one wrong field
  width shifts every subsequent read; the file will not usually fail, it will read garbage as
  structure.
- Growing the type table by splicing (every body offset after it shifts). Reserialise the whole
  package instead — which is what `jackall mgb encode` does.

## The practical envelope

Within all of that, what you *can* build is wider than the vanilla UI suggests: arbitrary
composition depth through instancing, per-element keyframe animation with six easing curves,
gradient-tinted quads, stencil masking, 9-patch borders (untested), Bink video playback via the
`BINKFILENAME` area property, and full timeline state machines driven by `Stop`/`Continue`/
`GotoFrameIndex` with no native code at all.

What you cannot do is invent behaviour. Every *interaction* ultimately lands on an action name the
game already registers, or on a native page class that looks your widgets up by name. Design the
screen in Magma; put the logic in a plugin.
