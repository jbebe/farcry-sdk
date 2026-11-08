---
name: status
description: Use when asked where the mods stand, what a mod still needs, or to record a feature, approval, release order or collision decision in a mod's STATUS.md
---

# Status

`/status` shows the board, `/status <mod>` one mod in full, `/status fit` which features belong in
another mod. Run the script with the PowerShell tool (Python is blocked in Bash) and show its output
as it is - it is already Markdown:

```powershell
python scripts/status.py            # every mod, one table
python scripts/status.py <mod>      # folder, title, tag prefix or a unique start of one
python scripts/status.py --fit      # purposes beside features
```

For `fit`, read each mod's purpose against its features and suggest, per feature that does not serve
it, the mod it fits or a new mod (a name and a one-line purpose). Suggest only; the user decides,
and a move is a `scrapped - moved to <mod>` line in one file and a new line in the other.

Everything on the board is computed fresh except what STATUS.md records: tags and commits since them
(a mod's own folder plus what its build files point at), open issues by `mod:*` and `p1`-`p3`
labels, staleness, the release-bar rows cheap enough to run, the Nexus reminder and collisions.
`scripts/modstatus.py` has the rules for each.

## STATUS.md

One per project, at `<project>/STATUS.md`, so parallel sessions on different mods never edit the same
file. It holds only decisions. `scripts/modstatus.py` parses it; a line it cannot read shows on the
board as a bad line.

```markdown
# Aiming Overhaul

Purpose: how the player aims, from input to the shot leaving the gun.
Stage: active

## Features

- [done] Sway in four layers - user: "perfect"
- [in progress] Lean on new controls
- [new] Per-shot bob in full auto #40
- [on hold] Auto lean
- [postponed] Shooting from vehicle windows #11
- [scrapped] Gun weight - dropped by the user

## Approvals

- played: 1b343c9 2026-11-08 - "this is it now"
- page text: ccf0eed 2026-11-08 - BBCode and short message checked

## Checks

- simplify: 3f2a9c1 2026-11-09 - since aiming-overhaul-1.2.0

## Release order

- after jackall-1.4.1
- Nexus file IDs set up

## Collisions

- flashlight: deploy apart - both drive the light from the same input pass
- 0x0071AE70: stacks - the input dispatcher; each pattern starts past the others' hook bytes
```

- **Stage** is a word: `active`, `maintenance`, `on hold`, `unfinished` or `scaffold`.
- **Features**: one line each, in one of the six states. A `#n` links its issue. Issues hold what is
  not planned; once one is picked for a version it becomes a feature line here that links to it.
- **Approvals** are the user's, each at the commit it was given: `played` (played, and it's good) and
  `page text` (nexus-description.bbcode and nexus-summary.txt checked). Record one the moment the
  user says so in chat, at `git rev-parse --short HEAD`, with their words as the note, and tell them
  in one line. `played` goes stale when the mod's code or data changes after it, `page text` when
  either text file does - the board computes both.
- **Checks** records `simplify`: `/simplify` ran over everything since the last tag, at that commit.
- **Release order**: `after <tag>` is checked against the tags; any other line is shown as it is.
- **Collisions**: a decision about a clash `scripts/collisions.py` finds, keyed by the other mod's
  folder or by the shared thing (a hooked function's `0x` address, a container such as `hud.mgb`, a
  control), written into both mods' files. A clash with no decision in either file stays on the board.
