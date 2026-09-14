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
      [texturing a replaced weapon](/farcry-sdk/docs/modding/texturing-a-weapon) prescribes.
      `Fc2ModelBundle` and `MaterialDocument` already model everything a `fc2model set-material` /
      `set-texture` pair would need. Worth building when a third mod wants it; the mesh half stays a
      per-mod script, because appending a material and skipping `SCOPE_HI` is policy rather than a
      generic operation.
- [ ] **A build picks a container's splitter off a *string*, and one caller can still synthesize the
      wrong one.** `PatchBuilder.RecoveredContainerPath` reads the container's name off any
      contributing fragment's staged path; a `mods\_hash\<hex>.game.xml\<mission>` override resolves
      as a valid fragment but `PathOf` nulls every `_hash\` path, so the fallback names it
      `_hash\<hex>.fcb` and the world descriptor is handed to the `.fcb` splitter, which fails with
      "missing 'FCbn' signature". `ContainerFormats.For`'s doc claims `.fcb` is the only format
      staged hash-addressed, which is false now that world descriptors split. The container path is
      known at index time in all three producers and thrown away; carrying it on `ModPathTarget`
      would close this and collapse the four places that re-derive it (`GameVfs`, `PatchBuilder`,
      `MainViewModel.Mods`, `For` itself).
- [ ] **Finer environment fragments, so sky and terrain mods can coexist.**
      `world1.game.xml\_environment.xml` replaces the whole `<Environment>` block, so the sky mod's
      moon-size edit carries retail shadow radius and view distance and collides with a shadow mod.
      Every preset edit, whether lighting, fog or bloom, collides with every other, because the whole
      preset library is one entity fragment. The engine already gives each preset a name and a GUID.

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
- [ ] **Hook chaining, for more than one rendering plugin.** FCSE gives an address to one plugin, and
  Sky Overhaul holds the Direct3D slots for `EndScene`, `DrawIndexedPrimitive` and both
  shader-constant setters, so a terrain shader plugin wanting them would be refused.

## Tools/"dll plugins"

- [ ] Lua script support for plugins so that simple changes don't have to be compiled
- [ ] How the permutation key in a `shadersobj` index table is derived from a shader's `#define`s.
      The no-option permutation keys on the CRC32 of the shader's name, which is confirmed for 30
      shaders; option-bearing ones can be enumerated but not addressed by name

## Tools/vortex-farcry2

## Mod

- [x] Create a first mod — `mods/vss-vintorez`
- [x] VSS: textures, confirmed in game; the method is
      [texturing a replaced weapon](/farcry-sdk/docs/modding/texturing-a-weapon)
- [ ] VSS: split the body into steel and stock materials, so the stock stops sharing the steel's
      specular response. Needs the transplant re-run, not new textures
- [x] VSS: the pickup archetypes, so the weapon on the ground is complete at close range. Rebuilt
      from the Dragunov's pickups; `archWeapon` has to be repointed with them or the pickup hands
      over a Dragunov
- [x] VSS: the LOD tiers. LOD1/LOD2 are budgeted per part rather than per cluster; LOD3/LOD4 are
      floored at LOD2's budget, since below it they come out as forty slivers rather than a gun
- [x] Move `FX_FIRE` to the VSS's muzzle — through the archetype's baked skeleton, which is
      per-archetype, rather than the rig file, which every world's Dart Rifle still names
- [x] VSS: lethality. The weapon kills (`selFireStrategy` is `Bullet`, both hit-location severities
      are `Kill`, and nothing spawns a dart). Its damage
      number stays the Dart Rifle's on purpose; a suppressed stealth weapon is what it is for
- [x] VSS: HUD and bazaar icons. Both redrawn; they are bound by name in `gamemodesconfig.xml`, so
      replacing the texture is the whole job
- [x] Find where the weapon-bazaar name comes from; it is not `sDisplayName`. It is
      `nameOasis="WEAPONBAZAAR_*_NAME"` in `engine\gamemodes\gamemodesconfig.xml`, resolved against
      `languages\<language>\oasisstrings.rml`. Ten strings name one weapon across five sections
- [x] VSS: jamming and breaking. Both confirmed in game and set to the Dragunov's values. Jamming
      is `fJamProbabilityPerReload` in `ReliabilityLevelsData` on the **weapon** archetype, per
      reload and zero at full condition; breaking is `iClipsForSelfDestruct` on `WeaponProperties`
