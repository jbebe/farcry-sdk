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

*nothing*

## Reverse

- [ ] Reverse Far Cry 2 Xbox360 prototypes with XEX decompiler

## Tools/JackAll

*nothing*

## Tools/BlenderFC2

- [ ] **Nothing writes into a `.fc2model`.** `fc2model` has `export`/`extract`/`inspect` only, so
  retexturing a weapon means hand-editing the pack's JSON — which is what
  [texturing a replaced weapon](/farcry-sdk/docs/modding/texturing-a-weapon) prescribes.
  `Fc2ModelBundle` and `MaterialDocument` already model everything a `fc2model set-material` /
  `set-texture` pair would need. Worth building when a third mod wants it; the mesh half stays a
  per-mod script, because appending a material and skipping `SCOPE_HI` is policy rather than a
  generic operation.
- [ ] **Add a node or an LOD from the scene.** Adding a *part* is done — **Add as New Part** appends
  one and every shipped mesh takes it with its own parts unchanged (3,133 of 3,133). A node and a
  whole LOD tier still have no scene-to-document path, and neither does removing a part. An added
  part also lives only at the LOD it was added to, so it vanishes at distance.
- [ ] **`.hkx` collision.** Not parsed at all, so a reshaped weapon keeps the donor's collision shape.
  This is the last format in a weapon's file set with nothing behind it.
- [ ] **A material a mesh embeds cannot be edited.** Four material names of 7,496, across three
  meshes, none of them weapons — they travel inside the mesh document as an opaque chunk, so an
  editor gets the name and no shader graph.

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
- [ ] **Hook chaining, for more than one rendering plugin.** FCSE gives an address to one plugin, but certain parts of Dunia must share logic extensibility to plugins.

## Tools/vortex-farcry2

*nothing*

## Mods/vss-vintorez

- [ ] VSS: split the body into steel and stock materials, so the stock stops sharing the steel's
      specular response. Needs the transplant re-run, not new textures
- [ ] Whether the `Weapon` shader samples a normal map at all. No `NormalTexture1` slot appears on any
      of the nine `Weapon` materials across three weapons, even though weapons seemingly have a normal map.
      Disassemble the template out of `shadersobj.fat`'s `obj10` tree, which keeps its reflection data

## Mods/DevTools

- [ ] The correct scale for `Game:SetHealth` — 100 and 25 both kill the player

## Mods/vehicle-overhaul

- [ ] Third-person view: the driver is the headless first-person body. Hide it, or seat a third-person
      stand-in with NPC driving animations
- [ ] Off-road physics on AI cars too: only the player's car is tuned, so AI drivers still get retail
      grip, brakes and climb assist
- [ ] An engine with real gears, its RPM read from the drivetrain, and a shift sound; the sound's
      options are in the
      [design log](/farcry-sdk/docs/design/realistic-sound#7-vehicle-engines--data-plugin-for-a-real-rpm)
- [ ] Shooting out of the windows, from a legacy mod
- [ ] Wrecks revived as drivable vehicles (bus, UAZ, a car); needed for 1.0

## Mods/sound-overhaul

- [ ] 

## Mod ideas

- [ ] Binoculars with a distance meter
- [ ] Flashlight
- [ ] **Downed enemies soak too much lead; change it in some way.** A shot that takes a soldier to
  49 health or below can down him instead of killing him. His health is then set to 49, even when
  the shot would have killed him. For 0.4 s after that, hits do nothing
  (`fHealthFailureCantDieDuration`). After that, torso hits do half damage and limb hits a fifth
  (`fHealthFailureTorsoHitModifier` 0.5, `fHealthFailureLimbsHitModifier` 0.2, on
  `CFCXCountersComponentAI` of all 135 soldier archetypes). Traced in the server build
  (`CFCXCountersComponentAI::DamageHealth`); not cross-checked in `Dunia.dll`
- [ ] Better explosion effects
- [ ] Better smoke during a bushfire and from an exploded car
- [ ] Tracer round effect. US tracers are red/orange, Soviet ones green
- [ ] Reuse the taxi driver as a quest giver
- [ ] **Revamp the map to be more realistic.** Generate a real map from the cartoonish one with AI.
  Make the right hand point at where we are, or point at it with the GPS antenna
- [ ] Full-body first person: look down and see your legs
- [ ] **Some kind of platformer on a computer.** Opening the computer brings up a Magma UI that
  lets you play a platformer game, with a high score and such
- [ ] **Radio(s).** A host created with ElevenLabs, for example
