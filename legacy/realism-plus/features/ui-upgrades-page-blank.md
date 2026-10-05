---
title: Upgrades page shows no purchase state
kind: component
bundle: ui
status: located
systems: [ui, economy, engine]
match: []
exclude: []
requires: [engine-dunia-dll]
verified: re
---

# Upgrades page shows no purchase state

On the pause menu's Upgrades page, each weapon row loses its three upgrade lines: the "not
available", "purchase" or "purchased" text and the bonus each upgrade gives. This is what the mod's
installation guide means by "The 'Upgrades' section of the pause menu is broken. You need to use the
weapon shop computer to see what upgrades you have bought."

## How

`InitItem` (GOG `0x1084E580`) fills one row. For each of the three upgrade kinds it finds a child
widget by name with `FUN_10527C10`: `a_unlock4`, then `a_unlock2`, then `a_unlock1`. It then writes
`NOT_AVAILABLE`, `UPGRADES_PURCHASE_WEAPON`, `UPGRADES_PURCHASED_NOCOLON` or the plan's bonus string
into it. The mod changes the underscore in each name to a dot (`a.unlock4` and so on). No widget has
those names, so every lookup returns null and the row's upgrade lines are skipped.

Why the mod does this is not stated. The likeliest reason is to hide states that no longer read
right once the mod makes upgrades available early. Whatever the intent, the page no longer shows
what is bought.

## Dunia.dll

| | Steam | GOG | Bytes |
|---|---|---|---|
| `a_unlock1` | `0x10EB12AD` | `0x10E28E31` | `5F -> 2E` |
| `a_unlock2` | `0x10EB12D5` | `0x10E28E59` | `5F -> 2E` |
| `a_unlock4` | `0x10EB12E1` | `0x10E28E65` | `5F -> 2E` |

## Uncertain

- The motive above is a guess. This is probably not wanted alone; it pairs with the economy
  changes, if with anything.
