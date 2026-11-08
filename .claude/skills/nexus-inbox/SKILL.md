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
3. Check for an issue on the same problem with
   `gh issue list --label nexus --state all --search "<keywords>"`. If one
   exists, plan to add the new report there with `gh issue comment` instead of opening a second issue.
4. **Triage with the user before touching GitHub.** Show your proposal: for each issue, the title,
   label and a short body draft, or the existing issue you would comment on. For each skip, one line
   with the reason. Then stop and wait. File only what the user approves, with their edits, and
   move whatever they reject to the skip pile.
5. File with `gh issue create --label nexus --label bug|enhancement`.
   Create the `nexus` label first if `gh label list` lacks it.
   - Title: `<mod short name>: <the problem in a few words>`.
   - Body: what is wrong or wanted, what the reporter tried, and their setup (versions, other mods).
     End with the item's link, its author and date.
   - Posts and bug reports are public on Nexus: quote them.
   - PMs are private and the repo is public. Describe the problem in your own words, and leave out
     the sender's name and anything personal.
6. Report to the user what you filed (issue links). Then run `node tools/nexus-inbox/inbox.mjs done`.
