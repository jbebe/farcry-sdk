---
title: No tutorial popups
kind: component
bundle: gameplay
claims:
  - "Disabled intrusive dialog box popups"
status: located
systems: [ui, missions]
match:
  - "domino/system/{popuptutorialmessagebox,popuptutorialmessageboxwithami,custompopuptutorialmessagebox,custompopuptutorialmessageboxwithami}.lua@L*"
  - "domino/system/saveafterteleport.lua@L30"
exclude: []
requires: []
verified: diff
---

# No tutorial popups

The modal tutorial message boxes that mission scripts open never appear, and fast travel no longer
requests a save on arrival.

## How

- The four stock Domino boxes that open tutorial message boxes - `PopUpTutorialMessageBox`,
  `PopUpTutorialMessageBoxWithAMI`, `CustomPopUpTutorialMessageBox` and
  `CustomPopUpTutorialMessageBoxWithAMI` (`domino/system/popuptutorialmessagebox.lua@L34`,
  `popuptutorialmessageboxwithami.lua@L36`, `custompopuptutorialmessagebox.lua@L36`,
  `custompopuptutorialmessageboxwithami.lua@L38`) - comment out their
  `CGameMessageBoxHelper_GetInstance():Create...` call and fire `Out` at once, so every graph that used
  one continues without the box.
- `domino/system/saveafterteleport.lua@L30`: the `SaveAfterTeleport` box no longer sends
  `CPlayerSoundAndFXComponent_SaveAtNextUpdate` to the player. The only graph that uses the box is
  `fasttravel.fasttravel.lua`, after the teleport.

## Uncertain

- That `SaveAtNextUpdate` opens the same save prompt as sleeping (rather than a silent autosave) is an
  inference from its name and the bedroll's save step; this page assumes it is the popup the claim
  means.
- Messages the mod shows itself (the skip-intro question, fast-travel cost, purchase prompts) use the
  helper directly and are unaffected.
