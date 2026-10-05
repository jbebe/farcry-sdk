---
title: Retail English common menu package in four widescreen languages
kind: noise
status: located
systems: [ui]
match:
  - "ui/localized/pcwidescreen/{cze,hun,pol,rus}/ui/common.mgb"
exclude: []
requires: []
verified: diff
---

# Retail English common menu package in four widescreen languages

The mod ships `common.mgb` in the widescreen Czech, Hungarian, Polish and Russian folders. It is not
an edit for the mod: all four are byte for byte the retail (GOG) patch's English widescreen copy
(111,069 bytes).

## Why it is noise

Against the Steam baseline each shows as a whole-file change (114,329 -> 111,069 bytes). Decoded,
the Steam copy has the two extra areas and longer navbar that `noise-ui-retail-common-mgb` in
`legacy/realism-plus` describes (Steam's online-profile and exclusive-content additions); the
retail English copy lacks them and, unlike Steam's Czech copy, carries the English placeholder
texts (`WWWWWWWWWWWWWWW`, `TEXTTEXT...`) in four text boxes that the game fills at run time. The
package's `.desc` and every other language's copy are Steam's own.

Nothing in the files was authored for the mod. What they do in game is undo Steam's version of the
package for widescreen players of those four languages.

## Uncertain

- Why these four languages get the file is not known. The mod's `sp_avatar.mgb.desc` points every
  language at the English widescreen packages (`ui-avatar-selection`), so this may be a side effect
  of how the author built the language folders.
- Whether a single-player screen on Steam needs the two missing areas is not checked.
