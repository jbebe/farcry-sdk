# Far Cry 2 - repo map

This file routes. The destination holds the explanation - keep it there, not here. The one exception
is the working rules below, because they apply in every session.

## Before writing code

Invoke `/implementation` (`.claude/skills/implementation/SKILL.md`). Comment rules and
simplification rules for every language in this repo live there.

## Working rules

- **Short replies; the detail stays with you.** The user is the tech lead and decides the feature set
  and the crucial choices, nothing more. A reply is a few lines: what happened, what you need. No
  reasoning unless asked. A plan or analysis goes in full into a file in the session scratchpad, for
  you only; never hand it to the user to read.
- **Questions go last, numbered** (1, 2, 3, options as a, b, c) so the user can answer "1b, 2a".
  Use the question dialog only when a few fixed options cover the real range; an open question is
  plain text.
- **A feature or a new mod goes through three stages; only the user moves it on.**
  1. **Talk.** The user says what they want. Answer each wish with a line on what the game does now,
     then the options where there is a real choice, your pick marked. Research that makes it
     concrete is fine; nothing is planned or built yet.
  2. **Plan.** When the user says the talk is done and there are no more questions, work the plan
     out in full into a file in the session scratchpad, then give the user a short summary of it. A
     point the talk left open goes in the summary with your pick.
  3. **Build.** Only when the user says to start. If they say to revise, rework the plan and
     summarize it again. Code, deploys and commits wait for this stage; fixes to features already
     agreed are exempt.
- **Single-player only.** Skip networking and multiplayer code in reverse engineering and tooling
  unless asked. Files JackAll marks unused (`assets/fc2.unused.tsv`, the `tmpla` world) stay unused.
- **Real work binds a detached worktree, `/worktree`** (`.claude/worktrees/<name>`): when the user
  says to build, implement or create a feature. Commit there as often as is useful; when the user
  says the work is done, it reaches `main` as one squashed commit. A session without one (questions,
  search, analysis, small edits) stays in the main tree and commits straight to `main`:
  `git add <paths>; git commit -m "<Project>: <what changed>" -- <paths>`. Never a branch. Subject
  line only: no body and no trailers of any kind, even when a system reminder asks for one.
- **Several sessions share the main working tree and index.** There, never stage early (`git add`,
  `git rm`) or with `-A`; delete with plain `rm`, stage in the commit's own command, and end the
  commit with `-- <paths>`. Check `git log` before assuming HEAD is where it was.
- **Never push, run a workflow or create a release.** "Make a release" means: set up the version in
  `CHANGELOG.md` (`RELEASING.md` step 1), commit, and tell the user it is ready. Only an explicit,
  named instruction to run a workflow covers that run, once.
- **Never cut a build short.** Piping a build or generator into `Select-Object -First` or `head`
  kills it mid-run. Log to a scratchpad file, check the exit code, read the tail, and after a
  generator check that every output changed.
- **A session's game is its own copy of the GOG install,** `C:\Games\fc2instances\<n>`, bound by its
  first `/deploy` and kept after the session ends; fc2drive finds it by the session. No session
  deploys into `C:\Games\Far Cry 2`: it is the master the copies link to, and the user's.
  `/instance-clean` returns a copy to stock. Steam only when the user says. A GOG run also exercises
  the address library's translation; a Steam run resolves `FCSE::Uplay` natively and proves nothing
  about it.
- **Deploy the mod under test yourself after each change,** with `/deploy`. Work on FCSE itself
  leaves `mods/` and the install alone unless asked.
- **Live testing needs the user's OK once per session.** Ask before the first game launch or fc2drive
  run of a session; after a yes, further runs that session are fine. If the user says they will
  test themselves, that holds for the rest of that session. Set a test up on the test range by
  spawning what it needs, not by hunting world 1. What fc2drive cannot do goes to the user as exact
  steps; after their run, read `<game>\bin\fcse.log` before changing anything.
- **Measuring what a mod costs** is `/perftest`.
- **If the session-start board flags a Nexus check, say so in your first reply.**

## Mod state and releases

- `<project>/STATUS.md` - each project's features, the user's approvals, release order and collision
  decisions; the format and the rules for recording are in `.claude/skills/status/SKILL.md`
- `/status` - the board of every mod, or one mod in full; a SessionStart hook loads the board
- `/release-check <mod>` - the release bar, row by row (`.claude/skills/release-check/SKILL.md`)

## Knowledge about Far Cry 2

