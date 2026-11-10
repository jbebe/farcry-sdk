# Docs site

Rules for writing a page: `docs/docs/`, JackAll's manual in `docs/jackall/`, and `src/pages/`.

## A page is documentation, not a changelog

A page says what is true now, organised by subject. How it came to be true is in `git log`, and what
a release changed is in that project's `CHANGELOG.md`.

- **When a fact changes, rewrite the sentence where it stands.** No "Corrected", "Reopened",
  "Update" or "Resolved" heading or note, no struck-through text, no "this page used to say".
- **Leave out the history of the work:** what was tried first, the theory that came before, what
  turned out wrong, when something was built or decided. A word that only makes sense against an
  earlier version of the page or the code ("no longer", "at first", "after the fact", "since
  2026-11-04", "now" set against before) marks a sentence to rewrite.
- **A new finding goes into the section about its subject**, not onto the end of the page.
- **No plans or session notes:** work steps, a starting state, "read this first", a test baseline.
  Open work is a GitHub issue.
- **A rejected approach stays only where a reader would reach for it**, as a rule with its reason:
  "the string table splits per string, because a weapon's name sits in its largest sections", not
  the story of how that was decided.
- **A provenance mark names the method and the build, never the date:** "(seen in game, GOG
  v1.03)", "(heard with DSOAL)", "(RE-verified)".
- **A community claim the engine contradicts stays, as a fact about the source:** "community guides
  say X; the engine does Y", because the reader meets the claim elsewhere. A mistake of this site's
  own is just fixed.
- Dates that are facts about the subject stay: when a build was compiled, the story's timeline.

Exempt: the design logs in `docs/docs/design/`, which keep their dated decisions, and the Almost
Complete Guide in `docs/docs/modding/guide/`, frozen by its own `CLAUDE.md`.
