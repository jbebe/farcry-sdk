---
title: The mod's Dunia.dll
kind: shared
status: located
systems: [engine]
match:
  - "install/bin/dunia.dll"
exclude: []
requires: []
verified: re
---

# The mod's Dunia.dll

A shared piece: the engine binary every variant ships, and the home of nine features. It is the GOG
build of `Dunia.dll` (19,412,104 bytes) with 11 byte runs changed. Against the Steam baseline the
analysis records it as one whole file, "a different build of this binary". The runs below come from
diffing it against GOG's own `Dunia.dll`. Shipping GOG's build also gives Steam players the GOG
engine, which gates bonus content on registry values instead of Ubisoft's privileges service.

The four engine variants (Limited Saving or Save Anywhere, Flashing or No Flashing Items) share nine
runs. They differ only in whether two save strings and the highlight string are blanked. The analysed
variant is Limited Saving with No Flashing Items.

| GOG RVA | Bytes | Page |
|---|---|---|
| `0x48987` | `8A C3 → B0 01` | [`dlc-predecessor-tapes`](dlc-predecessor-tapes.md) |
| `0x740F55` | `0A → 14` | [`fixes-jackal-tapes`](fixes-jackal-tapes.md) |
| `0xDC1A90` | `Mesh_Highlight` → zeros | [`graphics-no-flashing-items`](graphics-no-flashing-items.md) (No Flashing Items only) |
| `0xE0AF80` | `gadgets.ObjectiveIcons.SaveDiskGPS`, `…SaveDisk` → zeros | [`nav-no-save-icons`](nav-no-save-icons.md) |
| `0xE2273C`, `0xE286C0` | `CSaveGamePage`, `PAUSE_SAVEGAME` → zeros | [`saving-no-pause-save`](saving-no-pause-save.md) (Limited Saving only) |
| `0xE28E31`, `0xE28E59`, `0xE28E65` | `_ → .` | [`ui-upgrades-page-blank`](ui-upgrades-page-blank.md) |
| `0xE2A2BC`, `0xE2A308` | `dlc6`/`dlc5` → `sawedoffshotgun`; `sawedoffshotgun` → zeros | [`weapons-sawedoff-hud-icon`](weapons-sawedoff-hud-icon.md) |

Each component page has the sites in both builds. A layer cannot carry any of this: picking one means
an FCSE plugin, or shipping this whole binary to GOG players.