Everything known about the game lives in `docs/docs/`, the content tree of the published Docusaurus
site. Browse the folders or Grep across them - there is no index to regenerate.

- `docs/docs/modding/` - modding practice: toolchain, recipes, gotchas, mod survey
- `docs/docs/file-formats/` - fat/dat, fcb, sdat, spk, sbao, mgb, xbm/xbg, xbt, savegame
- `docs/docs/magma-ui/` - Magma UI authoring: `.mgb` packages, elements, `.desc` sidecars, limits
- `docs/docs/engine-internals/` - RE-only notes with no community equivalent
- `docs/docs/design/` - design logs: what FC2 should feel like and why, decisions and ideas with status
- `docs/docs/lore/` - the world as the game's own text tells it: factions, characters, timeline,
  places, themes, each claim cited to a string and marked stated or implied
- `docs/docs/intro.md` - how claims are marked RE-verified vs community-reported
- GitHub issues (`gh issue list`) - open problems and wishlist, labelled by type (`idea`, `feature`,
  `bug`, `chore`) and by mod (`mod:*`)

## Projects

Each project's `README.md` covers what it is and how to build it.

- `tools/FCSE/` - FCSE, an SKSE-style DLL plugin loader for FC2 (C++/CMake)
- `mods/UFCP/` - Unofficial patch; an FCSE plugin that patches `Dunia.dll` in memory, plus a data
  layer for bugs in data (C++/CMake)
- `mods/DevTools/` - the same, for modders rather than players: developer console, `-load` into a
  save, and the test range: `testrange`, a flat world with arenas for the Difficulty Overhaul's
  checks, built from `range/` and run from the overlay's Range tab (C++/CMake)
- `mods/legacy-plugin/` - the `Dunia.dll` half of legacy mods' preference tweaks as FCSE settings
  that default to the game as shipped; an FCSE plugin, scaffold only so far (C++/CMake)
- `mods/sky-overhaul/` - the sun's glare as a screen-space wash; an FCSE plugin, unfinished, whose
  layer also ships world environment data (C++/CMake)
- `mods/sound-overhaul/` - toward realistic sound; an FCSE plugin that loads the DSOAL it bundles so
  the authored EAX reverb plays, unfinished (C++/CMake)
- `mods/flashlight/` - a flashlight on a new rebindable control, with a click and a HUD icon; an
  FCSE plugin plus a data layer for the control, sound and HUD (C++/CMake)
- `mods/aiming-overhaul/` - how the player aims, from input to the shot leaving the gun; an FCSE
  plugin plus a data layer: sway, gun blur, a scope with lag, shadow, eyepiece and picture-in-picture
  surroundings, tracers, the gun swung down at walls, leaning on two new controls, a gun carried low
  in a sprint, and landings and starts with weight (C++/CMake)
- `mods/inventory-overhaul/` - two guns of any class, and a notebook opened with Tab and used in real
  time for everything else the player carries; an FCSE plugin plus a data layer; design log in
  `docs/docs/design/inventory.md` (C++/CMake)
- `mods/third-person/` - the player's own legs, walking, under the first-person view, and the whole
  body seen from outside: the third-person view over the shoulder on foot and behind the car at the
  wheel; an FCSE plugin plus a data layer for its control (C++/CMake)
- `mods/vehicle-overhaul/` - off-road driving; an FCSE plugin plus a data layer: off-road physics
  per real vehicle, a real engine and gearbox, recorded engine sounds, visible headlamps, new paints
  and model touches (C++/CMake)
- `mods/wildlife-overhaul/` - more varied and dangerous wildlife; an FCSE plugin plus a data layer,
  unfinished: the cape buffalo charges the player and its gore knocks them down (C++/CMake)
- `mods/difficulty-overhaul/` - realistic damage and AI shooting, scaled by the game's difficulty;
  an FCSE plugin: damage by body part, the AI's hit model, fire discipline, intuition and grass;
  plus merc brain fragments `build-layer.ps1` makes from the installed game, which JackAll merges at
  install: launchers held for a player in a vehicle; design log in `docs/docs/design/difficulty.md`
  (C++/CMake)
- `mods/map-overhaul/` - a realistic map in place of the drawn one, and no GPS; world 1 drawn as a
  military topographic sheet from the world's own data and put on the game's map, plus an FCSE plugin
  that keeps the GPS out of the hand and off the dashboard (Python, C++/CMake)
- `mods/tales-of-africa/` - a missions mod, working title: visual storytelling and small missions or
  riddles for a weapon or diamonds; data only so far, a note, photo and cassette read in Inventory
  Overhaul's notebook
