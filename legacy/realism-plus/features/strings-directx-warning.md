---
title: DirectX option recommends DirectX 9
kind: component
bundle: ui
status: located
systems: [ui, graphics]
match:
  - "languages/*/oasisstrings.fragment.xml#MainMenu/OPTIONS_DIRECTX_RESTART"
exclude: []
requires: []
verified: diff
---

# DirectX option recommends DirectX 9

The message shown when the DirectX version is changed in the options now opens with advice to stay
on DirectX 9.

## How

`MainMenu/OPTIONS_DIRECTX_RESTART`, all ten languages: "DirectX changes will only take effect once
you restart the game." -> "You should use DirectX 9. DirectX 10 is broken and causes a lot of bugs.
DirectX changes will only take effect once you restart the game." (translated in each language).

## Uncertain

- Which DirectX 10 problems the author means is not stated; `graphics-dx10-medium-levels` is the
  only DirectX 10 edit in the render settings.
