# Far Cry 2 extension for Vortex

**Before writing or editing any TypeScript in this tree, invoke the `implementation` skill**
(`.claude/skills/implementation/SKILL.md`).

## What this is

Far Cry 2 support for the Vortex mod manager. Vortex downloads, stages and orders the mods, and the
extension hands every build to `jackall-mi`, published from `tools/JackAll/src/JackAll.ModInstaller`.
`README.md` covers the design, what it installs and which source file does what.

## Vortex API quirks

Found against Vortex 2.8.

- **A start hook cannot cancel quietly.** Any rejection from a `registerStartHook` callback,
  `util.UserCanceled` included, stops the launch, but Vortex logs "Class constructor ProcessCanceled
  cannot be invoked without 'new'" and shows "Failed to run tool".
- **Play needs a discovered tool path.** A tool without `discovered.tools[id].path` starts with an
  empty executable. Dispatch `actions.addDiscoveredTool` with its path before making it the primary
  tool.
- **Vortex calls `ITool.queryPath()` with no arguments.** Never read a parameter in it.
- **Read Vortex's own code for how it behaves.** Its GitHub source reads easiest through
  raw.githubusercontent.com. The installed `resources\app.asar` is about 260 MB: search it with
  `grep -a -b -o -F` and read around a hit with `dd`, since a wide regex times out.

## Build

```
.\build.ps1            # publishes jackall-mi into dist\bin, bundles, zips vortex-farcry2-<version>.zip
.\build.ps1 -SkipCli   # the JavaScript only
npm run typecheck
npm test               # the built bundle against a stubbed vortex-api
```
