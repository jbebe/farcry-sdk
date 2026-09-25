---
sidebar_position: 1
---

# Realistic Sound

**Status:** research done (2026-09-25). The [free listen](#before-anything-a-free-listen) confirmed the
software low-pass works on PC. Nothing built yet. The next steps are the
[DSOAL experiment](#experiment-bring-eax-back-with-dsoal), then the
[first prototype](#first-prototype-sound-travels-at-340-ms).

## The goal

Far Cry 2 should sound like a serious shooter, in the way *Tarkov*, *Insurgency*, *STALKER* and *DayZ*
do. A gunshot is the loudest thing you have heard in a while. It tells you how far away the shooter is
and where, and the land around you answers it. Six targets make that up:

1. **Layered gunshots.** Up close, a shot is a sharp transient, a mechanical layer (bolt, spring) and a
   body. At range it becomes a low-passed boom that arrives late (sound travels at about 343 m/s), so a
   distant shooter's supersonic crack reaches you before the report.
2. **An environment tail.** Echo and slapback off hills, a wash in forest, reverb indoors, all depending
   on where the shot was fired.
3. **Distance and occlusion.** Air absorption, a low-pass that grows with distance, and muffling behind
   walls and terrain. This includes voices: a merc yelling 80 m away should sound far and filtered, not
   just quieter.
4. **Near misses and ricochets.** A crack or whiz when a round passes close, and ricochet whines off rock
   and metal.
5. **Impacts per material:** dirt, wood, metal, flesh.
6. **Mix and dynamic range.** A quiet world, so that a gunshot is the loudest thing you have heard.
   Music and ambience duck under gunfire.

For each target the question is whether FC2 already has a mechanism we can feed better data, one an
FCSE plugin can hook, or nothing at all.

## What the engine gives us

Everything below rests on [audio runtime](../engine-internals/audio-runtime.md). The parts that decide
the verdicts:

- **Loudness over distance is data.** Every positioned event has a rolloff curve, a piecewise table of
  decibels against metres ending in a hard cut. All 96 curves sit in one bank. Nothing else changes with
  distance: no filter, and **no delay**.
- **DARE has a per-voice low-pass and band-pass in software.** They run on a modern PC. The game drives
  the low-pass per sound from building zones and the mix presets' filter. The band-pass gets no input in
  retail.
- **Reverb exists only as EAX 4**, and it is authored almost everywhere: 63 presets, one per building
  class and per biome and intensity. On a PC without EAX all of it is silent.
- **Occlusion is zones, not rays.** A building and its doorways muffle what crosses them; nothing
  outdoors ever occludes.
- **A snapshot mixer** sets a volume and a filter per sound type (25 types), with priorities and
  fades. Presets are data; triggers are code or Domino scripts.
- **The weapon code is ready for more than retail gives it.** A per-shot echo on the player's weapon, a
  near-impact wizz, and a DARE multilayer that follows the shooter's distance from the player are all
  wired in the code. All three are **empty on every retail weapon**.
- **Fly-bys are geometric.** Every bullet's closest approach to the player's head is tested, and a
  moving, Doppler-shifted source is sent past the ear.
- **Impacts switch per surface material** into random containers. There is **no ricochet** anywhere.

Two premises of the original brief turned out wrong:

- The weapon sound fields live on `CWeaponProperties` → `FireStrategyProperties/Sounds`, not on
  `CCompoundPhysNetworkComponent`.
- `sndswtpCloseFarSoundSwitchType`/`sndswvlCloseSoundSwitchValue` do not exist. The switch ids that
  suggested them are the surface-material switch.

A third correction affects the plugin route: `FUN_10a38d20` is DARE's resource enumerator, not its
play dispatcher. The dispatcher is `FUN_10a3c9c0`.

## Verdicts

| # | Target | Verdict | In one line |
| --- | --- | --- | --- |
| 1 | Layered gunshots | **data + plugin** | Layering and a distance-driven far layer are data; the late arrival needs a plugin |
| 2 | Environment tail | **backend + data** (plugin for NPC echoes) | The reverb is authored and needs EAX; the player's echo is a dormant data slot |
| 3 | Distance and occlusion | **plugin** (+ data) | The filter exists, but nothing drives it by distance or by terrain |
| 4 | Near misses and ricochets | **data + plugin** | Fly-bys exist; the near-impact wizz is empty and needs a volume fix; ricochets need adding |
| 5 | Impacts per material | **data** | The per-material switch already exists |
| 6 | Mix and dynamic range | **data + plugin** | The mixer exists; nothing triggers it on gunfire |

Nothing on the list is impossible.

### 1. Layered gunshots — data + plugin

- **Close layers: data.** A multi-event (event type `12`) starts all its children together.
  First-person shots already are one: the weapon's sample plus a shared, surface-switched layer, which is
  probably the shell casings **(inferred)**. More layers mean more children, in new banks with
  [`depload`](../file-formats/depload.md) entries.
- **The far boom: data, once we can author it.** An NPC's shot plays in 3D at the weapon, through an
  emitter that answers the weapon's `sndmlDistanceFromShootingSoundToPlayerMultilayer` with the
  shooter-to-player distance in metres **(RE-verified)**. A DARE multilayer resource (resource kind `7`)
  can fade layers along a game parameter with (value, dB) curves **(seen in data: wind, vehicles)**.
  Retail sets the field on no weapon. The AK47's third-person shot is a single mono clip. The unused game
  parameter `0x00440259` (0–250) looks like the one intended for this **(inferred)**. A far layer can
  also be pre-filtered in the sample, so the low-pass needs no code.
- **Late arrival: plugin.** Nothing in DARE or the game delays a sound by distance **(RE-verified)**, and
  a multi-event cannot offset a child. Only a plugin can hold the report back by `d / 343`.
- **Crack before report: comes with the delay.** The fly-by fires at the player the moment a bullet's
  path is resolved **(RE-verified)**. Once reports are delayed, a far shot that passes close gives crack,
  then report, with no extra code. The crack's sound is data (`sndPassBy…` on the player).

### 2. Environment tail — backend + data (plugin for NPC echoes)

- **Reverb: backend.** Building zones, biome sound regions and mix presets all select reverb through
  type-`8` events. These end in an EAX 4 listener effect and nowhere else **(RE-verified)**. The data is
  there **(seen in data)**:
  - buildings: a small room (0.4 s), a darker 2.5 s space, and a hangar (6.8 s);
  - jungle: 1.6–3.0 s of dense reverb;
  - desert: a 1.0 s preset with full echo depth.

  On a PC without EAX none of it plays. [DSOAL](#experiment-bring-eax-back-with-dsoal) may bring all of it
  back unchanged.
- **The player's echo: data.** `sndSingleBulletShotEcho` and the auto-fire pair play at the shot, and
  the listener's echo length trims them: the sound region's `fEchoLenght` (1.2–6.1 s), or the building's
  **(RE-verified)**. The fields are empty on every weapon, so filling them wakes the path. The echo starts
  with the shot, so any slapback delay has to be in the sample.
- **NPC echoes, and terrain-aware slapback: plugin.** An NPC's shot never plays an echo
  **(RE-verified)**. Echo length follows the biome region, not the hills around you.

### 3. Distance and occlusion — plugin (+ data)

- **Air absorption: plugin, plus one data value.** Each voice has a 12 dB/octave Butterworth low-pass
  whose cutoff follows an "obstruction" amount **(RE-verified)**. No distance feeds it. The game writes
  that amount once per sound in the per-type occlusion callback (`FUN_10621880`), which is the hook
  point: add a distance term there, for the types flagged `occlusion`. DARE caches the result per sound
  object, so the plugin must also mark playing sounds dirty as the listener moves. The low-pass itself
  is confirmed working on PC **(heard in game)**. The other catch is the curve. Retail
  maps obstruction onto 20–3,200 Hz so steeply that any amount at all means a cutoff under 3.2 kHz.
  Gentle dulling at mid range needs the maximum in `7fffffff.bao` raised, say to 20 kHz. That also
  softens the existing occlusion: underwater would go from about 90 Hz to about 460 Hz, and a building
  filter of 0.01 from 1.2 kHz to 7.4 kHz **(computed from the traced curve)**.
- **Behind walls: data, within limits.** A sound from inside a building is low-passed by that building's
  `fOcclusionFilter` **(RE-verified)**. A wall never makes a gunshot quieter: `fOcclusionVolume` reaches
  only the outdoor ambience types. The filter is 0 on 391 of 623 buildings and 97% of entrances
  **(seen in data)**; where it is set, even 0.01 means a cutoff near 1.2 kHz. Setting filters is data.
- **Behind terrain or cover: plugin.** Two outdoor points are never occluded. A ray test from listener to
  source, feeding the same low-pass, is plugin work.
- **Voices: the same path.** Barks are a positioned, occluded sound type, so they get the same curves,
  zone occlusion and any plugin air absorption **(inferred: the bark play call is not traced)**.

### 4. Near misses and ricochets — data + plugin

- **Fly-bys: data.** The player's component sends a Doppler-shifted moving source past the head when a
  bullet passes within `fNormalRadius` (2.7 m in retail), with a near variant within `fSmallerRadius`
  (0.6 m) **(RE-verified, values seen in data)**. Better cracks and whizzes are new samples; the radii
  are entity fields.
- **Near-impact wizz: data plus a one-instruction fix.** The weapon's `sndPassByWizz` plays a wizz
  before the impact when an NPC round lands within 5 m of the player. It is empty on every weapon, so in
  retail those impacts play normally. The sequence it would start passes volume `0.0` (−96 dB) where the
  plain impact passes `1.0` **(RE-verified)**. Filling the field alone would silence close impacts, so it
  needs that constant patched as well.
- **Ricochets: data or plugin.** Nothing exists. Data can add a whine as extra variants in the rock and
  metal entries of the bullet-impact material switch: each entry is a random container with Q16.16
  weights, so a weight is the chance. A plugin is needed only for angle- or calibre-aware ricochets.

### 5. Impacts per material — data

`Weapon.Bullet` plays one event whose resource is a material switch with 36 entries, each a random
container of 2–7 clips. Materials come from `logicmaterials.xml`, 39 logic materials in all
**(RE-verified path, seen in data)**. Replacing or adding clips is data. One gap is behaviour, not data:
an NPC bullet hitting the player plays no impact sound **(RE-verified)**.

### 6. Mix and dynamic range — data + plugin

- **The mixer: data.** `soundmixings.xml` presets set a volume and a filter per sound type, with
  priorities and fades **(RE-verified)**. The low-health, stamina and healing presets are wired to the
  player's state but empty in retail **(seen in data)**, so filling them is pure data.
- **A quiet world: data.** Ambience types can be lowered with a permanent preset, or with the sounds and
  rolloff curves themselves, so that shots stand out.
- **Duck under gunfire: plugin.** No code applies a preset when shots are fired near the player. A plugin
  calling `CMixingManager::ApplyPreset` on each nearby shot would get the fades and priorities for
  free. So would a Domino script, if a trigger exists.
- **No limiter.** DARE has no compressor or limiter, so louder shots need headroom taken from everything
  else. That makes the mix a data job.

## Routes

| Route | Cost | Risk | Targets |
| --- | --- | --- | --- |
| (a) Data: new samples, retuned fields, rewired events | Low per change, but new events, containers, multilayers, curves and reverb presets need JackAll authoring that does not exist yet | Low. Every new bank chain needs its `depload` entry | 1, 2, 4, 5, 6 |
| (b) FCSE plugin | Medium. Hook the `CSoundSystem` vtable (`0x10e82d10`: `PlaySound` `+0x9c`, `PlaySoundAtPosition` `+0xa0`, `Update` `+0x50`) rather than DARE's internals | Medium. Deferred plays must not outlive their emitter; the GOG build needs the address library | 1, 2, 3, 4, 6 |
| (c) Backend: DSOAL | None: two DLLs next to the game | Low and reversible. Unknown whether it answers FC2's EAX 4 probe | 2, and EAX occlusion as a bonus |
| (c) Backend: own renderer (DirectSound → OpenAL Soft with EFX) | Very high: rewrite DARE's DS3D renderer | High | Only worth it if DSOAL fails and reverb must come back |

The recommended route is **data for content, a thin plugin for timing and distance, and DSOAL for
reverb**. Most of the data route waits on JackAll learning to write new `.spk` events and resource
containers. The plugin route and the DSOAL experiment need no new assets.

## Before anything: a free listen

**Done 2026-09-25, on GOG: the software low-pass works.**

Air absorption (target 3) rests on one link: a filter amount set by the game reaches DARE's low-pass.
That link is traced end to end **(RE-verified)**. Diving applies `Exclusive.Underwater`, which sets
dialog and vehicles to 0 dB and NPC weapons, explosions and 3D effects to −10 dB, all with filter 0.8:
obstruction 0.8, a cutoff near 90 Hz.

What was heard:

1. Under water, NPC gunfire, voice lines and explosions are silent, even up close.
2. Applying the preset from the console on dry land (`#StartSoundMixingFromLua("Exclusive.Underwater")`)
   lowered the ambience but left an already-running car engine unchanged.
3. Removing only the preset while under water (`#StopSoundMixingFromLua("Exclusive.Underwater")`)
   brought the outside world back.

The third run is the proof. It keeps everything else about being under water and takes away one thing.
Voices sit at 0 dB in the preset, so their silence is the filter. The start event under water is only
two water loops **(seen in data)**, and no other underwater code mutes anything **(RE-verified)**.

The second run changes the plugin plan. DARE caches each sound object's occlusion and asks the game
again only when the object is marked dirty (see
[audio runtime](../engine-internals/audio-runtime.md#the-occlusion-is-cached-per-sound-object)). A
distance filter that must follow a moving listener has to refresh sounds that are already playing.

## Experiment: bring EAX back with DSOAL

**Hypothesis.** FC2's reverb and EAX occlusion are silent only because DARE finds no EAX device. The
shipped `eax.dll` needs Creative's drivers, so DARE falls back to `DirectSoundCreate8` from the system
`dsound.dll`. A [DSOAL](https://github.com/kcat/dsoal) `dsound.dll` placed next to the game answers that
call instead. If it also answers the EAX 4 probe, the authored reverb plays without any change to the
game.

**First run, 2026-09-25 (GOG): no sound at all.** DSOAL r649 (March 2025) in `bin\` made the game
open with its "Your Sound-Driver is currently used by an other application" box. DARE's device open
continues only if `DirectSoundEnumerateA` returns exactly 0, and its callback stops the enumeration at
the first real device **(RE-verified)**. DSOAL returned `S_FALSE` in that case until commit `4dbbffa`
(14 Oct 2025, first in build r689). DSOAL's log stopped right after listing the devices, before any
device was created **(seen in the log)**.

**Setup.** Install the Sound Overhaul plugin (`mods/sound-overhaul`): it bundles DSOAL r695 and loads it from its own plugin folder, and also flips the enumeration check so an
older DSOAL works too. FC2 asks for EAX 4 specifically (`EAXPROPERTYID_EAX40_FXSlot0`), which DSOAL
provides. Remove any `dsound.dll` from `bin\`, or the plugin leaves that one in charge. To see the EAX
calls, launch from a shell with `DSOAL_LOGLEVEL`, `DSOAL_LOGFILE`, `ALSOFT_LOGLEVEL` and
`ALSOFT_LOGFILE` set.

**Listen, with and without the DLLs:**

| Where | Expected with EAX | Without |
| --- | --- | --- |
| Inside a hangar or warehouse (industrial buildings) | a long tail, about 7 s | dry |
| Inside an ordinary building | a short room tail, about 0.4 s | dry |
| Outdoors in jungle | gunshots and footsteps bloom, 1.6–3 s | dry |
| Outdoors in desert | a flutter echo (echo depth 1.0) | dry |
| Walking out of a doorway while someone fires outside | the muffle changes more strongly | volume only |

**Read the result.**

- **Clear tails:** EAX is live, target 2's reverb is solved by the bundled DSOAL, and the backend route
  becomes adopting it.
- **No difference:** check the log first. No DSOAL log at all means the DLLs were not picked up. A log
  without EAX property calls means the EAX 4 probe failed.
- **Crashes or stutter:** a compatibility question for DSOAL, not for us.

## First prototype: sound travels at 340 m/s

**What.** An FCSE plugin that holds back every NPC gunshot by its distance divided by 340 m/s.

**Why this one.**

- It is the cue that most separates a serious shooter from an arcade one: the flash across the valley,
  then the report.
- It has no engine support at all, so no data change can fake it.
- It needs no new assets.
- It is small.
- It proves the one capability the plugin route depends on: owning *when* a game sound plays. Delayed
  far layers, NPC echoes, ricochets and gunfire ducking all build on that.
- It brings target 1's crack-before-report with it, because the fly-by is not delayed.

**How.**

1. Hook `CSoundSystem::PlaySound` (vtable `0x10e82d10` `+0x9c`: event, sound type, emitter callbacks,
   volume in dB) and `CSoundSystem::Update` (`+0x50`).
2. In `PlaySound`, for sound type 9 (`Weapon_NPC`) only:
   - get the emitter's position from its callbacks (`GetPosition`, slot `+8` in the server's
     interface; confirm it in `Dunia.dll` first);
   - get the listener's position from `GetListenerCallbacks` (`+0xc`) and the same slot;
   - queue the call if `d / 340` is above a few tens of milliseconds, and return no handle.

   The third-person single and auto-start plays do not keep their handle **(RE-verified)**, so returning
   none is safe for them.
3. In `Update`, play every due entry through the original `PlaySound` before the original `Update`
   runs. Both happen on the game thread.
4. Cap the delay (1.5 s, about 500 m) and log counts to `fcse.log`. The emitter lives inside the
   weapon's fire strategy, so a weapon destroyed within the delay would leave a stale pointer. The cap
   keeps that window small.

Explosions (type 10) come next, once the emitters they use are known to outlive the delay.

**Test, once built.** Find an outpost across open ground at 150–300 m and start a fight. Without the
plugin, the report and the muzzle flash coincide. With it, each report lands 0.45–0.9 s after its
flash, and automatic fire keeps its rhythm, shifted. Bullets that pass close crack before the report
arrives. A hotkey toggle lets you A/B it in the same fight.

## Found along the way

- **A trap in the near-impact wizz.** Setting `sndPassByWizz` without patching its `0.0` volume would
  mute every NPC round landing within 5 m of the player. See target 4.
- **Empty presets.** Low health, medium health, critical health, stamina and healing presets are wired
  but do nothing.
- **Dormant weapon fields.** Echo, near-impact wizz and the distance multilayer are empty on all 91
  weapons.
- **One bank for all distances.** `common/soundbinary/2fffffff.spk` holds every rolloff curve. The most
  used one, shared by 949 events, cuts at 80 m.
- **Hit markers** exist only on the multiplayer weapons.
- `soundconfig.xml` sets `occmul_pc` to 1.0 against 50.0 on consoles. It multiplies every sound's
  obstruction, so on consoles any occlusion at all becomes full obstruction.
- **Walls filter gunfire but never make it quieter**, and only where a building's `fOcclusionFilter` is
  set. The retail values look negligible (0.01) but, on the cutoff curve, mean about 1.2 kHz.

## Tooling this needs

JackAll can replace audio inside existing `.spk` records. The data route needs it to also write:

- new events: leaves (type `1`) and multi-events (type `12`);
- resource containers: switches (kind `3`), random containers (kind `4`) and multilayers (kind `7`);
- rolloff curves in `2fffffff.spk`;
- reverb presets in `7fffffff.bao`;
- the `depload` entries for any new bank chain.

## Decisions

- 2026-09-25: sound is the first module of the realism work, ahead of combat-number tuning.
- 2026-09-25: this pass is research only. No mod code until the DSOAL experiment and the free listen
  are in.
- 2026-09-25: the free listen is in. The software low-pass works on PC, so air absorption stays on the
  plugin route, through the occlusion callback.
