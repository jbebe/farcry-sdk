---
title: Arms dealer's "next mission unlocks" panel matches the new unlocks
kind: component
bundle: weapons
status: located
systems: [ui, weapons]
match:
  - "ui/localized/*/*/ui/hud.mgb"
exclude: []
requires: [economy-bazaar-reorder]
verified: diff
---

# Arms dealer's "next mission unlocks" panel matches the new unlocks

The HUD panel that says which weapons the arms dealer's next mission unlocks (`TU70A_MESSAGE`,
"Your next mission will unlock the following weapons:") lists the mod's weapon set: no flare gun, a
Dragunov where the Silent MP-5 was, an Uzi where the Dragunov was.

## How

`ui/localized/<pc|pcwidescreen>/<eng|fre|ger|ita|spa>/ui/hud.mgb`, 10 whole files of unchanged size
(190,532 bytes). Decoded, all ten carry the same seven edits in the page that holds the unlock
panels, each panel three icon-and-name slots:

| Panel (base) | Slot | Base | Mod |
|---|---|---|---|
| Star .45 / Flare Gun / 6P9 | 2 | `hud_icon_flairgun`, `WEAPONBAZAAR_FLARE_CRATE_NAME`, and its `hud_gun_stain` backing | all three blanked (material `#00000000`, an empty text) |
| SPAS-12 / MP-5 / LPO-50 | 2 | `hud_icon_MP5SD`, `WEAPONBAZAAR_MP5_CRATE_NAME` | `hud_icon_dragunov`, `WEAPONBAZAAR_DRAGUNOV_CRATE_NAME` |
| Dragunov / M-79 / Dart Rifle | 1 | `hud_icon_dragunov`, `WEAPONBAZAAR_DRAGUNOV_CRATE_NAME` | `hud_icon_UZI`, `WEAPONBAZAAR_UZI_CRATE_NAME` |

The rest of the package (and its `.desc`) is untouched.

## Depends on

- `economy-bazaar-reorder`, the shop and unlock rewiring this panel describes, and
  `weapons-flare-gun-gadget`, why the flare gun leaves the list.

## Uncertain

- Only five interface languages are shipped; Czech, Hungarian, Polish and Russian keep the vanilla
  panel.
- Which arms-dealer mission each panel belongs to is read from its weapons, not traced.
