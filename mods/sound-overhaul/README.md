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
- **Lets an older or Wine-derived `dsound.dll` open a device at all** (`src/enumeration_fix.cpp`).
  The bundled DSOAL does not need it.
- **Logs every reverb switch to `fcse.log`** (`src/reverb_log.cpp`), a diagnostic that goes before a
  release.

## Layout

```
layer\plugins\sound-overhaul\
├─ SoundOverhaul.dll          built, gitignored
└─ dsoal\
   ├─ dsound.dll              DSOAL, Win32
   ├─ dsoal-aldrv.dll         OpenAL Soft, renamed as DSOAL expects
   ├─ alsoft.ini              OpenAL Soft's settings: plain stereo, so headphones get no HRTF
   └─ Documentation\          licenses and exact versions
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

`bin\fcse.log` reports both parts. To see DSOAL's own calls, launch with `DSOAL_LOGLEVEL=3` and
`DSOAL_LOGFILE=<path>` set; OpenAL Soft reads `ALSOFT_LOGLEVEL` and `ALSOFT_LOGFILE`.
