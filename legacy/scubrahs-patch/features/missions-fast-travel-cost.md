---
title: Optional diamond cost for fast travel
kind: component
status: located
systems: [missions, economy, ui]
match:
  - "domino/user/fasttravel/fasttravel.fasttravel.lua@L{1314,1321,1328,1335,1342,1349,1356,1363,1370,1377,1384,1391,1398,1405,1412,1419,1426,1433,1440,1447}"
  - "domino/user/fasttravel/fasttravel.fasttravel.lua@L{1478,1510,1542,1574,1606,1638,1670,1702,1734,1766,1798,1830,1862,1894,1926,1958,1990,2022,2054,2086}"
  - "languages/*/oasisstrings.fragment.xml#MessagesBoxContents/FTL_MESSAGE_PAID_*"
  - "languages/*/oasisstrings.fragment.xml#Tutorial/INSUF_MESSAGE"
exclude: []
requires: [missions-game-globals, user-configurable, economy-diamond-counter]
verified: diff
---

# Optional diamond cost for fast travel

With `FastTravelCost` set to 1-10 in `ScubrahsPatch.lua`, each bus ride costs that many diamonds and is
refused when the player has fewer. The default, `0`, leaves fast travel free. Not on the published
list by itself; it is one of the settings the "User Configurable" line refers to.

## How

`domino/user/fasttravel/fasttravel.fasttravel.lua`, two runs of 20 hunks, one per destination:

- `@L1314`..`@L1447`, where the graph opens the destination's confirmation box: when `FastTravelCost`
  is between 1 and 10 and `DiamondCounter` is at least the cost, the box uses text
  `FTL_MESSAGE_PAID_<cost>`; with too few diamonds it opens `INSUF_TITLE` / `INSUF_MESSAGE` with
  `BUTTON_CONTINUE` instead and the trip does not happen. With the cost at 0 the vanilla
  `FTL_MESSAGE` box opens as before.
- `@L1478`..`@L2086`, after the trip is confirmed: `AddDiamonds(-FastTravelCost)` and the same amount
  taken from `DiamondCounter`.
- New strings in all nine languages, in English: `MessagesBoxContents/FTL_MESSAGE_PAID_1`..`_10` (the
  vanilla question "Are you sure you wish to travel to the indicated location?" followed by
  `Cost:` and the diamond glyph `~DI` with the amount) and `Tutorial/INSUF_MESSAGE` ("You don't have
  enough diamonds for this purchase."). The mod's GPS-upgrade purchase script also references
  `INSUF_MESSAGE`, in a path its economy pages call obsolete.

## Depends on

- `user-configurable` supplies `FastTravelCost` from the file; `missions-game-globals` declares it.
- `economy-diamond-counter`: the scripts cannot read the player's diamonds, so they test the mod's own
  running count `DiamondCounter`; without the trackers that keep it current every paid trip is refused.
