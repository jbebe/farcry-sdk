---
title: Todos
description: Open tasks across the repository, docs, JackAll, and mod work.
---

# Todos

A running task list across the whole project. For the reasoning behind the JackAll entries — what's
already covered by parsers and editors, what's missing, and which direction needs the most
implementation — see the [Tooling roadmap](/todos/roadmap).

## Repository

*nothing*

## Docs

- [ ] Improve JackAll docs

## Reverse

- [ ] Reverse Far Cry 2 Xbox360 prototypes with XEX decompiler
- [ ] Reverse other similar Ubisoft titles:
  - https://hiddenpalace.org/Far_Cry_4_(Oct_25,_2014_prototype)
  - https://hiddenpalace.org/Tom_Clancy%27s_Splinter_Cell:_Chaos_Theory_(Jan_18,_2005_Multiplayer_prototype)
  - https://hiddenpalace.org/Tom_Clancy%27s_Rainbow_Six:_Lockdown_(Jan_16,_2006_demo)
  - https://hiddenpalace.org/Assassin%27s_Creed_(Feb_15,_2008_prototype)

## Tools/JackAll

Prioritized in the [Tooling roadmap](/todos/roadmap), which reorders the Domino entry below: the
visual revamp is cosmetic next to wiring up the existing `UserGraphWriter`, and swapping the graph
package won't fix the real problem (20,228 wire crossings after layout).

- [ ] Review the Domino viewer because it needs a lot of improvements
  - Review code
  - Revamp the visual interface, find a good graph wpf package
- [ ] **Nothing writes into a `.fc2model`.** `fc2model` has `export`/`extract`/`inspect` only, so
      retexturing a weapon means hand-editing the pack's JSON — which is what
      [texturing a replaced weapon](/farcry-sdk/docs/modding/texturing-a-weapon) currently
      prescribes. `Fc2ModelBundle` and `MaterialDocument` already model everything a
      `fc2model set-material` / `set-texture` pair would need. Worth building when a third mod wants
      it; the mesh half stays a per-mod script, because appending a material and skipping `SCOPE_HI`
      is policy rather than a generic operation.

## Tools/BlenderFC2

The art half of a custom weapon is
[done end to end](/farcry-sdk/docs/modding/adding-a-weapon#geometry-materials-and-textures-one-file-one-plugin) —
what is left is what a modeler cannot do rather than what is broken.

- [ ] **Add a node or an LOD from the scene.** Adding a *part* is done — **Add as New Part** appends
  one and every shipped mesh takes it with its own parts unchanged (3,133 of 3,133). A node and a
  whole LOD tier still have no scene-to-document path, and neither does removing a part. An added
  part also lives only at the LOD it was added to, so it vanishes at distance.
- [ ] **`.hkx` collision.** Not parsed at all, so a reshaped weapon keeps the donor's collision shape.
  This is the last format in a weapon's file set with nothing behind it.
- [ ] **A material a mesh embeds cannot be edited.** Four material names of 7,496, across three
  meshes, none of them weapons — they travel inside the mesh document as an opaque chunk, so an
  editor gets the name and no shader graph.
- [ ] **Nothing checks that the result looks right.** Every gate is numeric, and a part in the wrong
  place that still lands inside the model bounds passes all of them. `tests/render_preview.py` exists
  to be looked at; nothing compares renders.

## Tools/FCSE

- [ ] **Known bug: pressing Enter in a text field does nothing.** On the stock Options → Network
  page that commits the value and refreshes the page. The `ActionExecuterEditbox` on the element only
  *raises* an action on the `enter` trigger — committing is the page's response to it, not something
  the widget does — and FCSE's page never sees that action: the inherited handler
  (`CFCXBaseOptionPage::OnActionSignal`, `0x1087f1f0`) early-returns unless the dirty flag at
  `page+0x1B8` is set, and FCSE clears that flag every frame to suppress the "unsaved changes"
  prompt. The two needs conflict, so the fix is FCSE registering its own `IMagmaActionListener`
  rather than relying on the inherited one — and probably narrowing the dirty-flag clear at the same
  time. Values still save; only the Enter gesture is missing.
- [ ] **Known bug: no mouse cursor on the Mod Configuration page.**
- [ ] Two faults seen in `fcse.log` and not yet chased: `FCSE.exe+0x14ABB` (in FCSE's own code) and
  a recurring `Dunia.dll+0xAD4095` in magma's draw-collection walk.

## Tools/"dll plugins"

- [ ] Lua script support for plugins so that simple changes don't have to be compiled

## Tools/vortex-farcry2

## Mod

- [x] Create our first mod because that was the original plan — `mods/doom-super-shotgun`, then
      `mods/vss-vintorez`
- [x] VSS: textures. Done and confirmed in game; the method is
      [texturing a replaced weapon](/farcry-sdk/docs/modding/texturing-a-weapon)
- [ ] VSS: split the body into steel and stock materials, so the stock stops sharing the steel's
      specular response. Needs the transplant re-run, not new textures
- [ ] Point the doom mod's local build scripts at the pack — `retexture.py`, `unify_materials.py`
      and `verify.py` still import the Python `fc2fmt`, whose format code moved into JackAll, so
      they no longer run as written
- [x] VSS: the pickup archetypes, so the weapon on the ground is complete at close range. Rebuilt
      from the Dragunov's pickups; `archWeapon` has to be repointed with them or the pickup hands
      over a Dragunov
- [x] VSS: the LOD tiers. LOD1/LOD2 were budgeted per cluster instead of per part; LOD3/LOD4 were
      forty slivers rather than a gun, and are now floored at LOD2's budget
- [x] Move `FX_FIRE` to the VSS's muzzle — done through the archetype's baked skeleton, which is
      per-archetype, rather than the rig file, which every world's Dart Rifle still names
- [x] VSS: lethality. Measured rather than done — the weapon already kills (`selFireStrategy` is
      `Bullet`, both hit-location severities are `Kill`, and nothing spawns a dart). Its damage
      number stays the Dart Rifle's on purpose; a suppressed stealth weapon is what it is for
- [x] VSS: HUD and bazaar icons. Both redrawn; they are bound by name in `gamemodesconfig.xml`, so
      replacing the texture is the whole job
- [x] Find where the weapon-bazaar name comes from; it is not `sDisplayName`. It is
      `nameOasis="WEAPONBAZAAR_*_NAME"` in `engine\gamemodes\gamemodesconfig.xml`, resolved against
      `languages\<language>\oasisstrings.rml`. Ten strings name one weapon across five sections
- [x] VSS: jamming and breaking. Both confirmed in game, then set to the Dragunov's values. Jamming
      is `fJamProbabilityPerReload` in `ReliabilityLevelsData` on the **weapon** archetype, per
      reload and zero at full condition; breaking is `iClipsForSelfDestruct` on `WeaponProperties`
- [ ] What `nForcedFailure*` actually governs. Raising it from 0 to 20 produced no failures at all,
      and Mike's rusty Dragunov carries the same values as an ordinary one
- [ ] VSS: the weapon never *looks* degraded — a consequence of the albedo recipe pinning every
      `Clean`/`Broken` pair. Needs a second control map, and a weapon owns only two texture paths
- [ ] VSS: the other ten languages still say "Dart Rifle"; only English was renamed
- [ ] VSS: `pickups.Weapons.DartRifle_new.Multi.Dropped`, skipped on the single-player rule, so a
      dropped VSS in multiplayer still has no barrel
