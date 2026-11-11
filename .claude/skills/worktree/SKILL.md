---
name: worktree
description: Use when the user starts real work - asks to implement, build or create a feature, or to fix agreed work - to bind the session to its own detached worktree, and when the user says that work is done, to squash it into one commit on main
---

# Worktree

A session binds a detached worktree under `.claude/worktrees/<name>` when the user starts real work:
implement, build or create a feature, fix agreed work. Questions, searches, analysis and plans stay in
the main tree, and small edits there are committed the old way. The worktree's work reaches `main` as
a single commit, and only when the user says it is done. A game copy is a separate binding, made by
the first `/deploy`.

## Start

1. `git worktree add --detach .claude/worktrees/<name> main`, with `<name>` a short topic
   (`inventory-tabs`).
2. Enter it with the EnterWorktree tool, `path` set to that folder.

## During

- Commit in the worktree as often as is useful; the subjects are notes for the final one.
- Never edit, stage or commit in the main working tree.
- Builds start fresh in a new worktree: `out\`, `bin\` and `node_modules\` are per tree.
- Gitignored inputs live only in the main tree: `tmp\`, JackAll's test `Fixtures\`, fc2drive's
  approved pictures. Code reads them there through `MAIN_TREE` (`scripts/modstatus.py`), and a new
  reader does the same; a path to `tmp\` in a skill means the main tree's.
- No junctions into or out of the worktree: `git worktree remove` follows them.

## Done

Only when the user says the work is done:

1. Commit what is left in the worktree.
2. Run `python scripts/affected_mods.py` over `git diff --name-only main...HEAD` and add the
   CHANGELOG.md and STATUS.md lines the `implementation` skill asks for; commit them.
3. In the main working tree: `git -C <main> merge --squash <worktree HEAD>`. It refuses when a file
   it changes has uncommitted edits there: ask the user. On a conflict, resolve it in the main tree
   and tell the user what was resolved.
4. `git -C <main> commit -m "<Project>: <what changed>" -- <the paths from step 2>`, subject line only.
5. ExitWorktree with `keep`, then `git worktree remove .claude/worktrees/<name>`. Folders the editor's
   C# server still holds stay behind empty; they go once it lets go.

The session's game copy stays bound; a later `/deploy` of another session may take it over, since
its worktree is gone.
