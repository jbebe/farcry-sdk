# Loose file mods

This lets you change Far Cry 2's game files without repacking `.fat`/`.dat` archives. Whatever
you put here overrides the matching file in the game's normal data.

## Where this folder goes

Copy this whole `Loose` folder into your game's `Data_Win32` folder, so you end up with:

```
Far Cry 2\
  bin\
    FarCry2.exe
    dinput8.dll          <- from ModPatcher, see the main README
  Data_Win32\
    patch.dat
    common.dat
    worlds\
    Loose\               <- this folder
      README.md           (this file)
      ...your mod files...
```

## How to add a mod file

1. Unpack the archive that contains the file you want to change, using Gibbed's tools exactly
   like you normally would (`Gibbed.Dunia.Unpack.exe`, drag `patch.fat` or whichever archive onto
   it). This gives you a folder like `patch_unpack\`.
2. Edit the file inside `patch_unpack\` as usual.
3. **Instead of repacking**, copy the file (keeping its folder structure) straight from
   `patch_unpack\` into this `Loose` folder — drop the `patch_unpack\` part, keep everything after
   it.

Example: if you edited `patch_unpack\generated\EntityLibraryPatchOverride.fcb`, it goes in this
folder as:

```
Data_Win32\Loose\generated\EntityLibraryPatchOverride.fcb
```

Same idea no matter which archive the file came from (`worlds_unpack\`, `common_unpack\`, etc.) —
whatever comes after the `..._unpack\` part is the folder structure you recreate here.

## Checking it worked

Look at `bin\modpatcher.log` after launching the game. Every asset the game loads gets a line; if
your file was picked up you'll see a line like:

```
[VFS] override HIT  worlds/world1/generated/entitylibrary.fcb -> C:\...\Data_Win32\Loose\worlds\world1\generated\entitylibrary.fcb
```

If you don't see a HIT line for your file, double check the folder path matches exactly (typos in
the folder structure are the most common reason a file gets silently ignored).

## Known limitation right now

Only one version of each file is supported — if two different mods both want to change the same
file, whichever one is actually sitting in this folder wins, there's no way to combine them yet.
