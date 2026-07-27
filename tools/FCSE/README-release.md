# FCSE (Far Cry Script Extender)

An SKSE-style DLL plugin loader for Far Cry 2 - lets any number of third-party plugin DLLs change
engine behavior that normally only lives in `Dunia.dll` itself, without needing to ship (or fight
over) a shared patched copy of that file.

## Installing

1. Copy `FCSE.exe` into the game's `bin\` folder, next to the existing `FarCry2.exe` - that file
   is left completely untouched; `FCSE.exe` is an additional way to launch the game, not a
   replacement.
2. Drop plugin `.dll` files into `bin\plugins\` (created automatically the first time you launch
   `FCSE.exe` if it doesn't exist yet).
3. Launch `FCSE.exe` instead of `FarCry2.exe`.

Check `bin\fcse.log` afterward to confirm what happened - it records every plugin found/loaded,
what each one registered/hooked/patched, and any conflicts between plugins.

## Writing your own plugin

See the separate `fcse-example-plugin` download for a minimal, complete, working plugin (source +
compiled DLL) to start from, and the full writeup at https://jbebe.github.io/farcry-sdk/fcse for
how the plugin API works and what you can do with it.
