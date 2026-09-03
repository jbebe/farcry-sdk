# Releasing

A release is set up entirely in the project's `CHANGELOG.md` — the version and the notes both come
from it. Running the workflow publishes what is written there, and fails if nothing was written.

## What releases

Each is standalone: its own changelog, its own workflow, its own tag series, released on its own
schedule.

| Project | Directory | Tag | Also declares its version in |
|---|---|---|---|
| JackAll | `tools/JackAll` | `jackall-<version>` | |
| FCSE | `tools/FCSE` | `fcse-<version>` | |
| UFCP | `mods/UFCP` | `ufcp-<version>` | |
| Vortex extension | `tools/vortex-farcry2` | `vortex-<version>` | `info.json` |
| Blender add-on | `tools/BlenderFC2` | `blenderfc2-<version>` | `blender_manifest.toml` |
| VSS Vintorez | `mods/vss-vintorez` | `mod-vss-<version>` | |

The Blender add-on's release installs Blender on the runner before it builds. Blender builds its own
extension zips — a hand-made one will not install — so that is a build prerequisite there in the way
the MSVC toolchain is for FCSE.

## Cutting a release

1. In the project's `CHANGELOG.md`, add a section for the version you are releasing, above every
   older one:

   ```markdown
   ## [1.1.0] - 2026-08-23

   ### Added
   - ...
   ```

   Keeping an `## [Unreleased]` section above it, for notes still being written, is fine — a release
   skips it. Two projects declare their version a second time, and it has to agree: the Vortex
   extension's `info.json`, and the Blender add-on's `blender_manifest.toml`. Both are where the
   host application itself reads the version, and the add-on's also names the zip.

2. Commit and push to `main`.

3. Actions → the `<project> release` workflow → **Run workflow**. There is nothing to fill in.

The run takes the newest version in the changelog, builds, publishes the GitHub release under the
tag `jackall-1.1.0`, and uploads to Nexus Mods.

## What stops a release

Nothing is built until the changelog has been read, so a release that was not set up fails in
seconds rather than after a full build:

- the changelog documents no version yet;
- its newest version is already tagged, which is what happens when the changelog was not updated
  for this release;
- that version's section is empty;
- for the Vortex extension, `info.json` disagrees with the changelog;
- for the Blender add-on, `blender_manifest.toml` disagrees with the changelog.

## Where the notes go

`scripts/changelog.ps1` renders one section two ways and a release publishes both. Run it to see
either before releasing, adding `-Section Unreleased` to preview notes still being written:

```powershell
./scripts/changelog.ps1 -Path tools/JackAll/CHANGELOG.md -Render nexus
```

- `-Render markdown` — the section verbatim, as the GitHub release body.
- `-Render nexus` — plain text, as the Nexus Mods changelog entry. Nexus renders neither Markdown
  nor BBCode there, so headings become `Added:`, emphasis and backticks are dropped, links become
  `text (url)`, and each hard-wrapped bullet is joined back into the one line it reads as. Relative
  links resolve on GitHub but are dead text on Nexus, so keep changelog links absolute or drop them.
- `-Render version` — the version alone, which is the one the workflow releases.

## One-time setup per project

Nexus uploads are the only part that needs configuring. Until it is done the workflow logs a warning
for each artifact and skips it — everything else still runs.

1. Add the repository secret `NEXUSMODS_API_KEY` (Nexus Mods → Settings → API keys).
2. Fill in the `env:` block at the top of `.github/workflows/<project>-release.yml`:
   - `NEXUS_MOD_ID` — **not** the ID in the mod page URL. That URL ID is game-scoped, and the
     internal one is a composite UID: `(game_id << 32) | game_scoped_id`. Far Cry 2 is game 1471, so
     for any mod on that page it is `6317896892416 + <the ID in the URL>` — 368 gives
     6317896892784, 370 gives 6317896892786. The API answers with the same number:

     ```powershell
     $key = Read-Host 'Nexus API key'
     (Invoke-RestMethod 'https://api.nexusmods.com/v3/games/farcry2/mods/368' -Headers @{ apikey = $key }).id
     ```

     Needed only so a release can post its changelog entry; without it the upload still happens,
     minus the changelog.
   - one `NEXUS_FILE_*` per artifact — on the mod's Files tab, under "API Info".

An upload adds a version to a file entry that already exists, so each artifact needs its file
created by hand on the mod page once. The version it replaces is archived.

## Adding a new project

1. Give it a `CHANGELOG.md` with no version sections yet - its first release writes the first one.
2. Copy the nearest `<project>-release.yml` and change the tag prefix and directory it prepares
   from, the build steps, the artifact names and the Nexus `env:` block.

A weapon replacement has no build steps to change: it ships the `layer/` that is already checked in.
Copy `vss-release.yml`, which packages one, and only its directory, tag prefix, artifact name and
`env:` block differ. It also needs a `README-release.md` — the repository README links to sibling
directories by relative path, and every one of those is dead once the file is alone inside a zip.
