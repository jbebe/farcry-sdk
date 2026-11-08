---
name: nexus-inbox
description: Use when asked to process the Nexus inbox - turn captured Nexus Mods comments, bug reports and PMs into GitHub issues
---

# Nexus inbox

The user captures Nexus pages with the bookmarklet in `tools/nexus-inbox/` (see its README). Never
open or fetch Nexus pages yourself.

1. Run `node tools/nexus-inbox/inbox.mjs`. Each entry is a thread (comment thread, bug report, or PM
   conversation): `new` lists the ids of its unhandled items, and `thread` holds every captured item
   for context. An empty list means nothing to do: say so, run `done`, stop.
2. Sort each item into one of two piles:
   - **issue**: a bug, crash, compatibility problem or feature request about one of our mods.
   - **no issue**: thanks, praise, a question answered in the thread, chatter, or anything outside
     our mods.
3. Catch duplicates: search every open issue, whatever its labels, with
   `gh issue list --state open --search "<keywords>"`, and closed Nexus reports with
   `gh issue list --label nexus --state closed --search "<keywords>"`. Check the mod's STATUS.md too:
   a feature line may already cover it. Where one exists, plan to add the report there with
   `gh issue comment` instead of opening a second issue.
4. Give each issue three labels:
   - type: `bug`, `feature` (a concrete change asked for) or `idea` (not decided or designed yet);
   - priority: `p1` (a player is blocked, crashes or loses progress), `p2` (soon), `p3` (some day);
   - mod: the `mod:<folder>` whose STATUS.md `Purpose:` it serves. When none fits, `needs-home`, with a
     suggested new mod - a name and a one-line purpose.
5. **Triage with the user before touching GitHub.** Show your proposal: for each issue, the title,
   the three labels and a short body draft, or the existing issue you would comment on. For each
   skip, one line with the reason. Then stop and wait. File only what the user approves, with their
   edits, and move whatever they reject to the skip pile.
6. File with `gh issue create --label nexus --label <type> --label <priority> --label <mod>`.
   - Title: `<mod short name>: <the problem in a few words>`.
   - Body: what is wrong or wanted, what the reporter tried, and their setup (versions, other mods).
     End with the item's link, its author and date.
   - Posts and bug reports are public on Nexus: quote them.
   - PMs are private and the repo is public. Describe the problem in your own words, and leave out
     the sender's name and anything personal.
7. Report to the user what you filed (issue links). Then run `node tools/nexus-inbox/inbox.mjs done`.

Issues hold what is not planned yet. Once the user picks one for a version, it becomes a feature line
in that mod's STATUS.md that links to it (`- [new] ... #12`), and the issue stays open until the
feature ships.