- [ ] What `nForcedFailure*` actually governs. Raising it from 0 to 20 produces no failures at all,
      and Mike's rusty Dragunov carries the same values as an ordinary one
- [x] VSS: a degraded look, through a hand-painted rust map on a second control map. **A texture at
      an invented path loads from `patch.dat` with no hashlist and no `depload` entry**, proven with
      a magenta canary, so no weapon mod is limited to the texture paths its donor owns
- [ ] Whether the `Weapon` shader samples a normal map at all. No `NormalTexture1` slot appears on any
      of the nine `Weapon` materials across three weapons, so a texture path is not what is missing.
      Disassemble the template out of `shadersobj.fat`'s `obj10` tree, which keeps its reflection data
- [x] VSS: the name in all eleven shipped languages, not only English. One
      `oasisstrings.fragment.xml` per language; the name is a proper noun everywhere, so only the
      grammar around it moves — Polish and Russian pick up a preposition the bare name cannot inflect
      into
- [x] VSS: downsample the textures to the sizes retail uses — 512² base and 1024² `_mip0`. The four
      state files weigh exactly what the Dart Rifle's do, 1.33 MiB against 5.33 MiB. Confirmed in
      game: at retail's tier the weapon reads as well as the guns Ubisoft shipped
- [x] Does retail Far Cry 2 have a developer console? **Yes** — `~`/`` ` `` on a vanilla install,
      and a leading `#` runs arbitrary Lua, bypassing the `ConsoleDeveloperOnly` gate. Documented in
      [the developer console](/docs/engine-internals/developer-console)
- [ ] Raise `console+0x68` (developer mode) from data rather than a patch. That would expose the
      developer-only commands to `?` and to lookup, and make `console_dump_elements` run — its
      `ConsoleElementsDump.txt` would be an authoritative engine-generated command table
- [ ] `SwitchCamera` argument shape, and whether `Cameras.Camera.Editor` gives the retail game a
      free-fly camera. `CCameraFreeComponent`/`CCameraGhostComponent` both have live factories
- [ ] The correct scale for `Game:SetHealth` — 100 and 25 both kill the player
- [ ] Whether `-exec <file>` and the `ConsoleCommands` config section actually work in retail

## Mods/sky-overhaul

What the sky needs to be a complete package, in order: fog first, since it is the seam; then storms
and clouds together; then the light-shaft mask and the glare; then reflections. Out of scope, for
other mods: the daytime terrain lighting presets and the engine's own sun shadows.

- [ ] **Fog, as part of the air.** The sky owns the fog's colour at every hour; fog distances stay
      with the presets. The fog retint in `src/engine/fog_tint.cpp` gives the land's distant fog the
      sky's horizon hue at the engine's brightness, shaded darker, and leaves it the engine's after
      dark, where its processor-side copy of the sky has no light. Doing it through fog presets would
      collide with any lighting mod, since every preset lives in one JackAll fragment
- [ ] **Weather.** A storm now thickens our clouds and spreads an opaque cirrus sheet, and the world
      data points the storm's fog at the clear preset. Still open: `SetScriptedStormFactorOverride`
      never reaches the storm factor the sky reads, and rain has not appeared without DevTools
- [x] **Light shafts follow our clouds.** The god-ray mask pass's cloud draw is replaced by our
      clouds' cover, blended as the engine's own. Retail shades no terrain with clouds, so there are no
      cloud shadows to replace
- [x] **The sun glare sees our clouds.** Its occlusion patch is drawn through a shader that discards
      pixels as far as the clouds cover them
- [x] **Water reflections.** Our clouds are drawn only in the main sky pass; checked in game, the
      reflections look right as they are
- [x] **Ambient occlusion: dropped.** The steps and lone texels in the engine's depth were 8-bit
      storage, and half floats removed them. Ground-truth occlusion then ran clean in game with grass
      and leaves masked out, and was set aside for other work. What was learned is in
      [presenting a frame](/docs/engine-internals/presentation-and-input#ambient-occlusion-on-this-frame)
- [x] **Cloud shadows.** The ground marches toward the sun through our clouds' density, at half
      resolution from the engine's linear depth. Confirmed in game, grass included
- [x] **The colour grade.** The final pass's saturation, powers and contrast drawn with our values.
      Confirmed in game. Open: how the presets' `fColorRemap*` become the shader's powers
- [ ] **Sun colour (optional).** The near-white dawn light-shaft tint and the flare colour
- [ ] **Publish the sky's light (optional).** The sun's colour at the ground and the sky's ambient
      light, for a lighting mod to match
