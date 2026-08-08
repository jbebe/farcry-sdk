---
sidebar_position: 1
---

# Authoring Magma UI

:::info[Derived from shipped data and reverse engineering]
Everything here is either transcribed from the RE'd wire format (see
[`.mgb`](../file-formats/mgb.md) and [the field-name join](../file-formats/mgb-field-names.md)) or
measured across the **50-package vanilla corpus** — 773 areas, 5,422 elements, 10,949 keyframes — by
decoding every `ui\localized\pcwidescreen\eng\ui\*.mgb` to XML and counting. Claims that are
inferred rather than confirmed are marked **(inferred)** at the point they are made.

The one working example on this page — [`hello.mgb.xml`](pathname:///starters/hello.mgb.xml) —
encodes, re-parses, and round-trips byte-identically through `jackall mgb encode` / `decode`. It has
not been shown on screen in-game.
:::

Magma is the in-house UI engine behind every Far Cry 2 screen: main menu, HUD, pause menus, the
in-game map editor. A screen is a **package** — one `.mgb` binary, optionally paired with a
`.mgb.desc` sidecar. This section is the authoring counterpart to the format pages: those describe
the bytes, these describe how to make something the game will actually draw.

| Page | What it answers |
|---|---|
| This page | What the model is, how to build and verify a package, the rules that keep it loadable |
| [Reference](./reference.md) | Every element and attribute you can write in the `.mgb` XML, and what values are legal |
| [The `.mgb.desc` sidecar](./desc-sidecar.md) | The plain-XML companion file: resource manifest, load order, nav-bar prompts |
| [Patterns](./patterns.md) | How shipped screens do buttons, lists, fades, focus, masks — with the reusable `common.mgb` catalogue |
| [Interop with the Dunia engine](./engine-interop.md) | Where behaviour actually lives: the action registry, dispatch, and how runtime data reaches a widget |
| [Limits](./limits.md) | The hard edges of the format, and the failures that are silent |

## The model in six facts

1. **A package is a flat list of areas.** An `Area` is a self-contained mini-scene with its own
   coordinate space, its own frame timeline, and its own element list. There is no area nesting on
   the wire — a package is `Package → Area* → Element*`, exactly two levels deep.
2. **Nesting is done by instancing.** An element whose widget is an `AreaInstance` /
   `PageInstance` / `ButtonInstance` embeds *another area* — from this package or another one — at
   a position. That is the only composition mechanism, and it is what makes the tree arbitrarily
   deep in practice. 1,280 instances across the corpus; of the 1,273 area links they and the
   list/slider widgets carry, 624 point into `common.mgb`.
3. **Every element is a widget plus a timeline.** `Element` carries the shared header (visibility,
   mask mode, `UserData`), then a list of `Keyframe`s, then the widget's own body. Geometry and
   colour live in the *keyframes*, never in the widget — a static element is one with a single
   keyframe at `IDX="0"`. 63% of elements are exactly that.
4. **Everything is animated by frame index.** The area declares `FRAMERATE`; each keyframe declares
   `IDX`, an integer frame number; the engine interpolates between them. Playback is controlled by
   `Action`s attached to keyframes — 1,523 of the corpus's 1,853 action executers hang off a
   keyframe, and `Stop`/`GotoFrameIndex`/`Continue` account for almost everything they fire.
5. **Names are CRC32 hashes, one-way.** Areas, elements, keyframes, property keys and cross-package
   references are all stored as `CRC32(name)`. You author a string, the file keeps 4 bytes. Two
   objects with the same name in the same scope are indistinguishable; a name you never wrote down
   is unrecoverable.
6. **The `GenericObjectTable` is the package's export surface.** It maps a name to a path into the
   tree, and `magma::Engine::LoadPackage` registers every loaded package's table globally. It is how
   native C++ code finds a page by name, and the only part of your package the outside world can
   address.

## The authoring loop

`.mgb` is binary, has no lengths and no sentinels, and a single wrong field width silently corrupts
everything after it. You do not hand-write it — you write the **XML interchange format**
(JackAll's, not Magma's own `.mgm`) and build it:

```
# start from something real
jackall mgb decode "<extracted>/ui/localized/pcwidescreen/eng/ui/options.mgb" -o options.xml

# edit options.xml, then
jackall mgb encode options.xml -o options.mgb
```

`mgb encode` reads the result straight back before writing it, so a package that survives the
command is at least structurally loadable. The XML reader is deliberately strict: a misspelled
attribute or an element in the wrong place is an error naming the offender, rather than the silent
degradation Magma's own XML loader does.

Getting the `.mgb` into the game is a separate problem with two answers:

- **Replace a shipped package** — rebuild the archive it lives in (`patch.fat`), keeping the same
  path. The engine loads it through `CMagmaConfigUIResource` → `CMagmaUIResource::LoadPackageInMagma`.
- **Add a new package** — hook the file reader and serve your own bytes under a `UI\…` path, then
  bind a page to it by name. This is what FCSE does; see
  [the menu system page](../engine-internals/magma-menu-system.md) and `tools/FCSE/assets/README.md`.

## The minimal package

[`hello.mgb.xml`](pathname:///starters/hello.mgb.xml) is a complete, verified starting point:
1,540 bytes compiled, one `Page` with a fade-in panel, a text label, and the standard back-prompt
strip instanced out of `common.mgb`. Its shape, with the 166-entry `<TYPES>` block elided:

```xml
<MagmaPackage sentinel="CD0000AB" version="2010000" flag="false"
              POOLCOUNTS="0 0 0 …65 zeros…"
              PAGESIZE.w="1280" PAGESIZE.h="800"
              DISPLAYOFFSET.x="160" DISPLAYOFFSET.y="40" DEFAULTMATERIAL="">
  <TYPES>…166 <TYPE id="…"/> entries, copied verbatim from any decoded package…</TYPES>
  <USERDATA name="hello"><PROPERTIES /></USERDATA>
  <MATERIALS materialExtra="0" />
  <FONTSUBSTS /><FONTS /><FONTFAMILIES />
  <CHILDREN>
    <Area slot="101" type="Page" FRAMERATE="30" CURRENTFRAME="0"
          STATICBOX="0 1280 0 800" SINGLE_GLOBAL_SELECTION="true">
      <USERDATA name="HELLO_PAGE">
        <PROPERTIES><PROPERTY key="LAYER" type="2" value="10" /></PROPERTIES>
      </USERDATA>
      <CHILDREN>
        <!-- element list: Placeholder, RectShape, Text, PageInstance -->
      </CHILDREN>
      <DEFAULT_ELEMENTS>
        <DEFAULT_ELEMENT CONTROLLER="255" ID="p_prompts_navbar" />
      </DEFAULT_ELEMENTS>
    </Area>
  </CHILDREN>
  <GENERICOBJECTTABLE name="hello">
    <GENERICOBJECTS>
      <GENERICOBJECT name="HELLO_PAGE">
        <LINK slot="101" LASTOBJECTTYPE="Page" IDS="hello HELLO_PAGE" />
      </GENERICOBJECT>
    </GENERICOBJECTS>
  </GENERICOBJECTTABLE>
</MagmaPackage>
```

Three parts of that are not optional and not obvious:

- **`<TYPES>` is a fixed 166-entry block.** It is byte-identical in all 50 shipped packages
  (verified: one distinct block across the corpus), so the `slot="…"` numbers are constants for this
  build — `101` is always `Page`, `74` always `Image`. Copy the block verbatim and use
  [the slot table](./reference.md#type-slots). Do not renumber it.
- **`POOLCOUNTS` is 65 memory-pool hints.** All-zero works; they pre-reserve allocation chunks and
  affect no offset.
- **The `GENERICOBJECTTABLE` entry is what makes the page reachable.** `CUIPageBase::Init` hashes a
  page-name string and looks it up through `GenericObjectServer::FindGenericObject`. No entry, no
  binding — the package loads and draws nothing.

## Ten rules that keep a package loadable

1. **Copy the `<TYPES>` block; never invent slot numbers.** A slot that resolves to a class outside
   the five `MakeArea` / fourteen `MakeElement` / nine `MakeActionExecuter` sets makes the engine
   dereference a null pointer with no guard.
2. **Geometry is `u16`, and negatives wrap.** `x = -181` is written `65355`. Every coordinate,
   including `LEFT`/`RIGHT`/`TOP`/`BOTTOM` and `POSITION`, is unsigned 16-bit. 11% of the corpus's
   `ImageState` coordinates are wrapped negatives.
3. **`RectState` order is LEFT, RIGHT, TOP, BOTTOM** — not left/top/right/bottom. Getting this wrong
   produces a plausible-looking rectangle in the wrong place.
4. **Colours are ARGB** (`0xAARRGGBB`), authored as an 8-hex-digit attribute. `00FFFFFF` is
   transparent white — the start of a fade, not cyan.
5. **The keyframe state class is decided by the widget class, not by you.** An `Image` element's
   keyframes are `ImageState`, a `Text`'s are `TextState`, a `PageInstance`'s are `ScaleState`. See
   [the state table](./reference.md#keyframes-and-states).
6. **Every element needs at least one keyframe.** Zero keyframes means no geometry and nothing
   drawn. There are no zero-keyframe elements in the corpus.
7. **A cross-package `AREA` reference names a package by *hash of its bare name*
   (`PACKAGE="common"`), a `MATERIALLINK` names it by *path string* (`PACKAGE="\common.mgb"`).**
   Two different conventions in the same file; mixing them silently fails to resolve.
8. **A material's texture path resolves against the package's own name**, so a package the engine
   knows by a wrong path renders untextured white quads rather than erroring.
9. **`FullLink` `IDS` is a path, not a name.** `IDS="fcse FCSE_PAGE p_menu_nav #36150990 l_menu_nav_list"`
   walks package → page → element → the area that element instances → element inside it. Every step
   must exist.
10. **Actions come from a closed registry.** You can only fire action names the game already
    registers with `ActionServer` — 6 magma built-ins plus 81 hardcoded game actions, with no data
    path into the table. [The registry and how it dispatches](./engine-interop.md);
    [the names shipped packages use](./reference.md#the-action-catalogue).

## Where the rest of the knowledge lives

- [`.mgb` / `.mgb.desc` format](../file-formats/mgb.md) — the wire layout, the type table, the
  validation record. Read this if you are writing a *tool*, not a screen.
- [`.mgb` field names](../file-formats/mgb-field-names.md) — the per-offset `LoadVisitor` join and
  the full `Util::GetType` tag table these pages quote enum names from.
- [The menu system](../engine-internals/magma-menu-system.md) — how a native C++ page class binds
  to a Magma layout, what `CUIPageBase::FetchMagmaElements` requires by name, and how FCSE reaches
  a page of its own.
