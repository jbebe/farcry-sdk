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

## Sound

The realistic-sound goal: every improvement found so far, whether from the original brief or
uncovered in the engine. The reasoning and the verdict per target are in the
[design log](/farcry-sdk/docs/design/realistic-sound). How the engine works is on
[audio runtime](/farcry-sdk/docs/engine-internals/audio-runtime).

### Checks before building anything

- [ ] **Underwater listen.** Dive while an NPC fires. If gunfire turns dull rather than only quieter,
  DARE's software low-pass works on a modern PC and distance filtering by plugin is viable.
- [ ] **DSOAL experiment.** Put 32-bit `dsound.dll` + `dsoal-aldrv.dll` in `bin\` and A/B the
  authored reverb in a hangar, a normal building, jungle and desert. It decides the reverb route.

### Gunshots

- [ ] **Sound travels.** First prototype: an FCSE plugin that holds back `Weapon_NPC`
  plays by distance ÷ speed of sound (hook `CSoundSystem::PlaySound` `+0x9c`, flush in `Update`
  `+0x50`, vtable `0x10e82d10`). Nothing in the engine delays a sound today.
- [ ] **Delay explosions too** (sound type 10), once their emitters are known to outlive the delay.
- [ ] **Crack before report.** Comes with the delay, because fly-bys are not delayed. It needs better
  crack samples.
- [ ] **No crack from subsonic or silenced weapons** (silenced Makarov, Dart Rifle). The fly-by sound is
  per player, not per weapon, so a calibre-aware crack needs a plugin.
- [ ] **Close layers per weapon.** Add transient, mechanical (bolt, spring) and body layers as children
  of the first- and third-person multi-events (type `12`), in new banks with `depload` entries.
- [ ] **Far layer for NPC shots.** Fill `sndmlDistanceFromShootingSoundToPlayerMultilayer` (empty on
  all 91 weapons) and author a multilayer resource (kind `7`) that fades close → far over the
  shooter's distance. Candidate parameter: the unused `0x00440259` (range 0–250).
- [ ] **Pre-filter the far layer in the sample**, so the low-passed boom needs no code.
- [ ] **Replace the single-clip third-person shots.** The AK47's third-person auto shot is one mono
  clip; give every NPC weapon a layered close/far pair.
- [ ] **Malfunction sounds on every weapon.** `sndMalfunction*` is set only on the Carl Gustaf.
- [ ] **Weapon wear in the shot sound.** 41 switch resources key on the weapon-status switch
  (`0x004402A7`), but `WeaponStatusSwitchValues` is empty on all 101 weapons. Check whether wear is
  audible, and wire it if not.
- [ ] **Single-player hit confirmation (optional).** `sndHitPlayerSound`/`…HeadSound` are set only on
  multiplayer weapons.

### Environment tail

- [ ] **Bring the authored reverb back.** If DSOAL works, adopt it and have JackAll deploy the two
  DLLs. If not, the fallback is a software reverb in a plugin, or our own renderer (very expensive).
- [ ] **Retune the reverb presets** in `common/soundbinary/7fffffff.bao` (63 presets, EAX form).
- [ ] **Give the 84 reverb-less placed buildings a reverb.**
- [ ] **Wake the player's echo.** Fill `sndSingleBulletShotEcho` and `sndStart/StopAutoBulletShotEcho`
  (empty on every weapon) with real echo tails. Any slapback delay goes in the sample, because the
  echo starts with the shot.
- [ ] **Building echo lengths.** `fEchoLength` is 0 on 620 of 623 placed buildings; set it per
  building class.
- [ ] **Retune region echo lengths and reverbs** per biome and intensity in `common/soundregions.xml`
  (`fEchoLenght` 1.2–6.1 s today).
- [ ] **Echo on NPC gunshots** (plugin). An NPC's shot never plays an echo.
- [ ] **Terrain-aware slapback** (plugin). Ray-cast from the shot to nearby hills and cliffs to set the
  delay and direction of the echo, instead of a per-biome length.

### Distance and occlusion

- [ ] **Air absorption** (plugin). Drive DARE's per-voice Butterworth low-pass by distance for
  positioned types (weapons, explosions, barks, vehicles, animals), so a merc yelling 80 m away sounds
  far, not just quiet.
- [ ] **Terrain and cover occlusion outdoors** (plugin). Two outdoor points are never occluded today; a
  ray test from listener to source can feed the same low-pass.
- [ ] **Stronger building occlusion filters** (data). `fOcclusionFilter` is 0 on most buildings and
  `fSoundOcclusionFilter` on 97% of entrances, so walls only lower the volume.
- [ ] **Widen the occlusion mix preset.** `Compatible.VolumeLinesToLowerInsideForOcclusion` touches
  only ambience types 0, 23 and 24; decide which other types it should muffle when the listener is
  indoors.
- [ ] **Retune rolloff curves** in `common/soundbinary/2fffffff.spk` (all 96 curves, shared). Make
  gunfire carry realistically far, and give events their own curves where sharing is wrong.
- [ ] **Raise the voice cap.** `NB_AUDIBLE_VOICES=64` in `DARE.INI`; distant gunfire loses out in big
  fights. The −48 dB distance cull is code.
- [ ] **`occmul_pc` = 1.0 against 50.0 on consoles.** Find what it multiplies, then decide whether PC
  occlusion should match the consoles.
- [ ] **Distant voices.** Barks get air absorption through the plugin above; also check their rolloff
  curves.
- [ ] **HRTF for headphones.** OpenAL Soft behind DSOAL can render DS3D voices with HRTF.

### Near misses and ricochets

- [ ] **Near-impact wizz.** Fill `sndPassByWizz` (empty on all 91 weapons) so NPC rounds landing
  within 5 m get a whiz before the impact (`fPassByWizzTiming` 0.15 s). The field alone is not enough:
  that sequence plays at volume `0.0` → −96 dB (`0x100faea0`), so the constant needs patching to `1.0`
  at the same time, or close impacts go silent.
- [ ] **Better fly-by sounds.** Replace `sndPassByRegularSound`/`NearSound`/`UnderwaterSound` on the
  player component with real cracks and whizzes.
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
- [ ] **Music under combat.** `CMusicManager` reacts to shots, pass-bys, hits and explosions; decide
  whether combat music should back off for tension.
- [ ] **Sub-mixes.** All 25 sound types go to one line, `Master`; separate lines would allow group
  volumes.

### Tooling (JackAll)

- [ ] **Write new `.spk` events and banks**: leaves (type `1`) and multi-events (type `12`).
- [ ] **Author resource containers**: switches (kind `3`), random containers with weights (kind `4`),
  multilayers (kind `7`).
- [ ] **Rolloff curve editor** for `2fffffff.spk`.
- [ ] **Reverb preset editor** for `7fffffff.bao`, including new game-parameter declarations.
- [ ] **`depload` entries** for every new bank chain, since an unlisted bank plays silent.

### Open reverse-engineering questions

- [ ] **Emitter `GetPosition` slot in `Dunia.dll`** (`+8` in the server's interface): a prerequisite of
  the first prototype.
- [ ] **Where the PC build turns `ComputeOcclusion` into DARE's per-voice filter amount**: the hook
  point for air absorption.
- [ ] **The low-pass's minimum and maximum cutoffs** that obstruction maps between.
- [ ] **Whether DS3D's own distance rolloff is neutralised**, or doubles DARE's curves.
- [ ] **Where the occlusion materials live**, and their values.
- [ ] **What `occmul_pc` multiplies.**
- [ ] **The fourth DARE private effect.**
- [ ] **How `fAngle` is converted** in the fly-by (default 90, retail 1.25).
- [ ] **Event types `2`, `3`, `4` and `10`**: only partly traced.
- [ ] **Multilayer (kind `7`) and switch (kind `3`) layouts**, confirmed in code rather than only in data.
- [ ] **Whether a game parameter clamps at its declared range**, e.g. distances beyond 250 on
  `0x00440259`.
- [ ] **The `.srl` reader.** Confirm that the cell byte is `(intensity << 4) | region`.
- [ ] **Whether the weapon-status switch is answered at runtime.**
