---
title: Seven languages replaced by English
kind: component
bundle: strings
status: located
systems: [ui]
match:
  - "languages/{czech,french,german,hungarian,italian,polish,spanish}/oasisstrings.fragment.xml#**"
exclude:
  - "languages/*/oasisstrings.fragment.xml#InGameEditor*/**"
requires: []
verified: diff
---

# Seven languages replaced by English

A Czech, French, German, Hungarian, Italian, Polish or Spanish game shows the whole game in English:
menus, subtitles, objectives, the HUD, the shop, tutorials and multiplayer. This is a side effect of
how the editor's labels were shipped, not something anyone would pick. It is a component rather
than noise because it does change the game, for every non-English player.

## How

The mod ships a whole `languages/<language>/oasisstrings.rml` for each of the seven, and the seven
files are byte-identical. Each is an English table (its root says `language="english"`): outside the
`InGameEditor*` sections it is the 1.00 English text described in `strings-english-release-text`,
string for string, including the Fortunes Pack placeholder and the tab in `Subtitles/4911264`.
There is no line of any other language in it.

Each language's changes are every string whose translation differs from that English: outside the
editor sections from 9,098 (German) to 9,194 (Spanish), 64,098 in all. Around 500 strings per
language are the
same in English and the translation (names, numbers, untranslated lines) and so are not changes.
`Subtitles/5153939` ("Neither side has won.") appears twice in the mod's table, so it is listed
twice.

Russian and Chinese ship the base game's own tables unchanged, and Japanese is not shipped, so
those three keep their language.

The same files also carry English editor labels; those changes are
`strings-editor-labels-other-languages`. Building the files whole from one English table is why
both happen together: the editor needed its new labels in every language, and the simplest way was
the English table everywhere.

The 25 strings of patch 1.03 that the English table lacks (`strings-english-release-text`) are
missing from these seven too.

## Uncertain

- That the copies come from Janne252's editor mod, which this mod is built on, is an inference:
  their editor sections are an older, shorter version of the mod's English ones (see
  `strings-editor-labels-other-languages`).
