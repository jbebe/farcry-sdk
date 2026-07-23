**Author's note:** This is just a proof of concept. It works but we chose the [patch.dat path](https://jbebe.github.io/farcry-sdk/docs/file-formats/archives-fat-dat) instead of this.
<br>
<br>
<br>
<br>
<br>
<br>
<br>
# ModPatcher (Project 1: loose-file asset loading)

Lets Far Cry 2 load assets from a loose `Data_Win32\Loose\` folder instead of only from the
packed `patch.fat`/`patch.dat`-style archives, similar to STALKER's `gamedata` folder or Skyrim's
loose-file override. Background and the full reverse-engineering derivation live in
[`reverse/dunia/archive_loading.md`](../../reverse/dunia/archive_loading.md).

This README is for people building/maintaining the tool itself. **If you just want to install a
mod or make one, see [`Loose/README.md`](Loose/README.md) instead** — it skips all of this and
just explains where files go.

## How it works

Ships as `dinput8.dll`. `Dunia.dll` statically imports `DINPUT8.dll` (confirmed via its own
import-name string table), so this DLL loads and its `DllMain` runs before any of `Dunia.dll`'s
own code executes — before `InitDuniaEngine`, before the file-system bootstrap, before anything.

All 6 real `DINPUT8.dll` exports are forwarded to the real system DLL, resolved at load time via
an explicit, absolute `GetSystemDirectoryW()`-based path (not a bare `LoadLibraryW(L"dinput8.dll")`
— that would search our own directory first, per standard DLL search order, and just re-resolve to
ourselves since we share the filename; no bundled/renamed copy needed either, same technique
ENB/SKSE-style loaders use). `GetSystemDirectoryW`, called from inside this 32-bit DLL, is
automatically WOW64-redirected to `SysWOW64` on 64-bit Windows, so it always resolves to the
correct-architecture copy.

MSVC's linker turns out not to support true cross-DLL PE export forwarding from source at all
(neither a `.def` file's `Name=Module.Symbol` syntax nor `/EXPORT:Name=Module.Symbol` resolve —
both were tried and both fail to link), so the actual forwarding is done via
`LoadLibrary`/`GetProcAddress` at load time (`proxy_exports.cpp`) plus a naked `JMP`-thunk-per-export
aliased onto the public export names in `CMakeLists.txt` (`/EXPORT:Public=ThisName`, undecorated
internal name) — a raw `JMP` preserves the original caller's stack frame, so it works regardless of
each export's actual calling convention/signature.

The only thing this DLL adds on top of that: an inline hook on `Dunia.dll`'s `VFS_ResolvePath`,
installed via [MinHook](https://github.com/TsudaKageyu/minhook) (vendored as raw source via
`FetchContent` and built as our own CMake target — upstream ships only Visual Studio project
files, no CMake support).

The hook checks whether a requested relative asset path exists under `Data_Win32\Loose\` (install
root computed by going up one directory from wherever this DLL actually is, i.e. `bin\..\`); if
so, it rewrites the path to an absolute one before calling through to the real function — which
already has a branch that sends absolute paths straight to `CreateFileW`, bypassing the
packed-archive lookup entirely. No archive/`.fat` format replication needed. See
`src/hook_vfs.cpp` for the exact logic and `reverse/dunia/archive_loading.md` for why this works.

`Data_Win32\Loose\` was chosen (over, say, a folder next to the DLL, or a sibling of `Data_Win32`)
specifically to match what modders already produce: unpacking an archive with Gibbed's tools gives
a folder (e.g. `patch_unpack\`) whose internal structure already exactly matches the relative
paths the engine asks for — so "unpack, edit, drop the result under `Data_Win32\Loose\` instead of
repacking" requires no path translation at all. See `Loose/README.md`.

**Version gate**: refuses to install the hook unless `Dunia.dll`'s module size matches the
confirmed Steam v1.03 build (20,183,176 bytes) — the hook target is a hardcoded RVA from that
specific build, and would silently be wrong on a different build/patch level.

## Building

Requires the `x86-debug` or `x86-release` CMake preset — **never `x64-*`**, the game process is
32-bit and a 64-bit `dinput8.dll` cannot load into it at all.

```
.\build.ps1            # release (default)
.\build.ps1 -Config debug
```

`build.ps1` finds Visual Studio via `vswhere`, runs `vcvarsall.bat x86` to get a properly
32-bit-configured environment (the preset's `"architecture": {"strategy": "external"}` means CMake
expects the caller to have already set that up — there's no `-A`-style flag for the Ninja
generator), then configures and builds. Output path is printed at the end.

Or, from Visual Studio directly: open the folder, select "x86-Debug"/"x86-Release" as the active
configuration, build — VS sets up the same environment automatically.

## Installing

1. Build produces `dinput8.dll` (see build output directory for the preset you used).
2. Copy the built `dinput8.dll` into the game's `bin\` folder (next to `FarCry2.exe`). Nothing
   else needs installing — the real `dinput8.dll` is resolved from the system directory at
   runtime, not bundled.
3. Copy the `Loose\` folder into the game's `Data_Win32\` folder (a sibling of `bin\`, not inside
   it) — see [`Loose/README.md`](Loose/README.md) for the layout and how to add mod files.
4. Launch the game normally. Check `bin\modpatcher.log` to confirm the hook installed and see
   which paths were requested/overridden.

## Status

**Done and dynamically verified against a real launch.** Confirmed working end to end:

1. Build is genuinely 32-bit, loads cleanly as a `DINPUT8.dll` proxy, real input passthrough works.
2. Passthrough regression: with `Loose\` empty, the game boots and loads all the way through
   world/level bootstrap (menu, nav mesh, mission scripts, per-map data) with zero crashes — ~130
   distinct asset requests, every content type (`.xml`, `.lua`, `.fcb`, sound, archive containers)
   correctly observed and passed through unmodified.
3. Override actually takes effect: placing an **empty** file at `Data_Win32\Loose\worlds\worlds.dat`
   reliably crashes the game at the point it would load `worlds.dat` — direct proof the rewritten
   path is what's actually being opened, not just logged.

**Known scope limit, not a bug**: only assets requested through `VFS_ResolvePath` are covered. A
separate, lower-level streaming path (`LevelAsset_OpenStream` and friends, used at least for
world-sector terrain data, likely also meshes/textures) bypasses this hook entirely — see
`reverse/dunia/archive_loading.md`'s "Known coverage gap" section. Extending coverage to that path
is a scoped, not-yet-started follow-up (would need a hash-based lookup instead of the current
path-rewrite trick, since that lower path only has a precomputed hash, not a string, by the time
it's reached).
