---
title: Photographic moon and sharper sun flare
kind: component
bundle: graphics
status: located
systems: [graphics, environment]
match:
  - "graphics/sky/**"
exclude: []
requires: []
verified: diff
---

# Photographic moon and sharper sun flare

The cartoon-like bluish moon is replaced by a grey photograph of the full moon that fills more of
its sprite, so it also draws slightly larger. The sun flare and its time-of-day colour ramp are
redrawn at four to eight times the resolution with almost the same look.

## How

Three whole-file replacements in `graphics/sky/dome/`, the sprites every world's `<Sky>` names (see
`docs/docs/engine-internals/sky-and-clouds.md#the-authored-assets-nine-small-files-872-kb-total`):

| Texture | Base game | Mod |
|---|---|---|
| `moon.xbt` | 256² DXT5, 1 level, a bluish-white moon on about 80% of the square | 512² uncompressed A8R8G8B8, 1 level, a grey photographic moon on about 97% of it |
| `sun_flare.xbt` | 128² A8R8G8B8, 8 levels | 512² A8R8G8B8, 8 levels, the same white disc and ragged alpha edge |
| `sun_flare_tod_color.xbt` | 512×4 DXT5 ramp | 4096×32 A8R8G8B8, 13 levels; the same ramp, a slightly deeper orange at dawn and dusk and free of DXT banding |

The ramp is a lookup the sun flare reads by time of day, so its width is resolution along the day,
not detail anyone sees. 4096 is wider than any texture the base game ships (2048).

These files are shared by every world, so the change shows in the campaign, the multiplayer maps
and maps made with the editor alike. The sun-flare size and brightness the mod raises for editor
maps are `graphics-editor-map-sky`.

## Uncertain

- `moon.xbt` and `sun_flare.xbt` carry a borrowed header that names
  `graphics\terrain\_textures\desert\desert_sand_flat_n_mip0.xbt` as their companion, a 2048² DXT5
  sand normal map. The base game's sky sprites have no companion. If the streaming loader ever
  wants the top level, it would fetch a sand normal map in the wrong size and codec. A sprite this
  small on screen may never ask for it; not seen in game.
- The source of the moon photograph is not identified.
