---
title: String noise - the English table's unread XML source
kind: noise
systems: [ui]
match:
  - "languages/english/oasisstrings.xml#**"
exclude:
  - "languages/english/oasisstrings.xml#section[InGameEditor*]/**"
verified: diff
---

# String noise - the English table's unread XML source

The mod ships `languages/english/oasisstrings.xml` beside `oasisstrings.rml`. The engine only opens
the compiled `%s\%s\oasisstrings.rml`; nothing reads the `.xml` (see
`docs/docs/engine-internals/asset-reachability.md`), so every change in it does nothing in game.
The text the game shows is the `.rml`, which the analysis lists as
`languages/english/oasisstrings.fragment.xml`.

This page takes the one change outside the editor sections:
`section[Subtitles]/string[5153939][+1]`, a second copy of `5153939` ("Neither side has won."),
which the mod's tables carry twice. The `.rml` has the same duplicate, harmless since both copies
say the same.

The file is otherwise the source the English `.rml` was compiled from: the 1.00 text described in
`strings-english-release-text` plus the editor labels. Its `InGameEditor*` sections are the
editor's labels again and do nothing either; they are left to the editor pages, which should treat
them as dead too.
