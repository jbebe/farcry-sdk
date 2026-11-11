---
name: instance-clean
description: Use when asked to clean a game copy (C:\Games\fc2instances\<n>) of everything a session put into it, or for /instance-clean <n> - removes added plugins and files and restores the stock patch; the copy stays bound to its session
---

# Instance clean

A game copy under `C:\Games\fc2instances\<n>` holds the stock game: every stock file as a hard link
to `C:\Games\Far Cry 2`, the patch copied from its `.vanilla` backup. `stock.json` lists every stock
file. Sessions add plugins, FCSE, ini and log files, and rebuild the patch; nothing else. A stock file
written in place would change the master and every copy: replace one only by deleting it first.

Commands run from the repo root with the PowerShell tool.

1. `python scripts/fc2instance.py list` shows each copy and the session it is bound to.
2. If the copy is bound to another session whose worktree still exists, ask the user before going on.
3. `python scripts/fc2instance.py clean <n>`. It refuses while the game runs from that copy; games in
   other copies may keep running. The claim stays: a copy changes hands only through `/deploy`'s
   claim.
4. Report the script's line: added files removed, stock files restored. The mods' settings went with
   their `.ini` files, back to defaults.

Never delete a copy's folder recursively and never put a junction into one: its stock files are hard
links into the GOG install, and the script removes only what `stock.json` does not list.
