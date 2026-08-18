---
title: FCSE
description: Introduce FCSE, the SKSE-style DLL plugin loader for Far Cry 2
---

# FCSE (Far Cry Script Extender)

Some engine behavior can't be reached through game assets at all. The Dunia engine is versatile,
but Ubisoft had to force some kind of abstract domain on top of it so that it doesn't become "the
next Unity or Unreal" — and that domain logic stayed compiled directly into `Dunia.dll`. Until now,
the only way to change it was to ship your own patched copy of that file, which works for exactly
one mod at a time: two differently-patched copies of `Dunia.dll` can't coexist, so the moment two
mods both want to touch engine internals, one of them loses.

FCSE fixes that the way [SKSE](https://www.nexusmods.com/skyrimspecialedition/mods/30379) fixed the
equivalent problem for Skyrim: a separate launcher, `FCSE.exe`, that loads any number of
third-party plugin DLLs from `bin\plugins\` before the game engine spins up, giving each one a safe,
shared way to change engine behavior — without needing a shared pre-patched binary, and without one
mod's changes silently clobbering another's.

Source: [`tools/FCSE`](https://github.com/jbebe/farcry-sdk/tree/main/tools/FCSE) — see its
[README](https://github.com/jbebe/farcry-sdk/tree/main/tools/FCSE) for the full technical design
(exactly how it reimplements `FarCry2.exe`'s own `WinMain`, resolves `Dunia.dll`'s exports by name,
and orders plugin registration around a confirmed engine quirk) and
[`fcse_api.h`](https://github.com/jbebe/farcry-sdk/blob/main/tools/FCSE/include/fcse_api.h) for
the full plugin ABI, documented inline. For the underlying reverse-engineering this is built on,
see [Engine Internals](/docs/category/engine-internals) — particularly the [launcher
exe](/docs/engine-internals/launcher-exe) and [function registry](/docs/engine-internals/function-registry)
notes.

## What it can do

A plugin is a plain DLL, dropped into `bin\plugins\`, exporting one required function
(`FCSE_Load`) and one optional one (`FCSE_OnRegisterFunctions`). FCSE gives it three escalating
tools to change engine behavior with:

1. **Claim a named engine callback** (`AddFunctionCB`) — zero reverse-engineering required, and
   version-independent (it's a string key, resolved the same way on every build). Dunia's own code
   already calls out by name to a fixed set of hooks for real gameplay events — diamond pickups,
   malaria-curve progression, main-menu construction, loading-screen text, and more (see the
   [function-registry notes](/docs/engine-internals/function-registry) for the full surveyed list).
   A plugin can claim one of these names outright, or **override one of FCSE's own 12 stock
   handlers** — plugin registrations run before FCSE's own, specifically so this works.
2. **Detour a function** (`Hook`) — for engine internals with no existing named hook, backed by
   [MinHook](https://github.com/TsudaKageyu/minhook). Needs the plugin author to have found the
   target address themselves (e.g. via Ghidra against a specific confirmed `Dunia.dll` build) —
   FCSE hands back a working trampoline to call the original.
3. **Patch bytes directly** (`Patch`) — for small constant/branch-flip edits, applied live and
   in-process instead of to a shared file on disk. This is the direct successor to what
   `reverse/patch_toRed.py`/`patch_incHB.py`/`patch_carJoke.py` already do *statically* against
   `Dunia.dll` before launch — same idea, but any number of plugins can now each apply their own
   edit without agreeing on one shared pre-patched binary.

**Conflicts are loud, not silent.** If two plugins target the same name/address, FCSE doesn't try
to chain their effects together — the second claimant is rejected, and both plugins' identities are
logged, so a real conflict is always visible and debuggable instead of turning into a
hard-to-diagnose behavior change. Every run also writes a single `bin\fcse.log`, tagged by source
and timestamped to 100ns resolution, so tracing exactly what happened (which plugins loaded, what
each one claimed, what got rejected) never needs a debugger.

See
[`example_plugin.cpp`](https://github.com/jbebe/farcry-sdk/blob/main/tools/FCSE/example_plugin/example_plugin.cpp)
for a small, complete plugin exercising all three tiers.

## Installing with Vortex

0. Download the latest FCSE version from here: [nexusmods.com](https://www.nexusmods.com/farcry2/mods/368)
1. Add FCSE as a mod to the game
2. Go to Tools/FCSE and set it as the default launcher
3. Start the game from Vortex by pressing the 'Play' button.

## Installing manually

0. Download the latest FCSE version from here: [nexusmods.com](https://www.nexusmods.com/farcry2/mods/368)
1. Copy `FCSE.exe` into the game's `bin\` folder, next to the existing `FarCry2.exe` — that file is
   left completely untouched; `FCSE.exe` is an additional way to launch the game, not a
   replacement.
2. Drop plugin `.dll` files into `bin\plugins\` (created automatically on first run if it doesn't
   exist yet).
3. Launch `FCSE.exe` instead of `FarCry2.exe`.
