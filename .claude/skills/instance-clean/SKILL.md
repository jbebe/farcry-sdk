---
name: instance-clean
description: Use when asked to clean a game copy (C:\Games\fc2instances\<n>) of everything a session put into it, or for /instance-clean <n> - removes added plugins and files, restores the stock patch, keeps or frees the copy's claim
---

# Instance clean

A game copy under `C:\Games\fc2instances\<n>` holds the stock game: `bin\` as copies, the archives
as hard links to `C:\Games\Far Cry 2`, the patch from its `.vanilla` backup. `stock.json` lists every
stock file. Sessions add plugins, FCSE, ini and log files, and rebuild the patch; nothing else.

Commands run from the repo root with the PowerShell tool.

1. `python scripts/fc2instance.py list` shows each copy and the worktree that holds it.
2. The game must be closed in that copy: no `FCSE` or `FarCry2` process whose path is under it. Another
   copy's game may keep running.
3. If the copy is held by a worktree other than this session's and that worktree still exists, ask the
   user before going on.
4. `python scripts/fc2instance.py clean <n>` keeps the claim; `release <n>` cleans and frees the copy.
   Release when the worktree is done or gone; clean when the same session starts over.
5. Report the script's line: added files removed, stock files restored.

Never delete a copy's folder recursively and never put a junction into one: the archives are hard
links into the GOG install, and the script removes only what `stock.json` does not list.
