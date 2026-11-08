---
name: release-check
description: Use when asked whether a mod can be released, to check a mod against the release bar, or for /release-check <mod> [hotfix]
---

# Release check

`/release-check <mod>` checks a mod against the user's release bar and marks each row pass, fail,
stale or review, with what to do about it. `/release-check <mod> hotfix` is for a player waiting on
a fix: every row applies except the user's re-check of the BBCode and short message.

1. Run it with the PowerShell tool (Python is blocked in Bash). The build and tests take a minute or
   two; they are the steps the mod's release workflow runs, and each writes a log it names.

   ```powershell
   python scripts/release_check.py <mod>            # or: <mod> --hotfix
   ```

2. Go through the two review rows yourself, reading each candidate in its file:
   - **debug lines**: a log that is not a failure, a TODO or debug flag, commented-out code. A trace
     or leftover goes; anything kept needs a reason you can state in a few words.
   - **unused parts**: a function, constant, relocation or setting nothing reads, a layer file the game
     and the mod never name. Hashed or generated names (MOVE clips, materials named inside a mesh)
     show up here though they are used: confirm by the generator or the binary that names them.
   Fix what is real (removing a setting or a feature the user can see needs their OK first), then
   report the row as pass or fail.
3. Rows that need you:
   - **simplify**: run `/simplify` over everything since the last tag (`git diff <tag>..HEAD --
     <mod dir>`), apply and commit what it finds, then record `- simplify: <sha> <date> - since <tag>`
     under `## Checks` in the mod's STATUS.md.
   - **played**, **page text**: only the user can give these. Ask; record what they say at
     `git rev-parse --short HEAD`, never on their behalf.
   - **changelog**: the release needs a `## [x.y.z] - date` section newer than the last tag
     (RELEASING.md).
4. Show the result as the script's table plus what you did in step 2. When every row passes, say the
   mod is ready and stop: the user pushes and runs the workflow. Never push, tag or run a workflow.
