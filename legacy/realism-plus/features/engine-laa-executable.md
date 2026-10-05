---
title: Large-address-aware GOG launcher
kind: component
bundle: fixes
status: located
systems: [engine]
match:
  - "install/bin/farcry2.exe@*"
exclude: []
requires: []
verified: diff
---

# Large-address-aware GOG launcher

`bin/FarCry2.exe` is the GOG launcher with `IMAGE_FILE_LARGE_ADDRESS_AWARE` set, so the 32-bit game
may use up to 4 GB of address space on 64-bit Windows. Against GOG's own launcher only two bytes
differ: the COFF `Characteristics` low byte `0x03` → `0x23` (`hdr+0xFE`), and the PE checksum
(`hdr+0x140`, `0x35` → `0x55`).

Against the Steam baseline the analysis lists seven runs. Five of them are simply GOG's build: the
header and debug timestamps (`hdr+0xF0`, `0x20E4`), `0x20F0`, the PDB path at `0x223C` (Steam's is
the `fc2-pc-uplay` branch) and the Authenticode overlay. The two above are the edit.

Scubrah's Patch ships the same flag on Steam's launcher
([`laa-executable`](../../scubrahs-patch/features/laa-executable.md)).
