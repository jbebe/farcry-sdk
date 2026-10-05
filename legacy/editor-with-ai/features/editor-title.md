---
title: Editor title credits Janne252
kind: component
bundle: editor-content
status: located
systems: [ui]
match:
  - "languages/english/oasisstrings.fragment.xml#InGameEditor_PC/EDITOR_NAME"
  - "languages/english/oasisstrings.xml#section[InGameEditor_PC]/string[EDITOR_NAME]{,@value}"
exclude: []
requires: []
verified: diff
---

# Editor title credits Janne252

The map editor's name, shown in its title, reads "Far Cry® 2 Map Editor - Running with Janne252's
Mod. Xfire contact: Janne252" instead of "Far Cry® 2 Map Editor".

## How

The `InGameEditor_PC` string `EDITOR_NAME` is changed in English, both in
`languages/english/oasisstrings.xml` and in `oasisstrings.fragment.xml` (two changes). The other
languages' copies are not on this page.

Together with the `Janne252_*` palette strings ([`noise-editor-strings`](noise-editor-strings.md))
and the "Far Cry 2 - Multi... Editor v1.3.2.2" comments in the palette files, it shows which editor
mods this one is built on. See [`editor-razor-library`](editor-razor-library.md) for Janne252's
content.
