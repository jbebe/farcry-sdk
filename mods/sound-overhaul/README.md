# Sound Overhaul

An FCSE plugin toward Far Cry 2 sounding like a serious shooter. The plan, the verdict per target and
the engine findings behind it are in the design log, `docs/docs/design/realistic-sound.md`, and on
the audio runtime page, `docs/docs/engine-internals/audio-runtime.md`.

## What it does

- **Brings back the authored reverb, through DSOAL.** The game's reverb and EAX occlusion only play
  on an EAX 4 device, which a modern PC does not have. [DSOAL](https://github.com/kcat/dsoal)
  provides one in software. The plugin loads the DSOAL shipped in `dsoal\` beside it and points
  `Dunia.dll`'s two DirectSound imports at it (`src/dsoal.cpp`), so nothing is copied into `bin\`
  and uninstalling the plugin removes it. A `dsound.dll` already in `bin\` is left in charge.
- **The enumeration fix.** DARE continues its device setup only if `DirectSoundEnumerateA` returns
  exactly 0, and DSOAL builds before r689 return `S_FALSE`, so the game started with no sound behind
  a "Sound-Driver is currently used by an other application" box. One branch byte accepts any
  success code (`src/enumeration_fix.cpp`). The bundled DSOAL does not need it; it keeps any older
  or Wine-derived `dsound.dll` working.

## Layout

```
layer\plugins\sound-overhaul\
├─ SoundOverhaul.dll          built, gitignored
└─ dsoal\
   ├─ dsound.dll              DSOAL, Win32
   ├─ dsoal-aldrv.dll         OpenAL Soft, renamed as DSOAL expects
   └─ Documentation\          licenses and exact versions
```

FCSE loads a folder's own files before its subfolders, so the plugin loads DSOAL before FCSE offers
`dsoal\*.dll` as plugins; FCSE finds no `FCSE_Load` there and logs them as skipped.

## The bundled DSOAL

Build `r695` from DSOAL's [build archive](https://github.com/kcat/dsoal/releases/tag/archive),
`DSOAL_r695.zip`, folder `DSOAL\Win32`:

- DSOAL `d9ffca874fc2e65a1df06581638eec9e9169c3d1` (2026-09-23)
- OpenAL Soft `d05da32974425708940f650fc2b19ffd9a4936ac` (r10717, 2026-09-23)

Both are LGPL-2.1; the license texts ship in `dsoal\Documentation\`, and the source is at those
commits. To update, replace the two DLLs and `Documentation\` from a newer archive build, and the
commits above.

## Build and install

```
.\build.ps1                                   # x86 release, staged into layer\plugins\sound-overhaul\
.\build.ps1 -Install "C:\Games\Far Cry 2\bin"   # copies it to bin\plugins\sound-overhaul\
```

`bin\fcse.log` reports both parts. To see DSOAL's own calls, launch with `DSOAL_LOGLEVEL=3` and
`DSOAL_LOGFILE=<path>` set; OpenAL Soft reads `ALSOFT_LOGLEVEL` and `ALSOFT_LOGFILE`.
