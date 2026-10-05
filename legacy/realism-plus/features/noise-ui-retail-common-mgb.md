---
title: Retail copy of the common menu package
kind: noise
status: located
systems: [ui]
match:
  - "ui/localized/*/*/ui/common.mgb"
  - "ui/localized/*/*/ui/common.mgb.desc#**"
exclude: []
requires: []
verified: diff
---

# Retail copy of the common menu package

The mod ships `common.mgb` and `common.mgb.desc` for five interface languages, but they are not
edits: they are the retail (GOG) patch's copies, byte for byte.

## Why it is noise

- `ui/localized/<pc|pcwidescreen>/<eng|fre|ger|ita|spa>/ui/common.mgb` (10 files, 111,069 bytes)
  equal the GOG `patch.dat`'s copies. Steam's patch carries a newer `common.mgb` (114,767 bytes),
  so against the Steam baseline each shows as a whole-file change.
- The 1,020 `common.mgb.desc` changes are the same retail copies: the 54 texture dependencies are
  the same set as Steam's, listed in a different order, so every reordered `CTextureResource[i]` `ID`
  and `crc_ID` reads as a change.

Nothing in these files was authored for the mod. What they do on a Steam install is undo Steam's
version: decoded, Steam's `common.mgb` has two more areas, `#2F024CF2` (the wide 700x41 prompt
button) and `#7E860205`, and a longer navbar `#E58F0F6C`; Steam's string table adds the Ubisoft
online-profile and exclusive-content key strings these belong to (inference from the strings). The
Czech, Hungarian, Polish and Russian packages keep Steam's copy.

## Uncertain

- Whether any single-player screen on Steam needs the two missing areas is not checked; the
  online-profile screens are the likely users.
