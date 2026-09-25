---
sidebar_position: 20
---

# Audio Runtime

:::info[Verified via reverse engineering]
Traced live via GhidraMCP. The middleware (DARE, Ubisoft's sound engine, statically linked) is traced in
**`Dunia.dll`** (Steam build, image base `0x10000000`): every `FUN_10a…` address on this page is that
binary's. The game layer above it keeps its C++ names in the **`FarCry2_server`** ELF, where the sound
calls are stubbed but the logic around them is intact. Names like `CMixingManager::Update` and addresses
in the `0x08…`/`0x09…` range are the server's.
:::

:::info[Verified against the retail corpus]
Values quoted from data come from the extracted retail files: `config/soundconfig.xml`,
`databases/soundmixing/soundmixings.xml` and every `.spk` bank (4,895 files, 11,847 records).
:::

FC2's sound has two layers. DARE decodes, positions and mixes voices and talks to DirectSound. The game
layer decides what plays, which sound type it belongs to, how loud each type is right now and whether
the listener is inside a building. What each layer can and cannot do decides what a sound mod can change.

## In short

- DARE outputs through **DirectSound 8**. Every voice gets its own DirectSound buffer, and a mono
  positioned voice gets a DS3D buffer. DirectSound places it; **DARE sets its loudness**.
- Distance attenuation comes from a **per-event rolloff curve**: a piecewise-linear table of metres
  against decibels, ending in a hard cut to −96 dB.
- DARE has a real **per-voice software filter chain**: a 12 dB/octave Butterworth low-pass driven by an
  obstruction amount, and a band-pass driven by occlusion materials. It runs on any PC. The game drives
  the low-pass, from building zones and the mix presets' filter, for every type flagged `occlusion`.
  Retail maps obstruction onto a 20–3,200 Hz cutoff and gives the band-pass no input.
- It has **no software reverb, delay line, EQ, compressor or limiter**. The only reverb is an
  **EAX 4** listener effect, and a PC without an EAX device (Windows Vista or later, without DSOAL or
  Creative's ALchemy) runs FC2 with **no reverb at all**.
- Occlusion is **zone-based**. A building is a zone and its doors and windows are holes. Nothing is
  ray-cast, so terrain and walls between two outdoor points do not muffle anything.
- Loudness per category comes from **25 sound types** and a **snapshot mixer** (`CMixingManager`) whose
  presets set a volume and a filter amount per type, with priorities and fades.
- **Doppler** is computed with a speed of sound of 340 m/s. Nothing delays a sound by distance.
- The weapon code supports an echo on the **player's** gunshot and a DARE multilayer on an NPC's
  gunshot driven by its distance from the player. **Retail sets neither**: every weapon's echo and
  distance-multilayer fields are empty. Bullet fly-bys are geometric, and there is no ricochet sound
  anywhere.

## From event to speaker

### The game asks for an event

Game code plays a DARE event id through `CSoundSystem` (`PlaySound`, `PlaySoundAtPosition`,
`PlaySoundReverb`, …), against a sound object and one of the sound types below. The ids come from
entity fields prefixed `snd` (see the [entity component schema](./entity-component-schema.md)).

### DARE dispatches on the event type

`FUN_10a3c9c0` is the play dispatcher. It switches on the event object's type (the
[binary event objects](../file-formats/spk.md#binary-event-objects) in `.spk` banks):

| Type | Handler | What playing it does |
| --- | --- | --- |
| `1`, `5`, `6`, `7`, `9` | `FUN_10a3c730` | starts a voice on the event's sound resource |
| `2` | `FUN_10a383b0` | acts on the event in word `[2]`, with a Q16.16 value from word `[3]` (1.0, 0.3, 0.5, 3.0 in retail): reads as stop-with-fade **(inferred)** |
| `3` | `FUN_10a38420` | acts on the event in word `[2]` **(inferred: stop family)**; no retail records |
| `4` | `FUN_10a3acf0` | starts a child instance from the events it links (words `[2]`, `[3]`); not traced further |
| `8` | `FUN_10a31bf0` | **sets the reverb** to the effect in word `[2]` (see [reverb](#reverb)) |
| `10` | `FUN_10a37060` | applies a Q16.16 dB value over a duration to a target: a volume fade **(inferred)** |
| `11` | `FUN_10a3ae70` | a **switch**: plays the **one** child whose key matches the object's current switch value (`FUN_10a37200`), else the default in word `[4]` |
| `12` | `FUN_10a3b090` | a **multi-event**: starts **every** child at the same moment and groups them under one instance |

A multi-event has no per-child offset, so a layer can never start later than its siblings. A switch has
no crossfade: the value picks one child when the event starts.

### A voice is a chain of tools

`FUN_10a6ff20` builds a voice as a chain of DARE "tools": a decoder first (`FUN_10a7ae40`, Ogg Vorbis or
IMA-ADPCM), then the stages the binary names `TPitch`, `TFader`, `TVolPan` and `TEffectChain`, with the
effect stage (`FUN_10a630c0`) last. The effect stage holds the voice's two filters (see
[DSP](#dsp)). `FUN_10a760e0` updates a voice's parameters: it clamps the volume to −96…0 dB in
Q16.16 and pushes the occlusion values into the effect stage.

### The DS3D renderer

DARE's output renderer is built by `FUN_10a4ede0` and reads the `[Renderer DS3D Options]` section of
`DARE.INI`. `FUN_10a4c8f0` opens the device:

1. `DirectSoundEnumerateA`, then `EAX.DLL!EAXDirectSoundCreate8`. If that fails it falls back to
   `DSOUND.DLL!DirectSoundCreate8`. The enumeration callback (`0x10a49d20`) stops at the first real
   device, and the open goes on only if `DirectSoundEnumerateA` returned exactly 0. A DirectSound that
   returns `S_FALSE` there fails the whole open, and every failure of this function shows the same
   "Your Sound-Driver is currently used by an other application" box (`FUN_10a4cf70`).
   `Dunia.dll` imports both DirectSound functions by ordinal, 11 and 2.
2. Priority cooperative level, a primary buffer with `DSBCAPS_CTRL3D`, and an
   `IDirectSound3DListener8` on it.
3. A 400 ms looping software buffer (`LOCSOFTWARE`, volume/pan/frequency control) that DARE fills with
   its own software mix (`FUN_10a4ae40`, the "RDSound3D tool buffer"). The strings name DARE's
   interactive-music "themes" as a user of this path.
4. The EAX probe (see [reverb](#reverb)).

Every other voice plays through its own buffer from one of three pools, pre-created when
`LAZY_SOUND_BUFFER_POOL=FALSE` (retail sets it) with `NB_AUDIBLE_VOICES` buffers each:

| Pool | Format | Flags |
| --- | --- | --- |
| positioned | 48 kHz mono | `0x80B0`: `CTRL3D`, volume, frequency |
| stereo | 48 kHz stereo | `0x80E0`: pan, volume, frequency |
| unpositioned mono | 48 kHz mono | `0x80E0` |

`FUN_10a4d260` picks the pool: a mono voice with a 3D position gets a DS3D buffer, and a stereo voice
never does. All three add `DSBCAPS_LOCDEFER` unless the device matches `NO_OPTIMISATION_SOUND_CARDS`.
Each buffer is a 200 ms streaming ring that DARE keeps filled. A voice's 3D position and velocity go to
`IDirectSound3DBuffer::SetPosition`/`SetVelocity` as deferred settings. `FUN_10a4e700` then commits them
once per update through the listener's `CommitDeferredSettings`. If a pool runs dry, `FUN_10a6a670`
creates a buffer on demand.

## How many voices

`NB_AUDIBLE_VOICES` (`[Sound Manager Options]`, default 64, written back to the INI when missing) is the
cap. `FUN_10a5fb60` enforces it on every update:

- It adds up the voices every playing instance needs. Instances also belong to voice groups that each
  have their own cap.
- When the total or any group is over, `FUN_10a5f980` sorts the instances by **priority**, then by
  **loudness after distance attenuation**, then by **age**. The newest instance wins a tie.
- It walks the sorted list, and an instance that no longer fits is **virtualised or stopped**, as a
  callback decides.
- Separately, any positioned instance quieter than **−48 dB** after distance attenuation is dropped,
  even when voices are free.

## Distance

### Rolloff curves

`FUN_10a66740` computes a positioned voice's attenuation. It takes the distance from the listener and
evaluates the event's rolloff curve (`FUN_10a55440`) by linear interpolation. Past the last point the
curve holds its last value, and every retail curve ends with a point at −96 dB, so **the last point is the
event's audible range**. A voice flagged as unpositioned skips this and gets 0 dB. A positioned voice
with no curve gets −96 dB. The curve is the event's word `[7]`, a
[rolloff record](../file-formats/spk.md#rolloff-curves) in the banks.

Across retail's 96 curves the range runs from 4 m to 1,000 m. The two most used are an 80 m curve
(949 events: −3.8 dB at 0 m, −19 dB at 56 m, −35 dB at 74 m) and a 40 m curve (602 events). Nine curves
reach 350 m or more; the longest drops to −52 dB at 914 m and cuts at 1,000 m.

Loudness over distance is data. Anything that changes with distance other than loudness is not: no
curve sets a filter.

### What DirectSound adds

DS3D panning places the voice around the listener. Each DS3D buffer gets a minimum and maximum
distance when it is created. Whether those neutralise DirectSound's own rolloff, so that DARE's curve is
the only attenuation, is not traced.

### Doppler, and no delay

`FUN_10a6bf60` computes a Doppler pitch factor, `c / (c − relative radial speed)`, with
`c = 340` (`DAT_10fa6860`). It logs "Relative speed is greater than the sound speed." when a source
outruns sound. That is the only use of a speed of sound in DARE. Nothing in DARE schedules a sound later
because it is far away, and a multi-event cannot offset a layer.

## DSP

What DARE can do to a voice's signal:

| Processing | Code | Parameters | Needs EAX |
| --- | --- | --- | --- |
| Low-pass, 2nd-order Butterworth | `FUN_10a7c0e0` | cutoff 20–20,000 Hz, gain −96…+6 dB; bypassed at 20 kHz and 0 dB | no |
| Band-pass, 2nd-order | `FUN_10a7c220` | centre 20–20,000 Hz, width 40–20,000 Hz, gain −96…+6 dB; bypassed at full width and 0 dB | no |
| Doppler pitch | `FUN_10a6bf60` | speed of sound 340 | no |
| Listener reverb | `FUN_10a49a40`, `FUN_10a4a530` | `EAXREVERBPROPERTIES` (EAX 4, FX slot 0), with transitions between reverbs | **yes** |
| Per-voice occlusion, obstruction, room send | `FUN_10a68c00`, `FUN_10a69110`, `FUN_10a68640` | `EAXSOURCE_OCCLUSIONPARAMETERS`, `…OBSTRUCTIONPARAMETERS`, `EAXSOURCE_ROOM` | **yes** |

There is no compressor, limiter, EQ, software reverb or delay line anywhere in DARE.

### The software filters

DARE registers four private effects at start-up (`FUN_10a44850` → `FUN_10a61d90`):

1. the low-pass,
2. a band-pass,
3. a band-pass with flags,
4. one more that is not identified.

`FUN_10a62b10` builds the chain every voice gets: the flagged band-pass, then the low-pass. The inputs
come from the voice's occlusion block:

- **Occlusion materials**, a weighted list. Each material is a DARE project resource (`FUN_10a54510`)
  carrying both a software band (centre, width, gain, flags) and an EAX occlusion set. `FUN_10a62540`
  intersects the materials' pass bands into one band-pass and adds their gains. Two materials whose
  bands do not overlap give −96 dB. **Retail uses none**: the project descriptor declares no materials
  **(seen in data)** and the game's occlusion callback always passes an empty list **(RE-verified)**, so
  the band-pass never engages.
- **Obstruction**, one amount from 0 to 1. `FUN_10a62c30` turns it into the low-pass cutoff and switches
  the low-pass on when the amount is above zero:

  ```
  cutoff = min + (max − min) × (1 − obstruction^0.1)
  ```

  `min` and `max` come from the DARE project descriptor, `common/soundbinary/7fffffff.bao`: 20 Hz and
  3,200 Hz in retail (see [the project descriptor](../file-formats/spk.md#the-project-descriptor)).

The curve is steep. Any obstruction above zero puts the cutoff under 3.2 kHz:

| Obstruction | 0 | 0.001 | 0.01 | 0.1 | 0.5 | 0.8 | 1 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Cutoff | off | 1.6 kHz | 1.2 kHz | 674 Hz | 233 Hz | 90 Hz | 20 Hz |

The project descriptor also decides what obstruction does. Retail sets it to drive the low-pass. Set
the other way, `FUN_10a4fed0` would turn obstruction into a volume drop instead, and the low-pass would
stay off (`FUN_10a34630`). With the low-pass on, obstruction never lowers the volume.

The flag that would switch the software filters off (`DAT_11656c48`) is never written, so they run
whether or not EAX is present. On an EAX card both paths would act **(inferred)**.

### EAX is probed, not assumed

After opening the device, `FUN_10a4acb0` creates an 11 kHz mono dummy DS3D buffer, only to reach an
`IKsPropertySet`. `FUN_10a4a4c0` then asks it whether the EAX 4 property sets `EAXPROPERTYID_EAX40_FXSlot0`
(`c4d79f1e-f1ac-436b-a81d-a738e7045469`) and `EAXPROPERTYID_EAX40_Source`
(`1b86b823-22df-4eae-8b3c-1278ce544227`) support both get and set. Only then does it set the EAX flag
(renderer `+0x168`) and load the reverb. Every EAX write checks that flag first.

The `bin\eax.dll` that ships with the game is Creative's "EAX Unified" 3.062. Its
`EAXDirectSoundCreate8` is a COM shim: it looks up and instantiates the `EAXUnified8` class that
Creative's drivers register. On a PC without them it fails, DARE falls back to plain
`DirectSoundCreate8`, and Windows' software DirectSound answers no to the EAX query **(inferred for the
shim failing; the fallback and the probe are traced)**.

## Occlusion

### Buildings are zones

`CSoundOcclusionManager` holds the zones. `CBuildingInfoComponent::RegisterSoundOcclusion`
(`0x08b9ec00`) registers each building as a zone with its parameters (`SZoneParam`):

- `fOcclusionVolume` and `fOcclusionFilter`,
- `sndReverb` and `fEchoLength`,
- `sndEnter`/`sndExit` sounds.

It also registers the building's entrances as **holes**, each carrying `fSoundOcclusionVolume`,
`fSoundOcclusionFilter` and `fSoundRange` from `CEntranceInfoComponent`, and the links between connected
buildings.

Retail's values **(seen in data)**:

- **Buildings** (623 placed): `fOcclusionVolume` is mostly 0.3–0.7. `fOcclusionFilter` is 0 on 391 of
  them and 0.01 or less on most of the rest; only 27 go to 0.3 or higher. `fEchoLength` is 0 on 620.
- **Entrances** (1,967 placed): `fSoundOcclusionVolume` is 0.2 on about half, and
  `fSoundOcclusionFilter` is 0 on 97%.

The small filter values are not small in effect. Because of the cutoff curve, 0.01 is a low-pass near
1.2 kHz.

`CSoundOcclusionManager::Update` (`0x099bb5d0`) runs every 0.25 s from the listener's position:

1. `GetOcclusionFactor` finds the listener's zone and the closest hole. Near a hole it blends the
   zone's volume and filter toward the hole's by distance.
2. On a zone change it plays the old zone's exit sounds and the new zone's enter sounds, and takes the
   new zone's reverb and echo length.
3. It moves the current values toward the targets at a fixed rate, the fade time from the sound
   config's `occfade`.
4. `UpdateMix` writes them into the occlusion mix preset as a volume in dB (doubled) and a filter
   amount, for the sound types that preset lists. The sound regions name
   `Compatible.VolumeLinesToLowerInsideForOcclusion`, which lists only the outdoor ambience types 0, 23
   and 24.

### From zone to filter, per sound

At start-up `FUN_10622d70` registers each sound type with DARE. A type flagged `occlusion` in
`soundconfig.xml` gets an occlusion callback, `FUN_10621950` → `FUN_10621880`. DARE calls it for every
sound of that type, and it writes one number into the voice's obstruction **(RE-verified)**:

```
obstruction = clamp((1 − objectFactor × passThrough[type]) × occmul_pc, 0, 1)
```

- `passThrough[type]` is what `CMixingManager::Update` keeps per type: (1 − the absolute preset's
  filter) × (1 − the strongest relative preset's filter). With no filtering preset it is 1.
- `objectFactor` comes from the sound object's `GetOcclusionFactor`. For the default callbacks it is
  1 − `fOcclusionFilter` of the building the source stands in, blended toward the nearest hole's value
  near a door, and 1 for a source outside every building. It is re-evaluated every 0.5 s when the source
  has moved, adjusted by `ApplyListenerFactor` for whether the listener shares the zone, and faded over
  `occfade` seconds **(RE-verified in the server build, which carries the symbols; the PC callback calls
  the same interface slot)**.
- `occmul_pc` is 1.0. `LoadConfigFile` (`0x106233b0`) stores it at `CSoundSystem+0x24`, where this
  callback reads it. The consoles' 50.0 turns any non-zero amount into full obstruction.

The result drives only the low-pass. The callback never sets occlusion materials, and obstruction never
lowers the volume. `CSoundSystem::ComputeOcclusion` is a function of its own in the server build; in
`Dunia.dll` it is inlined into this callback.

What that means in retail:

- A sound outdoors, with no filtering preset active, gets obstruction 0: no filter.
- A gunshot from inside a building with a non-zero `fOcclusionFilter` is low-passed. At 0.01 the cutoff
  is near 1.2 kHz.
- A wall never makes a gunshot quieter. `fOcclusionVolume` reaches only the types in the occlusion mix
  preset, the outdoor ambience types 0, 23 and 24.
- `Exclusive.Underwater` sets filter 0.8 on the types it lists, so their obstruction becomes 0.8 and the
  cutoff about 90 Hz.

Two outdoor points are never occluded from each other, however much rock lies between them.

:::info[Heard in game (GOG, 2026-09-25)]
Under water, NPC gunfire, voice lines and explosions are inaudible. Removing only the preset from the
console while still under water (`#StopSoundMixingFromLua("Exclusive.Underwater")`) brings them back.
The preset sets voices to 0 dB, so the silence is the low-pass: the path above works on a PC without
EAX.
:::

### The occlusion is cached per sound object

DARE does not ask the callback every frame. Each frame, every playing instance reads its occlusion
through `DARE_Object_QueryOcclusion` (`FUN_10a53750`, from `FUN_10a3d2e0` → `FUN_10a3cc60` →
`FUN_10a3c4d0`). That calls the game only when the object's dirty bit (`+0x20 & 2`) is set, and clears
it. Otherwise it returns the stored block **(RE-verified)**. The bit is set when the object is created,
by vtable slot `+8` (`0x10a537d0`); what else sets it is not traced.

In game, a preset applied from the console above water left an already-running vehicle engine
unchanged, while it lowered the ambience's volume at once **(heard in game)**. That fits a cache that is
refreshed only for new sound objects **(inferred)**. Volume goes through `SetTypeVolume` and applies
immediately.

`fOcclusionEvaluatorWeight` on `CAISoundAndFXComponent` is not audio. It is one of the AI **vision**
weights (distance, FOV, vegetation, stance, speed, ambient light) in the visibility evaluator.

## Reverb

The whole path:

1. A reverb source picks a reverb event: a building zone's `sndReverb`, a sound region's
   `sndReverb` (`SSoundRegionLevel`), or a mix preset's `sndReverb` (`CMixingManager::ComputeReverb`
   takes the first active preset that has one).
2. `CSoundSystem::PlaySoundReverb` plays it.
3. It is a type-`8` event, so DARE's dispatcher calls `FUN_10a31bf0` → `FUN_10a510a0` → the
   renderer's reverb slot (vtable `+0x90`, `FUN_10a4bf50`).
4. `FUN_10a4bf50` looks the reverb effect up and hands its `EAXREVERBPROPERTIES` to `FUN_10a49a40`,
   which does nothing unless EAX was detected.

There is no second path. On a PC without EAX the reverb is silent, and so are DARE's EAX occlusion,
obstruction and room sends. The software filters are not affected.

The reverb effects are not records in any `.spk` bank. They are 63 presets in EAX form inside
`common/soundbinary/7fffffff.bao`, the DARE project descriptor, and retail's 17 distinct type-`8`
events each point at one. The game was built to reverberate almost everywhere:

| Where | Reverb event → preset | Character |
| --- | --- | --- |
| Most buildings (395 placed) | `0x004BE3E6` → `0x004BE3C2` | small room, 0.4 s decay (≈ EAX *Room*) |
| Shanties and similar (135) | `0x004BE3E4` → `0x004BE3C0` | 2.5 s decay, dark (RoomHF −1500) |
| Hangars and warehouses (7) | `0x004BE3E0` → `0x004BE3BC` | 6.8 s decay, size 50 (≈ EAX *Hangar*) |
| 2 placed buildings | `0x004BE3E9` → `0x004BE3C5` | 5 s decay, size 50 |
| Desert, every intensity level | `0x004B19C9` → `0x004B19CF` | 1.0 s decay, echo depth 1.0 |
| Jungle | `0x004B19C4`…`C8` | 1.6–3.0 s decay, rising with intensity |
| Savannah | `0x004B19BF`…`C2` | 0.6–2.0 s decay |
| A region's `sndNoReverb` | `0x004B19E8` → `0x004B19E9` | reflections and reverb at −10000 (off) |

The outdoor reverbs come from `common/soundregions.xml`. Each biome region has levels keyed by an
intensity; the [`.srl`](../file-formats/srl-zsr.md) grid appears to store `(intensity << 4) | region`
per cell **(seen in data: every low nibble sampled is 0–6, the seven regions; not traced in code)**.
Each level carries an `fEchoLenght` (1.2–6.1 s, 0 at intensity 0) and a `sndReverb`. 84 placed buildings
have no reverb. No retail mix preset sets one.

## Weapon sounds

A bullet weapon's sound fields live on `CWeaponFireBulletProperties` (vtable `0x10e1c4d8`, constructor
`FUN_100fa620`; the server's offsets are Dunia's minus `0x60`). They are played by
`CWeaponFireBulletStrategy` (vtable `0x10e1ea18`):

- `ApplyDelayBullet` (`FUN_10115680`) handles each shot's result and plays the single-shot sounds.
- `StartUse` (`FUN_10113580`) and `PreStopUse` (`0x1010f210`) handle automatic fire.

Every play call passes a volume in dB, which is 0 on these paths. None passes a delay.

In the data these fields sit under `CWeaponProperties` → `FireStrategyProperties/Sounds` (105
`WeaponProperties.*` archetypes, 91 with a bullet `Sounds` block). First-person events use sound type 8
and third-person events type 9 on every weapon.

:::caution[Most of the machinery below is dormant in retail]
`sndSingleBulletShotEcho`, `sndStartAutoBulletShotEcho`, `sndStopAutoBulletShotEcho`, `sndPassByWizz`
and `sndmlDistanceFromShootingSoundToPlayerMultilayer` are `0xFFFFFFFF` on all 91 weapons, DLC
included. The shipped game has no gunshot echo, no near-impact wizz and no distance layering. The
AK47's third-person auto shot, for example, is a single mono clip. The code paths are live and wait for
data.
:::

### The player's shot and its echo

For the local player's weapon, `ApplyDelayBullet` plays `sndSingleBulletShot` on an emitter that reports
no position, so the player's own shot is effectively unpositioned **(inferred from the emitter's
default position)**. At the same moment it plays `sndSingleBulletShotEcho` at the shot's origin, which is
the listener, and immediately calls `StopSound(echo, GetEchoLength())`.

The echo starts with the shot, with no delay. How long its tail lasts is set by the listener's echo
length: the sound region's `fEchoLenght`, blended with the building zone's `fEchoLength` when the
listener is inside one. That time is passed to DARE's stop call; it reads as a fade-out time
**(inferred)**. Nothing about the terrain around the player enters it.

Automatic fire does the same with a loop:

- `StartUse` starts `sndStartAutoBulletShot` and `sndStartAutoBulletShotEcho`.
- `PreStopUse` stops the loop with a fade: `fAutoBulletShotFadeOutMultiplier` × the time left to the
  next round, but at least `fAutoBulletShotMinFadeOut`. It then plays `sndStopAutoBulletShot`, and
  plays `sndStopAutoBulletShotEcho` trimmed by the echo length.

### Everyone else's shot

A shot from an NPC, or from any weapon not held by the local player, plays the `ThirdPerson` group's
events instead. They play in 3D at the weapon entity, through an emitter (`CFireBulletCB`) that DARE
queries each update. **An NPC's shot never plays an echo.**

That emitter is also how the far sound works. `FUN_1010fa10` gives it the weapon's
`sndmlDistanceFromShootingSoundToPlayerMultilayer` id. When DARE asks for that multilayer's value,
`CFireBulletCB::GetMultiLayer` (`0x1010eff0`) returns the distance from the weapon to the local player,
in metres. The layers, and the distances they fade across, belong to a DARE
[multilayer resource](../file-formats/spk.md#resource-containers), not to game code, and the code has no
distance thresholds. Retail sets the field on no weapon. The game parameter `0x00440259`, declared with
a range of 0–250 and used by nothing, looks like the one it was meant for **(inferred)**.
`sndswtpCloseFarSoundSwitchType` and `sndswvlCloseSoundSwitchValue` do not exist in retail.

The only switch the shot path feeds is the surface material. The player's shot emitter answers the
material switch by casting straight down from the player (`CFireBulletShellCB::GetSwitch`,
`FUN_10111200`). That matches the data: a first-person single shot is a multi-event of the weapon's
sample and a shared material-switched layer (`0x004565A6`, 8 surface groups of 5 variants). That layer
is the shell casings landing **(inferred)**.

### Fly-bys

What you hear when a bullet passes is the player's, not the weapon's.

`SendShotStimsAndEvents` sends every primary bullet's segment to each local player who is neither the
shooter nor the target. `CPlayerSoundAndFXComponent::OnEvent` tests it against the head and gives up if:

- the segment ends short of the head,
- it hits within `fMinImpactDistance` of the head,
- or it passes further than `fNormalRadius` away.

Otherwise it builds a moving source through the closest point, `tan(fAngle/2) × fNormalRadius` either
side along the bullet's direction, and moves it along that path over `fSoundDuration`. It reports a
velocity, so DARE applies Doppler. `PlayBulletPassBySound` (`FUN_106cf9c0`) plays `sndPassByNearSound`
within `fSmallerRadius`, `sndPassByRegularSound` beyond it, or `sndPassByUnderwaterSound` when the
listener is underwater.

The constructor (`FUN_106d1440`) defaults are `fNormalRadius` 4, `fSmallerRadius` 2, `fAngle` 90,
`fSoundDuration` 0.5 and `fMinImpactDistance` 5. The single-player character overrides them with 2.7,
0.6, 1.25, 0.5 and 5 **(seen in data)**. How `fAngle` is converted before the tangent is not traced.
The retail sounds are `sndPassByRegularSound` `0x00448BD2` (a random container of 12 variations),
`sndPassByNearSound` `0x00448BC8` and `sndPassByUnderwaterSound` `0x0045546A`, all type 9. The choice
between them is geometric; only the regular sound picks a variation at random.

The weapon's own `sndPassByWizz` belongs to a different path, in `SpawnBulletPuff` (`FUN_100faab0`).
It only runs when three things hold:

- the field is set,
- the bullet is not the local player's,
- it lands above water within 5 m of a local player (a hard-coded, unregistered field at `+0x4a8`,
  default 5.0).

When it runs, it plays a sequence instead of the plain impact: the wizz, then the impact
`fPassByWizzTiming` (0.15 s) later.

In retail the field is `0xFFFFFFFF` on every weapon, so the branch never runs. Close impacts take the
normal call, at volume 1.0 (0 dB), like any other impact **(RE-verified)**.

:::warning[Filling `sndPassByWizz` alone makes close impacts silent]
The sequence call passes a volume of `0.0` (`0x100faea0`) where the plain impact passes `1.0`.
`SpawnSound` (`FUN_10511bc0`) runs it through `ConvertPercentToDecibels`, which returns the −96 dB
floor for anything at or below 0. A mod that sets `sndPassByWizz` would therefore mute both the wizz and
the impact for every NPC round landing within 5 m of the player, unless that `0.0` is patched to `1.0`.
Traced, not heard.
:::

### Impacts

A `matimp…` field references a `CMaterialImpactFx` record in
`common/databases/materialimpacts/materialimpacts.xml` (64 records), with one `sndSwitchSound`, its sound
type, and a particle system and decal for each of 39 logic materials. `CMaterialImpactFx::Execute`
(`FUN_10511d50`) looks up the material that was hit and plays that one event at the impact point. The
material's `sndswvlSoundSwitchValue` (`databases/materials/logicmaterials.xml`) is the answer for the
switch type `sndswtpMaterialSoundSwitchType` (`0x00440260`), so the surface selects the sound through a
DARE switch.

For bullets (`Weapon.Bullet` on 87 of the 91 weapons) that event is `0x004565A3`. It plays a material
switch resource with 36 entries, each a random container of 2–7 clips: flesh, several metals, glass,
water and so on. The shell-casing and explosion impact records have no sound.

An NPC's bullet that hits the player plays particles and a decal but no impact sound. Shell casings play
`matimpShellImpactFx` on the ground under the shooter.

There is **no ricochet** anywhere: no event, field, string or code. The only bounce asset,
`matimpBounceImpactFx`, belongs to thrown objects.

### Hit confirmation

`sndHitPlayerSound` and `sndHitPlayerHeadSound` are the player's hit markers, not sounds of the player
being hit. `ApplyDelayBullet` plays one, unpositioned, on the first pawn a local player's shot hits:
the head sound when the hit location is the head, the other sound otherwise. Retail sets them only on the
18 multiplayer (`.Multi`) weapon archetypes.

## Mixing

### Sound types

`config/soundconfig.xml` defines 25 **sound types**, the categories everything is mixed by. All of them
go to one line, `Master`. A type flagged `positioning` is 3D; a type flagged `occlusion` takes
occlusion.

| Id | Type | 3D | Occlusion |
| --- | --- | --- | --- |
| 0 | `Ambiance_Generic` | yes | |
| 1 | `Ambiance_Inside` | yes | yes |
| 2–5 | `Dialog`, `Buddy_Dialog`, `Bark_NPC`, `Ono_NPC` | yes | yes |
| 6–8 | `Ono_Player`, `Breath_Player`, `Weapon_Player` | | |
| 9 | `Weapon_NPC` | yes | yes |
| 10, 11 | `Explosion`, `Effect_3D` | yes | yes |
| 12 | `Effect_Non_Loc` | | |
| 13, 14 | `Vehicles`, `Foley_NPC` | yes | yes |
| 15–18 | `Foley_Player`, `Music_Breifing`, `Music_InGame`, `Music_Memorable` | | |
| 19 | `Radio` | yes | yes |
| 20 | `Interface` | | |
| 21 | `animalsounds` | yes | yes |
| 22 | `Infamy_FX` | | |
| 23, 24 | `Ambiance_River`, `Ambiance_Followers` | yes | yes |

`CSoundSystem` keeps a volume per type (`SetTypeVolume`, vtable `+0x68`) and can mute, pause or
inhibit a type.

### Mix presets

`CMixingManager` applies snapshot presets from `databases/soundmixing/soundmixings.xml` (37 in retail).
A preset holds:

- `bAbsolute`, `uiProprity` (spelled so in the data), `fTransitionIn`, `fTransitionOut` and `fDuration`
  (0 means until removed).
- `sndStart`/`sndStop` events and a `sndReverb`.
- A `MixingSet` of `Mix` entries, each with a `sndtpType`, an `fVolume` in dB and an `fOcclusion` filter
  amount from 0 to 1. Every retail set ends with a `sndtpType="-1" fVolume="-96"` row. The update only
  reads entries whose type index is in range, so that row has no effect.

Entity fields name a preset by the CRC of its `hidName` (`crc_hidName` in the XML).

`CMixingManager::Update` (`0x099b4d00`) combines them every frame:

- **Absolute** presets form a stack ordered by priority. The top one wins, blended with the one below
  it by its fade ratio (`GetAbsoluteValues`, `ComputeRatio`).
- **Relative** presets add on top. For each type the lowest volume and the strongest filter win.
- It sends each type's summed volume, clamped to −96…0 dB, to `SetTypeVolume`, and keeps each type's
  filter for `ComputeOcclusion`.

`ApplyPreset(name, duration)` starts or extends one and plays its `sndStart`. The Domino box
`SoundMixing` (`StartSoundMixingFromLua`/`StopSoundMixingFromLua`) lets a mission script do the same.
Both are global Lua functions, so the game's console reaches them through its `#` Lua prefix. That is
a quick way to audition a preset in game:

```
#StartSoundMixingFromLua("Exclusive.Underwater")
#StopSoundMixingFromLua("Exclusive.Underwater")
```

The name is the preset's `hidName`. A preset started this way stays until it is stopped.

Retail uses this for its big moments:

- `Exclusive.Ear_Ringing` drops every type but explosions to −96 dB with filter 0.8.
- `Exclusive.Underwater` filters most types at 0.8.
- `Exclusive.Phone_Call` ducks the world 6–12 dB.
- `Compatible.In_Vehicles` lowers the ambience types by 12 dB.

The player component wires presets to its state **(seen in data)**:

| Field on the SP character | Preset | Contents |
| --- | --- | --- |
| `mixLowHealthPreset` | `Compatible.Health_Low` | **empty** |
| `mixMediumHealthPreset` | `Compatible.Health_Medium` | **empty** |
| `mixHealthFailurePreset` | `Exclusive.Health_Critical` | **empty** |
| `mixLowStaminaPreset`, `mixMediumStaminaPreset` | `Compatible.Stamina_Low`, `…_Medium` | **empty** |
| `mixReduceSoundPreset` | `Exclusive.Healing` | **empty** |
| `mixUnderwaterSoundPreset` | `Exclusive.Underwater` | 0 to −15 dB and filter 0.8 on 13 types, among them dialog, vehicles, NPC weapons and explosions |
| `mixRunningPreset`, `mixCrouchedPreset`, `mixIronsightPreset` | none | — |

So the game switches presets on low health, stamina and healing, but in retail those presets change
nothing. `Compatible.Ironsight` and `Compatible.Running` exist in the library but no field points at them.

## The rest of the game layer

- **`CAmbianceManager`**: sound regions ([`.srl`](../file-formats/srl-zsr.md)), their ambience,
  the wind, rain and altitude multilayers, random one-shots, `sndNoReverb`, and the occlusion mix preset
  (`mixOcclusionMixing`). `GetEchoLength` (`0x09645c00`) blends the region's `fEchoLenght` (sic) with the
  listener's building zone's `fEchoLength`.
- **`CMusicManager`** is told about combat through `RecordBulletShot`, `RecordBulletPassBy`,
  `RecordBulletHit` and `RecordExplosion`, and keeps a danger level **(inferred from the names)**.
- **`CPlayerSoundAndFXComponent`**: player footsteps, damage sounds, the underwater ambience and
  `PlayBulletPassBySound`.
- **`CBarkManagerService`** picks AI barks from the `scripts\game\BarkData` banks. A bark is a
  `Bark_NPC` sound, a positioned type with occlusion, so it gets the same curves and zone occlusion as
  any other 3D sound **(inferred: the bark play call is not traced)**.
- **`CWaterSoundManager`**: splashes, with switch values for object size, speed and water depth.

## Configuration

| File | Section / element | What it sets |
| --- | --- | --- |
| `Data_Win32\SoundBinary\DARE.INI` | `[Sound Manager Options]` `NB_AUDIBLE_VOICES` | voice cap and pool size (retail 64) |
| | `[Renderer DS3D Options]` `DS3D_CACHE_SIZE` | a 2 MB cache the renderer allocates |
| | `LAZY_SOUND_BUFFER_POOL` | `FALSE` pre-creates the pools (retail); the code default is `TRUE` |
| | `NO_OPTIMISATION_SOUND_CARDS` | devices that get no `LOCDEFER` (retail: `SigmaTel Audio`) |
| | `[Renderer options]` `Output frequency` | not in retail's INI; code default 44,100 |
| `config/soundconfig.xml` | `SoundProject` | `bindir`, `occfade`, `occmul_*` |
| | `SoundTypes`, `SoundLines` | the types above |
| `databases/soundmixing/soundmixings.xml` | `SoundMixing` | the presets above |
| `soundregions.xml` | `SoundRegion` levels | per-biome ambience, `fEchoLenght`, `sndReverb`, the occlusion preset |
| `databases/materialimpacts/materialimpacts.xml` | `MaterialImpact` | impact events, particles and decals |
| `databases/materials/logicmaterials.xml` | material settings | the material switch group and each material's switch value |
| `soundbinary/7fffffff.bao` | DARE project descriptor | reverb presets, the obstruction cutoff range, multitrack channels, game-parameter ranges |
| `soundbinary/2fffffff.spk` | rolloff pack | all 96 rolloff curves |
| `engine\settings\DefaultSoundConfig.xml`, `OverrideSoundConfig.xml` | `CSoundConfig` | the player's sound options |

The XML and bank paths are under `common/` in the extracted data.

## Open

- Whether DS3D's own distance rolloff is neutralised.
- What `ApplyListenerFactor` returns when the listener is in a building and the source is not.
- What, besides creating a sound object, marks its cached occlusion dirty.
- The fourth private effect.
- How `fAngle` is converted in the fly-by.
