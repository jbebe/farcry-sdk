# Legacy mods

How existing community mods work, feature by feature, so a feature can be understood, compared and
picked into a new mod without carrying the rest of the mod with it.

This folder is metadata only. It never holds a mod's files or enough of their content to rebuild the
mod: every pick reads the original archive from `tmp/mods/` (gitignored) and the installed game. A
texture, sound or model a feature needs is named by its path inside that archive, never copied here.

The procedure lives in `.claude/skills/legacy-mod/SKILL.md`; the tooling is `jackall-cli legacy`
(`tools/JackAll`).

## Layout

```
legacy/<mod>/
  mod.md              provenance, how the mod ships, its bundles, its published feature list
  features/<id>.md    one page per component, bundle or noise rule
tmp/legacy/<mod>/     the analysis - source, imported layer, changes.jsonl - rebuilt, never committed
```

## Changes

`jackall-cli legacy analyze` lists every difference between the mod and the base game as a
*change*, at the finest grain the format allows. Each has an address:

| Address | Change |
|---|---|
| `downloadcontent/dlc1/generated/entitylibrary.fcb/vehicle/land/dlc_vehicle1_dlc1.xml#Entity/Components/CVehicle/FOV/fFOVAngle` | one value in one archetype |
| `levels/w1_b_2/generated/worldsectors/worldsector3859.data.fcb/_layout.xml#layer[missions\weaponbazaar\primary\goldak47]` | one mission layer a sector gains |
| `engine/settings/defaultrenderconfig.xml#@MaxFps` | one attribute of a whole XML file |
| `domino/user/fasttravel/fasttravel.fasttravel.lua@L1314` | one hunk of a script, at its base-game line |
| `_hash/01257145.lua` | a whole file the base game does not have |
| `install/bin/dunia.dll@0x488f3` | one byte run in a game binary, by RVA |
| `install/scubrahspatch.lua` | a file installed beside the game |

Inside an XML path an `.fcb` value is named by its name and an object by its type; anything else by
tag and key attribute (`Quality[UltraHigh]`). Siblings sharing a label carry `[i]`, their base-game
index, or `[+i]` for an addition. Float values the mod's editor merely rounded are not changes, and
neither is a nested RML value re-encoded with the same content.

A change to an archetype that a later library declares again carries `shadowedBy`: it edits a copy
the game never reads. It still belongs to the feature that made it, and that page says it is dead.

## Feature pages

A page is YAML frontmatter, which `legacy check` and `legacy pick` read, and a body for people.

```yaml
---
title: Increased swimming speed
kind: component            # component | shared | bundle | noise
bundle: balancing          # the bundle page this belongs to, if any
claims:                    # lines of mod.md's published feature list this page covers, verbatim
  - Increased swimming speed
status: located            # located | partial | no-artifact | unresolved
systems: [player]
match:                     # address globs: ** spans anything, * stays inside one /-segment, {a,b}
  - "**/entitylibrary*.fcb/player/**#**/fSwimSpeed*"
exclude: []                # globs carved out of match
requires: []               # pages that must be picked with this one
existing: mods/UFCP — src/fixes/jackal_tapes.cpp   # where this repo already does it
verified: diff             # diff | re | in-game
---
```

- **component** — the smallest change worth picking alone. Its rules claim changes; one whose
  changes all sit inside a shared unit claims none and `requires` the shared page instead.
- **shared** — an indivisible unit several components need: a script hunk carrying many fixes, a
  file of globals, a manager script. Its components `requires` it, so a pick brings it along.
- **bundle** — a headline feature naming its components in `includes`; claims no changes itself.
- **noise** — changes that do nothing in game: a re-encoding, an editor leftover, a value written
  back unchanged in meaning. The body says why. Never picked unless named.
- **status** — `located`: every change it needs is found; `partial`: some of the claim has no change
  behind it; `no-artifact`: the claim has nothing in the mod (an external tool, a statement);
  `unresolved`: the claim is real but its changes are not found yet.
- **verified** — `diff`: read from the changes; `re`: the mechanism traced in the engine; `in-game`:
  seen working.

The body says what the feature does in game, how - which units change, what values, what a script
now does - what it depends on, and what is still uncertain. Values and identifiers are quoted; file
contents are described, never pasted.

## Systems

`systems:` uses this list, so features of different mods can be found together:

`player` · `weapons` · `economy` · `ai` · `vehicles` · `patrols` · `missions` · `buddies` · `world`
· `environment` · `graphics` · `audio` · `ui` · `input` · `engine` · `save`

## Done means

`jackall-cli legacy check --mod legacy/<mod> --work tmp/legacy/<mod>` reports clean: every change is
claimed by exactly one page, every published claim is covered by a page, and every page is
well-formed.
