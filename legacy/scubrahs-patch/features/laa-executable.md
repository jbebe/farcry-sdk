---
title: Large-address-aware launcher
kind: component
bundle: fixes
claims:
  - "Included LAA patched game executable for stability"
status: located
systems: [engine]
match:
  - "install/bin/farcry2.exe@*"
exclude: []
requires: []
verified: diff
---

# Large-address-aware launcher

The game's 32-bit process may use up to 4 GB of address space on a 64-bit Windows instead of 2 GB,
which keeps it from running out of room for large allocations.

## How

`Far Cry 2/bin/farcry2.exe` is shipped whole, a launcher with the PE header flag
`IMAGE_FILE_LARGE_ADDRESS_AWARE` set. Its code is the same as Steam's; it is a different build of
the same source. The seven byte runs against Steam's:

- `hdr+0xFE`: the low byte of the COFF `Characteristics`, `0x03` -> `0x23` (`0x0103` -> `0x0123`,
  bit `0x20` is large-address-aware). This is the change that matters.
- `hdr+0xF0` and `0x20E4`: the header and debug-directory build timestamps.
- `hdr+0x140`: the PE checksum.
- `0x20F0` and `0x223C`: the debug directory's `RSDS` record. Steam's names
  `d:\dev\fc2relaunch\fcx-branches\fc2-pc-uplay\bin\NomadLaunch_PC.pdb`, the mod's
  `d:\CastorVersions\FCX-PC-0053\fcx-branches\milestones-b\bin\NomadLaunch_PC.pdb`: the original
  2008 retail launcher, not the Uplay relaunch's.
- the overlay: a different Authenticode signature of the same length (4,744 bytes).

The flag on the launcher is what lets the whole process, `Dunia.dll` included, use the extra space.

## Uncertain

- Whether the signature still verifies after the flag was set is not checked; most likely it does
  not, which Windows does not enforce for a game launcher.