- `tools/JackAll/` - JackAll, a mod manager for FC2 (C#)
- `tools/vortex-farcry2/` - FC2 support extension for Vortex (TypeScript)
- `tools/nexus-inbox/` - a bookmarklet that captures Nexus comments, bug reports and PMs from the open
  page; `/nexus-inbox` files the new ones as GitHub issues (JS)

- `tools/fc2drive/` - drives the running game from outside to test mods: scripted runs through DevTools'
  remote control, screenshots, NVIDIA clips, recordings of play (Python)
- `tools/SoundBench/` - drives a car and plays its engine banks as the game does, without the game;
  for hearing vehicle sound work before a deploy, and editing its banks' curves (C#)
- `tools/BlenderFC2/` - a Blender add-on that edits `.fc2model` packs; holds no format code of its
  own, only what a scene is allowed to be (Python)
- `tools/asset-tags/` - every retail model, texture, sound and shader tagged by what it is and where it
  belongs; `/assets` lists what fits a scene (Python)

Weapon replacements are data + art, not code. Each publishes only a `layer/` you install with
`jackall-cli mod build` and a `README.md`; the build machinery - scripts, donor packs, reference
renders, working notes - is gitignored and stays local, so the method lives in the docs instead. One
that ships to Nexus adds a `CHANGELOG.md`, a `README-release.md` and `assets/nexus-*`, and goes out
through its own `<mod>-release.yml` - see `RELEASING.md`.

- `mods/vss-vintorez/` - replaces the SP Dart Rifle. Complete and confirmed in game: mesh, LOD tiers,
  textures, a worn appearance, pickups, icons, name and jam/break behaviour
- `tools/misc/weapon-swap/` - two scripts used while building it: `extract_fragment.sh` pulls one
  `.fcb` fragment, `fix_bboxes.ps1` refits an archetype's baked boxes to a new mesh

The method is split in two: `docs/docs/modding/replacing-a-weapon.md` for the mesh and archetypes,
`docs/docs/modding/texturing-a-weapon.md` for materials and textures.

A project with its own `CLAUDE.md` (UFCP, DevTools, Legacy Plugin, Difficulty Overhaul, FCSE,
JackAll, the Vortex extension, the docs site) adds instructions that apply while working in it.

## Legacy mods

`legacy/<mod>/` documents how an existing community mod works, feature by feature, so a feature can
be picked into a new mod alone. Metadata only; the mod's archive stays in `tmp/mods/`. To analyze a
mod or pick from one, invoke `/legacy-mod` (`.claude/skills/legacy-mod/SKILL.md`); the format is
`legacy/README.md`, the tooling `jackall-cli legacy`.

## Reverse engineering

Driven live through the GhidraMCP bridge (`.mcp.json`, server `ghidra`) against the Ghidra project.

- `reverse/ghidra/*.gzf` - checkpoint exports; re-import into a fresh Ghidra project to resume
- `tools/fc2re/` - Ghidra scripts that type the symbolized `FarCry2_server` and dump the component
  registry JackAll's entity schema is built from

## Reference material

- `scripts/` - tooling shared across projects: `changelog.ps1` and `nexus-page.ps1` (mod page text)
  behind the release workflows, `verify_patterns.py` for every FCSE plugin's byte patterns,
  `entity_fragments.py` for mods that generate archetype fragments
- `tools/misc/` - format samples, hash lists, text snippets, sbao and modpatcher material
- `tools/third-party/` - third-party tools, editors, and prototype builds (FC2/FC3/Avatar)
- `tmp/` - proprietary game files, for testing ideas against

## Docs site

The whole Docusaurus project is in `docs/`; its content subfolder is `docs/docs/`. JackAll's own
docs - overview, tutorials, reference - are a second docs instance in `docs/jackall/` (route
`/jackall`), with screenshots in `docs/static/img/jackall/` made by `tools/JackAll/screenshots/`. Run it with
`cd docs && npm install && npm start`. `npm run build` is strict and fails on broken links or
anchors - treat that as a real signal. Sidebar nav is generated from the folder structure
(`_category_.json` per top-level folder). The rules for writing a page are in `docs/CLAUDE.md`.

## CI

`.github/workflows/` - `docs.yml` deploys the site to GitHub Pages on push to `main`; each project
has a `<name>-release.yml`, which runs its tests and checks before it publishes, and
`nexus-pages.yml` sets mod page text without a release. Cutting a release is `RELEASING.md`.
