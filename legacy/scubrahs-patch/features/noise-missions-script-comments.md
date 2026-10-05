---
title: Comment, log and whitespace edits in mission scripts
kind: noise
systems: [missions]
match:
  - "domino/system/gameelementobjective.lua@L{34,41}"
  - "domino/system/objectivestate.lua@L{1,20,24,28,35,38}"
  - "domino/system/playbark.lua@L97"
  - "domino/system/setsidequestmissionstate.lua@L1"
  - "domino/user/common_buddysidequests.bsq_missionbriefing.lua@L*"
  - "domino/user/common_buddysidequests.bsq_missiondebriefing.lua@L{223,241,258}"
  - "domino/user/common_customboxes.missionacceptedbroadcast.lua@L{240,241,357}"
  - "domino/user/common_customboxes.missioncompletebroadcast.lua@L{95,448}"
  - "domino/user/master_world1.world1.lua@L954"
  - "domino/user/master_world2.world2.lua@L1144"
  - "domino/user/sidemissions/sidemissions.mikesplace_bsqmission_and_pawnbrief_selection.lua@L{666,707,783,1312}"
exclude: []
verified: diff
---

# Comment, log and whitespace edits in mission scripts

Hunks in the mission scripts whose code, once comments, blank lines, indentation and
`System:Log`/`System:Trace` lines are set aside, is identical to the base game's. They change nothing
in game. Every other hunk of these files is on a feature page.

- **Comments and logging.** New or removed comment lines (`master_world1@L954`, `master_world2@L1144`,
  `missionacceptedbroadcast@L240`, `@L241`, `missioncompletebroadcast@L95`, `pawnbrief_selection@L1312`),
  `-- left empty on purpose` and `-- Export visible for Nomad engine.` removals
  (`objectivestate@L20`..`@L35`), and a log line (`pawnbrief_selection@L783`).
- **Disabled experiments.** Unchanged calls wrapped in a commented-out `if` the mod's comments mark
  "currently unused - not needed" or "reverted in 3.1" (`bsq_missionbriefing` all five hunks,
  `bsq_missiondebriefing@L223`, `@L241`, `@L258`, `pawnbrief_selection@L666`, `@L707`).
- **Re-indentation** (`gameelementobjective@L34`, `@L41`, `playbark@L97`) and **a line break lost
  at the end of the file** (`objectivestate@L38`,
  `missionacceptedbroadcast@L357`, `missioncompletebroadcast@L448`).
- **Reflection headers.** `objectivestate@L1` and `setsidequestmissionstate@L1` drop the
  `-- DOMINO REFLECTION BOX` comment block (the second replaces it with a note that the box is "mostly
  redundant"). The block is the Domino editor's palette entry; that the engine never reads it is
  inferred from the mod running without it in these and other boxes.

The same kinds of hunks in `setmissionstate.lua`, `delay.lua` and `postfx.lua` stay with the pages
that own those files (`patrols-set-mission-state`, `outposts-delay-presets`, the graphics pages).
