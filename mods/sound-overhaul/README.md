# Sound Overhaul

An FCSE plugin toward Far Cry 2 sounding like a serious shooter. The plan, the verdict per target and
the engine findings behind it are in the design log, `docs/docs/design/realistic-sound.md`, and on
the audio runtime page, `docs/docs/engine-internals/audio-runtime.md`.

## What it does

- **Brings back the authored reverb, through DSOAL.** The game's reverb and EAX occlusion only play
  on an EAX 4 device, which a modern PC does not have. [DSOAL](https://github.com/kcat/dsoal)
  provides one in software. The plugin loads the DSOAL shipped in `dsoal\` beside it and points
  `Dunia.dll`'s DirectSound imports at it, `EAX.DLL`'s `EAXDirectSoundCreate8` included: without
  Creative's drivers the shipped `eax.dll` would hand the game a Windows DirectSound instead
  (`src/dsoal.cpp`). Nothing is copied into `bin\`, and uninstalling the plugin removes it. A
  DirectSound that is already replaced, such as a `dsound.dll` in `bin\`, is left in charge.
- **Switches the reverb as the player moves** (`src/reverb.cpp`). `CSoundSystem::PlaySoundReverb`
  ships empty, so buildings, regions and mix presets never reached DARE and one reverb played
  everywhere. The plugin gives it a body that plays the reverb event.
- **Gives every building a reverb that fits it** (`src/rooms.cpp` and the `00fc090*` banks). Retail
  picked one of four by hand, so a bus rang for 2.5 s and 84 buildings had none. As each building loads,
  the plugin picks by its structure type and size: small, medium and large rooms, a hall, a hangar and a
  bright metal box, on presets retail authored but never used. Its walls also muffle sound from outside by
  material, from concrete and mud down to a bus's open windows; buildings retail muffled harder, such as
  the armories, keep theirs. Open doors and windows reach into the room by their size, about 4 m for a
  window, so a house with its windows open is barely muffled inside while a closed one stays walled off.
- **Can turn combat music off** (`src/combat_music.cpp`). A *Combat music* Yes/No setting, on Sound
  Overhaul's page of the Mod Configuration Menu and in `bin\fcse.ini`. With it off, the chase, battle,
  fight and suspense states still take over from the calm music but play nothing, so enemies can be heard;
  the calm music returns when the fight ends. The game's own Music setting still turns all music off.
- **Lets the player's own sounds take the room** (`src/player_reverb.cpp`). DARE gives every voice
  the same reverb send, which the player's shots and reloads, playing at full volume, bury under
  their dry sound. The plugin raises the send of 2D voices by 1,000 mB, the most EAX allows.
- **Gives the Makarov a dry first-person shot and an outdoor echo** (`layer\mods\soundbinary\004569c9.spk`
  and the `WeaponProperties.Secondary.Makarov` fragments under `layer\mods\worlds\`). Indoors the room
  reverb supplies the tail. Outdoors the weapon's dormant `sndSingleBulletShotEcho` plays the rest of
  the recording, trimmed by the game to the region's echo length.
- **Lets an older or Wine-derived `dsound.dll` open a device at all** (`src/enumeration_fix.cpp`).
  The bundled DSOAL does not need it.

## Layout

```
layer\
├─ plugins\sound-overhaul\
│  ├─ SoundOverhaul.dll       built, gitignored
│  └─ dsoal\
│     ├─ dsound.dll           DSOAL, Win32
│     ├─ dsoal-aldrv.dll      OpenAL Soft, renamed as DSOAL expects
│     ├─ alsoft.ini           OpenAL Soft's settings: plain stereo, so headphones get no HRTF
│     └─ Documentation\       licenses and exact versions
└─ mods\
   ├─ soundbinary\            sound banks replacing retail ones
   └─ worlds\world1|world2\   weapon archetype fragments pointing at the echo events
assets\weapon-sounds\         the recordings the banks are made from
```

A bank is the retail one with its audio swapped, which also rewrites the length the game plays to. An
echo adds the shot's event, sample and audio records again under new ids, the audio imported the same
way:

```
jackall-cli spk import 004569c9.spk 0x004bf596 assets\weapon-sounds\makarov\close_1_trimmed.wav
```

FCSE also offers the two DLLs in `dsoal\` as plugins and logs them as skipped; that is expected.

## The bundled DSOAL

Build r695 from DSOAL's [build archive](https://github.com/kcat/dsoal/releases/tag/archive),
`DSOAL_r695.zip`, folder `DSOAL\Win32`. The exact DSOAL and OpenAL Soft commits are in
`dsoal\Documentation\DSOAL-Version.txt` and `OpenALSoft-Version.txt`. Both are LGPL-2.1, and their
license texts ship beside them. To update, replace `dsoal\` from a newer archive build.

## Build and install

```
.\build.ps1                                   # x86 release, staged into layer\plugins\sound-overhaul\
.\build.ps1 -Install "C:\Games\Far Cry 2\bin"   # copies it to bin\plugins\sound-overhaul\
```

The sound banks go into `patch.dat`, together with any other layer you play with:

```
jackall-cli mod build --game "C:\Games\Far Cry 2" --layer layer
```

`bin\fcse.log` reports both parts. To see DSOAL's own calls, launch with `DSOAL_LOGLEVEL=3` and
`DSOAL_LOGFILE=<path>` set; OpenAL Soft reads `ALSOFT_LOGLEVEL` and `ALSOFT_LOGFILE`.
