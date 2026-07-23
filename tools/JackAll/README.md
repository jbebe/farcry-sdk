# JackAll

A mod manager for Far Cry 2.

The name is for the Jackal, and for *jack of all trades* — the point is one tool that covers the
whole job, instead of the half-dozen single-purpose converters and a `.bat` file the community has
had to string together until now.

## What it does

Presents every game archive as one browsable filesystem, lets you replace or revert individual files,
and compiles the result into a real `patch.dat` / `patch.fat` that the **stock, unmodified engine
loads**. No DLL, no injection, nothing running inside the game process — and the mods it produces are
ordinary patch archives, so they're shareable with people who don't use this tool.

- **Mods tab** — an ordered list of mod zips, applied top to bottom (later wins). Your own edits live
  in `workspace\`, which is pinned last and can be switched off like any other mod.
- **Files tab** — the merged view of all 13 archives, as the engine resolves them. Anything a mod
  supplies is highlighted, so "what have I actually changed" is answerable at a glance.

A mod zip is just a tree of relative paths (`worlds\world1\generated\…`) — exactly what unpacking an
archive already gives you, so existing community mods drop straight in.

## Safety

`patch.dat` is backed up to `patch.dat.vanilla` before the first build, and **every build regenerates
the patch from that backup**, never from whatever is currently on disk. So:

- building twice produces identical bytes (you can't accumulate corruption by clicking the button),
- disabling a mod and rebuilding genuinely removes it,
- a failed build leaves the game untouched — the new patch is written to a temp file and only swapped
  in once it's complete.

`common.dat`, `worlds.dat` and the rest are opened read-only and never written.

## Layout

```
JackAll.exe         one self-contained file - no .NET install needed
config.ini          your game folder + the mod list; hand-editable, comments preserved
data\               shipped/generated support files - nothing here is hand-edited
  .itemhashes         hash -> filename dictionary (161,686 names)
  .fcbclasses         .fcb class/member name-and-type config
  .archivehashes      known-good hashes for a clean 1.03 install's archives
  .appcache           sniffed file types + decoded `.fcb` structure; delete it if you reinstall the game
  ffmpeg.exe          bundled so .sbao audio import/export works out of the box
workspace\          your staged edits, as plain files
```

`.appcache` is why startup is ~400 ms instead of ~1.2 s. A quarter of the game's entries have no
recovered filename, so the only way to know what they are is to read their first bytes — 50,000-odd
random seeks, which is fine on an SSD and ruinous on a mechanical drive. The archives never change,
so neither do the answers: they're sniffed once and written down. It's a pure optimisation — delete
it, corrupt it, truncate it, and the tool just rebuilds it on the next launch.

The archives are indexed by CRC32 of the filename, and the community's dictionary is incomplete —
about 54,000 of the game's 214,000 files have no recovered name. Those are still fully usable: they
get an extension sniffed from their header and appear under `_unknown\`, and an edit to one is staged
as `_hash\<crc32>.<ext>`, which the builder writes at that literal hash. Nothing is unmoddable just
because nobody knows what it's called.

## Building

```
dotnet build            # or open JackAll.sln
dotnet test             # runs against the real installed game, if it's present
```

The tests are the interesting part. They don't use fixtures — they run against the archives Ubisoft
actually shipped, because that's the only real authority on the format:

- every shipped `.fat` re-serializes byte-for-byte identical,
- the LZO decoder is checked against every compressed entry in the game (~110,000 streams),
- a no-mod build reproduces the real 10 MB `patch.dat` byte-for-byte,
- after a build with a mod applied, every *other* file still decompresses to its original bytes.
