---
name: worktree
description: Use at the start of a session that will change files, and when the user says a session's work is done - creates the session's detached worktree, gives it a game copy, and squashes it into one commit on main
---

# Worktree

Each session works in its own detached worktree under `.claude/worktrees/<name>` and its own game
copy. Its work reaches `main` as a single commit, and only when the user says it is done.

## Start

Before the session's first edit:

1. `git worktree add --detach .claude/worktrees/<name> main`, with `<name>` a short topic
   (`inventory-tabs`).
2. Enter it with the EnterWorktree tool, `path` set to that folder.
3. When the session needs the game, `python scripts/fc2instance.py path` (PowerShell tool) prints its
   copy, claiming a free one. fc2drive and `/deploy` find the same copy themselves.

## During

- Commit in the worktree as often as is useful; the subjects are notes for the final one.
- Never edit, stage or commit in the main working tree.
- Builds start fresh in a new worktree: `out\` and `node_modules\` are per tree.
- No junctions into or out of the worktree: `git worktree remove` follows them.

## Done

Only when the user says the work is done:

1. Commit what is left in the worktree.
2. Run `python scripts/affected_mods.py` over `git diff --name-only main...HEAD` and add the
   CHANGELOG.md and STATUS.md lines the `implementation` skill asks for; commit them.
3. In the main working tree: `git -C <main> merge --squash <worktree HEAD>`. On a conflict, resolve it
   in the main tree and tell the user what was resolved.
4. `git -C <main> commit -m "<Project>: <what changed>"`, subject line only.
5. `python scripts/fc2instance.py release <n>` for the session's copy (`/instance-clean`).
6. ExitWorktree with `keep`, then `git worktree remove .claude/worktrees/<name>`.
