---
title: Higher-resolution menu and HUD textures
kind: component
bundle: ui
status: located
systems: [ui]
match:
  - "ui/fonts/**"
  - "ui/textures/**"
  - "ui/video/**"
exclude:
  - "ui/textures/hud/icons_weapons/**"
  - "ui/textures/hud/icons_objectives/**"
  - "ui/textures/avatars/avatar_bai.xbt"
requires: []
verified: diff
---

# Higher-resolution menu and HUD textures

Nearly every menu and HUD texture is replaced by a two- to four-times larger version of the same
image: the notebook and paper backdrops, logos, cursor, prompts, network and battery icons, the
character photos of the start screen, the shop's weapon pictures, the health bar and syringe, the
inventory icons, the fonts and the menu's mosquito video.

## How

150 whole files, each replacing the base file of the same path:

- `ui/textures/common/*` (67) and `ui/textures/cursor.xbt`, `ui/textures/loading/loading_logo3.xbt`,
  `ui/textures/ui_icon_{ammo,manual,repair}upgrade.xbt`, `ui/textures/ui_tape_icon.xbt`: mostly
  2x per side (for example `map.xbt` 256x512 -> 512x1024, `brightness_screen.xbt` 4x).
- `ui/textures/avatars/avatar_*.xbt` (8 of 9): 512x512 -> 1024x1024 portraits of the original
  characters. The ninth, `avatar_bai.xbt`, is `ui-avatar-selection`.
- `ui/textures/guns/gun_icon_*.xbt` (30): the shop's weapon pictures, 256x64 -> 512x128.
- `ui/textures/hud/*.xbt` (16): crosshairs, hit indicators, life bar, syringe, stains, text box,
  weapon-swap arrow, lock, check mark, notebook (`notebook.xbt` 1 MB -> 4 MB) and diamond.
- `ui/textures/hud/icons_inventory/*` (6): 2x.
- `ui/textures/hud/icons_buddies/*` (12): same size (128x128), re-graded slightly darker.
- `ui/fonts/farcry2_25_0.xbt` and `arial_cyrillic_25_{0,1,2}.xbt`: the glyph atlases at 2x
  (`farcry2_25_0` 128x1024 -> 256x2048).
- `ui/video/mosquito.bik`: the same 10.6 s Bink clip at 1200x1200 instead of 600x600.

Scaled back to the base size, most match the base image closely (structural similarity 0.94-1.0),
so they are upscales of the same art. A few are redrawn:

- `hud/hud_icon_seringe.xbt` (64x64 -> 256x256): the healing syringe becomes an auto-injector tube.
- `loading/loading_logo3.xbt` (same 64x64): the smooth ring becomes a brush-stroke ring.
- `common/loading_symbol.xbt`, `common/circle_glow.xbt` and some halftone and arrow textures differ
  more than an upscale would (similarity 0.71-0.90) but show the same motif.

## Uncertain

- The fonts' glyph positions live in the font packages, which the mod does not change; that a 2x
  atlas still maps its glyphs correctly assumes the engine addresses glyphs in normalised
  coordinates. Not checked in game.
- The source of the upscales (a separate HD UI pack, or the author's own) is not stated.
