---
sidebar_position: 25
---

# `.mft` — Magma fonts

:::info[Verified via reverse engineering]
Layout traced in `Dunia.dll` (`magma::PixmapFont::LoadFontFile` @ `0x10ac6b40` and the character map
reader @ `0x10ac7460`, Steam) via GhidraMCP. JackAll reads and writes both shipped fonts byte for byte,
and a font it drew from a TrueType file draws in game **(seen in game, 2026-10-28)**. See
[intro](../intro.md) for how RE-verified and community-reported claims are distinguished on this site.
:::

The game's text is drawn from pixmap fonts: an `.mft` of glyph metrics in `ui\fonts\`, beside pages
of glyphs already drawn, as `.xbt` textures. Two ship, `farcry2_25.mft` (Latin, one page) and
`arial_cyrillic_25.mft` (Latin, Greek and Cyrillic, three pages); every text in the game uses one of
them through a font family in `fonts.mgb`.

All values are little-endian.

## Layout

| Field | Size | Value |
| --- | --- | --- |
| Magic | 11 | `"Magma Font\0"` |
| Version | u16 | `0xA0` in both; the reader branches on it from `0x6F` up |
| Marker | u16 | `0xABCD` |
| Name | u16 length + bytes | `Farcry2_25` |
| Source | u32 length + bytes | the TrueType file it was drawn from, `R:\main\data\UI\fonts\Farcry2.ttf` |
| Generator settings | 5 × u32 | `64 64 1024 1024 1`, or `… 10` for the Cyrillic font; read and dropped |
| Flag | u8 | `0` in both |
| Size | u8 | the pixel size the glyphs were drawn at, `25` |
| Style | 4 × u8 | `0` in both |
| Type | u8 length + bytes | `PixmapFont` |
| Glyph count | u16 | |
| Glyphs | 30 each | see below |
| Character map | 256 blocks | see below |
| Trailer | 2 × f32 | kept by the font, of unknown use: `-1.171875 0.78125` and `-3.390625 3.359375` |
| Page count | u8 | |
| Pages | u32 length + bytes each | `\Farcry2_25_0.png`; the game reads `ui\fonts\farcry2_25_0.xbt` |
| Texture type | 10 | `"PNG"` padded with zeros |
| Texture settings | u32 length + bytes | `<settings database="TextureProfiles.xml" profile="Grayscale AlphaBlend No MipMap" />` |

### Glyphs

| Field | Type | |
| --- | --- | --- |
| Code | u16 | the character, UTF-16 |
| Width, Height | u16, u16 | the glyph's pixels |
| Left | i16 | from the pen to the glyph's left edge |
| Top | i16 | from the baseline up to the glyph's top edge |
| Advance | u16 | how far the pen moves on |
| U0, V0, U1, V1 | 4 × f32 | the glyph's cell on its page |
| Page | u16 | which page |

Glyph 0 is `0x7F`, empty: what a character the font lacks draws as. The others need not be in code
order; the Cyrillic font's aren't.

The UVs run from half a pixel inside the cell's top-left corner to one and a half pixels past the
glyph's bottom-right: `U0 = (x + 0.5) / pageWidth`, `U1 = (x + width + 1.5) / pageWidth`, the same for
V. Each cell is therefore a pixel wider and taller than its glyph.

### Character map

256 blocks, one per 256 characters: a u8 flag, and when it is set, 256 u16 glyph indices, 0 for a
character the font lacks. In both shipped fonts a block is present exactly when one of its
characters has a glyph, and every entry is that glyph's index, so the map follows from the glyph
list.

## Pages

A page is single-level DXT5, white, with the glyphs in alpha. `farcry2_25_0.xbt` is 128 × 1024; the
Cyrillic font's are 256 × 256. A page's `.xbt` header is the default one any texture can have.

A page can be any size under the same `.mft`, since its UVs are fractions: a font's page enlarged 4×
draws the same glyphs in the same places **(seen in game, 2026-11-01)**.

## Adding a font

A font is used through a font family. Both are declared in `fonts.mgb`, one per language and screen
shape:

```xml
<FONT slot="106" type="PixmapFont" name="Caveat_40" file="\Fonts\caveat_40.mft" />
<FONTFAMILY name="Caveat_40" font="Caveat_40" PACKAGE="" />
```

A `Text` names the family by its `FONTFAMILY` id. The font's `.mft` and pages must also be listed
under `fonts.mgb.desc` in the UI dependency list, `ui\localized\<aspect>\<lang>\ui\_depload.xml`:
without that, the text draws nothing. See
[UI dependency lists](./depload.md#ui-dependency-lists).

## Tools

JackAll reads and writes the format, and draws a new font from any TrueType or OpenType file with
FreeType: `jackall-cli mft decode`, `encode`, `faces` and `build`, and **Make a font** on the Mods
tab; see [Add a font](/jackall/fonts).
