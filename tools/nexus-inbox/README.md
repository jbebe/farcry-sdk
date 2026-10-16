# Nexus inbox

Turns new Nexus Mods comments, bug reports and private messages into GitHub issues. Nexus has no API
for any of the three and blocks automated page loads, so you open the pages yourself and a
bookmarklet captures them.

## Setup

Run `node inbox.mjs link` and save its output as the URL of a bookmark on Chrome's bookmarks bar.

## Use

1. Open a mod's Posts or Bugs tab, or a PM conversation on forums.nexusmods.com, and click the
   bookmark. It downloads the page's items to `Downloads/nexus-*.json`. A Posts tab with several
   pages needs one click per page.
2. Run `/nexus-inbox` in Claude Code. It proposes which unhandled items should become issues
   labeled `nexus`, and files only the ones you approve.

An item counts as handled when:
- it is in `seen.json` (local and gitignored), which fills with everything captured so far
- it is yours
- you replied after it in the same thread
- it is a bug report whose status is no longer New
