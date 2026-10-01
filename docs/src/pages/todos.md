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

- [ ] A mod that overhauls vehicles. Not started; the engine sound's options are in the
      [design log](/farcry-sdk/docs/design/realistic-sound#7-vehicle-engines--data-plugin-for-a-real-rpm)

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

## Sound

The realistic-sound goal: every improvement found so far, whether from the original brief or
uncovered in the engine. The reasoning and the verdict per target are in the
[design log](/farcry-sdk/docs/design/realistic-sound). How the engine works is on
[audio runtime](/farcry-sdk/docs/engine-internals/audio-runtime).

### What you should hear, step by step

The direction (2026-09-26): data and small hooks on the engine's own systems, no sound manager of our
own. A step is done when it is heard in game.

- [x] **1. Distant enemy fire turns into a crack** (2026-09-29, heard on the M1903). An NPC's echo is a
  near echo to 55 m and a crack with its echo from 40 m out to 900 m, and every NPC shot of a gun with an
  echo fades out by 80 m, so from afar the crack is all there is.
- [x] **2. Voices fall off as fast as real speech** (2026-09-27, heard). The dialog curves in
  `2fffffff.spk` fall to −15 dB at 20 m, −32 dB at 40 m and silence at 50 m.
- [x] **3. Vehicles have an engine you hear** — dropped (2026-09-30). The Datsun got a rougher, quieter
  idle; its engine rebuilds were abandoned and the retail engine stays.
- [x] **4. Every interior's reverb matches its size** (2026-09-30, heard). Sound Overhaul's
  `rooms.cpp` gives every building a reverb by structure type and size as it loads: small room
  0.4 s, medium 0.8 s, large 0.9 s, hall 1.7 s, hangar 3.1 s, metal box 1.0 s, on presets retail
  authored but never used.
- [x] **5. Shooting in a small room sounds like it** (2026-09-30, heard). The room presets' early
  reflections are raised and brought forward in `7fffffff.bao`: +400/+600 mB at 3–4 ms in small rooms
  and metal boxes, softer and later up to the hangar. Measured offline through DSOAL, the slap sits
  9–12 dB under the shot in small rooms; the user kept it light.
- [x] **Every weapon has a new shot** (2026-09-29): every single-player gun, the grenade and the
  rocket, with a big and a small echo, full-auto per round. The player's shots are heard in game; NPC
  shots and the rocket's impact are not yet.
- [x] **The authored reverb switches by place** (DSOAL and the `PlaySoundReverb` body, 2026-09-25).
- [x] **Your own shots take the room** (+1,000 mB send on 2D voices, 2026-09-26).
- [x] **The Makarov's first-person shot is dry, with an outdoor echo** (2026-09-26).
- [x] **The G3KA4 has new first- and third-person shots, per-round full-auto and an echo**
  (2026-09-26).
- [x] **NPC shots play their weapon's echo**, and only a burst's last echo rings (2026-09-26).
- [x] **The M1903 has an echo** (2026-09-26).

### Checks before building anything

- [ ] **Walls with EAX live.** With DSOAL, DARE's EAX occlusion and obstruction now act on top of the
  software low-pass. Listen at doorways and inside buildings for sound muffled twice.

### Gunshots

- [ ] **No crack from subsonic or silenced weapons** (silenced Makarov, Dart Rifle). The fly-by sound is
  per player, not per weapon, so a calibre-aware crack needs a plugin.
- [ ] **Close layers per weapon.** Add transient, mechanical (bolt, spring) and body layers as children
  of the first- and third-person multi-events (type `12`), in new banks with `depload` entries.
- [ ] **Malfunction sounds on every weapon.** `sndMalfunction*` is set only on the Carl Gustaf.
- [ ] **Weapon wear in the shot sound.** 41 switch resources key on the weapon-status switch
  (`0x004402A7`), but `WeaponStatusSwitchValues` is empty on all 101 weapons. Check whether wear is
  audible, and wire it if not.
- [ ] **Single-player hit confirmation (optional).** `sndHitPlayerSound`/`…HeadSound` are set only on
  multiplayer weapons.

### Environment tail

- [ ] **A weapon's echo by environment.** Each weapon has one echo today. Choosing it by place needs a
  hook on its one play call: it plays with no emitter, so no switch can choose **(inferred)**.
- [ ] **Release Sound Overhaul 1.0.0.** The diagnostic logs are removed and the release is set up
  (`CHANGELOG.md`, `sound-overhaul-release.yml`, the Nexus texts); it still needs pushing, a run of the
  workflow, and a Nexus page with its IDs in the workflow.
- [ ] **Retune the reverb presets** in `common/soundbinary/7fffffff.bao` (63 presets, EAX form).
- [ ] **Building echo lengths.** `fEchoLength` is 0 on 620 of 623 placed buildings; set it per
  building class.
- [ ] **Retune region echo lengths and reverbs** per biome and intensity in `common/soundregions.xml`
  (`fEchoLenght` 1.2–6.1 s today).
- [ ] **Terrain-aware slapback** (plugin). Ray-cast from the shot to nearby hills and cliffs to set the
  delay and direction of the echo, instead of a per-biome length.

### Distance and occlusion

- [ ] **Air absorption** (plugin). Drive DARE's per-voice Butterworth low-pass by distance for
  positioned types (weapons, explosions, barks, vehicles, animals), so a merc yelling 80 m away sounds
  far, not just quiet. Hook the per-type occlusion callback `FUN_10621880`, which writes each sound's
  obstruction, and mark playing sounds dirty as the listener moves: DARE caches occlusion per sound
  object. The low-pass is confirmed working on PC (underwater listen, 2026-09-25).
- [ ] **Soften the obstruction curve** (data). `7fffffff.bao` maps obstruction onto 20–3,200 Hz, so
  any obstruction means a cutoff under 3.2 kHz. Raise the maximum (`+0xB4`) for gentle distance
  dulling, and retune underwater and building filters to match.
- [ ] **Terrain and cover occlusion outdoors** (plugin). Two outdoor points are never occluded today; a
  ray test from listener to source can feed the same low-pass.
- [x] **Your own pickups are never muffled** (2026-10-01, heard). The safehouse ammo, explosive and fuel
  piles and some mission pickups played their first-person grab as `Effect_3D` (type 11), which a room's
  occlusion muffles; Sound Overhaul's fragments set it to `Foley_Player` (15), as on every other pickup.
- [x] **Set building occlusion filters** (2026-10-01, heard). `fOcclusionFilter` was 0 on most of the 623
  buildings. Sound Overhaul's `rooms.cpp` raises it by material as each building loads, never below
  retail's: 0.05 concrete, brick and mud, 0.03 generic, down to 0.001 for a bus. Open doors and windows
  (filter 0) reach 2 m + 1.5 × the side of their area, about 4 m for a window and at most 8 m, instead of
  retail's 1–2 m, through a hook on `AddHole` (Dunia `0x10626a30`), so a house with its windows open is
  barely muffled inside.
- [ ] **Walls that lower gunfire volume** (plugin or data). No path makes a gunshot quieter through a
  wall today; decide whether one should.
- [ ] **Material-aware occlusion** (plugin + data). DARE can band-pass by occlusion material, but the
  project declares no materials and the game's callback never passes any.
- [ ] **Widen the occlusion mix preset.** `Compatible.VolumeLinesToLowerInsideForOcclusion` touches
  only ambience types 0, 23 and 24; decide which other types it should muffle when the listener is
  indoors.
- [ ] **Retune rolloff curves** in `common/soundbinary/2fffffff.spk` (all 96 curves, shared). Make
  gunfire carry realistically far, and give events their own curves where sharing is wrong.
- [ ] **Raise the voice cap.** `NB_AUDIBLE_VOICES=64` in `DARE.INI`; distant gunfire loses out in big
  fights. The −48 dB distance cull is code.
- [ ] **`occmul_pc` = 1.0 against 50.0 on consoles.** It multiplies every sound's obstruction, and 50
  saturates any occlusion to full. Decide whether PC should be harsher than 1.0.
- [ ] **HRTF for headphones.** OpenAL Soft behind DSOAL can render DS3D voices with HRTF, but as it is
  now it feels awful; it needs a major reconfiguration before it is ever enabled.

### Near misses and ricochets

- [ ] **Near-impact wizz.** Fill `sndPassByWizz` (empty on all 91 weapons) so NPC rounds landing
  within 5 m get a whiz before the impact (`fPassByWizzTiming` 0.15 s). The field alone is not enough:
  that sequence plays at volume `0.0` → −96 dB (`0x100faea0`), so the constant needs patching to `1.0`
  at the same time, or close impacts go silent.
- [x] **Better fly-by sounds** — kept retail (2026-09-30). Compared against JSRS's `wizz` set by ear: the
  game's own regular (12) and near (7) whizzes and its impacts are good as they are.
- [ ] **Retune the fly-by geometry.** `fNormalRadius` 2.7, `fSmallerRadius` 0.6, `fAngle` 1.25,
  `fSoundDuration` 0.5, and `fMinImpactDistance` 5, which suppresses fly-bys for rounds landing near
  the player.
- [ ] **Ricochets** (data). Add whine variants to the rock and metal entries of the bullet-impact
  material switch; the random containers' weights set the chance.
- [ ] **Angle- or material-aware ricochets** (plugin), if the data version is not enough.

### Impacts and foley

- [ ] **Better impact clips per material.** `Weapon.Bullet` → a material switch with 36 entries, each
  a random container of 2–7 clips.
- [ ] **Bullets hitting the player make no impact sound** (code skips it). Add a body hit, by plugin.
- [ ] **Shell casings.** Better per-surface casing drops on the first-person shot layer (`0x004565A6`).
  The `Weapon.MetalShell*` impact records have no sound, so NPC casings are silent.
- [ ] **Footsteps and NPC foley.** Per-material footsteps exist; retune how far enemies' movement
  carries, so they can be heard approaching.

### Mix and dynamic range

- [ ] **A quiet world.** Lower the ambience types with a permanent preset, or with sample gain and
  rolloffs, so a gunshot is the loudest thing you hear. Check whether the empty `Exclusive.Normal`
  preset is the one applied by default.
- [x] **Revise the ambience** (2026-09-30). The distant dogs were one random-fx set, `0x004E4D2D`, used only
  by the savannah's daytime 40–120 m calls (`004b89d8.spk`); it is removed with its four clips, and the
  other animals are kept.
- [ ] **Duck music and ambience under gunfire** (plugin + data). A new preset in `soundmixings.xml`,
  applied through `CMixingManager::ApplyPreset` whenever shots are fired near the player.
- [ ] **Brief suppression on a close fly-by** (plugin + data): a short muffle or duck preset triggered
  by the fly-by.
- [ ] **Fill the empty presets.** Low, medium and critical health, low and medium stamina, and healing
  are wired to the player's state but do nothing.
- [ ] **Wire the unwired presets.** `mixIronsightPreset`, `mixRunningPreset` and `mixCrouchedPreset`
  are empty fields; `Compatible.Ironsight` and `Compatible.Running` exist unused.
- [ ] **Headroom and gain staging.** DARE has no compressor or limiter, so louder shots need headroom
  taken from every other type.
- [ ] **Music for calm moments only.** The music is good, but it should be off most of the time.
  `CMusicManager` reacts to shots, pass-bys, hits and explosions.
- [ ] **Sub-mixes.** All 25 sound types go to one line, `Master`; separate lines would allow group
  volumes.

### Tooling (JackAll)

- [ ] **Write new `.spk` events and banks**: leaves (type `1`) and multi-events (type `12`).
- [ ] **Author resource containers**: switches (kind `3`), random containers with weights (kind `4`),
  multilayers (kind `7`).
- [ ] **Rolloff curve editor** for `2fffffff.spk`.
- [ ] **Reverb preset editor** for `7fffffff.bao`, including new game-parameter declarations.
- [ ] **`depload` updates implicitly.** Creating or deleting a bank updates `depload` without asking,
  and an insert adds the entry to world1 and world2 too. An unlisted child bank plays silent; a
  top-level id loads its own bank without one (2026-09-26), which the danger box on the
  [`.spk` page](/farcry-sdk/docs/file-formats/spk) still contradicts.
- [ ] **The App's `.spk` import keeps the replacement's own length**, as `jackall-cli spk import`
  does; today it pads.
- [x] **Records in ascending id order.** `spk new` and the App's variations write records by id, and
  the checks note a sound bank that is not. Not what broke the Makarov's echo: bark banks play out of
  order, and sorting alone left the echo silent. The cause was event word `[14]`, now `positioned`.

### Open reverse-engineering questions

- [ ] **Emitter `GetPosition` slot in `Dunia.dll`** (`+8` in the server's interface): a prerequisite of
  the first prototype.
- [ ] **Whether DS3D's own distance rolloff is neutralised**, or doubles DARE's curves.
- [ ] **What `ApplyListenerFactor` returns** when the listener is inside a building and the source is
  not.
- [ ] **What marks a sound object's cached occlusion dirty**, besides its creation (vtable slot `+8`,
  `0x10a537d0`). The air-absorption plugin needs a way to refresh playing sounds.
- [ ] **The fourth DARE private effect.**
- [ ] **How `fAngle` is converted** in the fly-by (default 90, retail 1.25).
- [ ] **Event types `2`, `3`, `4` and `10`**: only partly traced.
- [ ] **Multilayer (kind `7`) and switch (kind `3`) layouts**, confirmed in code rather than only in data.
- [ ] **Whether a game parameter clamps at its declared range**, e.g. distances beyond 250 on
  `0x00440259`.
- [ ] **The `.srl` reader.** Confirm that the cell byte is `(intensity << 4) | region`.
- [ ] **Whether the weapon-status switch is answered at runtime.**
