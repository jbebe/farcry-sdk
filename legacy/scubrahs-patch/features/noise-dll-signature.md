---
title: Dunia.dll checksum and signature
kind: noise
systems: [engine]
match:
  - "install/bin/dunia.dll@hdr+*"
  - "install/bin/dunia.dll@overlay"
---

# Dunia.dll checksum and signature

Byte patching the DLL changes two things that are never part of a feature:

- `@hdr+0x190`: the PE header `CheckSum`, which Windows does not verify for a DLL loaded by a game.
- `@hdr+0x1d4`: the size of the security directory, which points at the new overlay.
- `@overlay`: the Authenticode blob past the last section (4,744 bytes in Steam's build, 5,736 in the
  mod's), never mapped into memory. Ubisoft's signature no longer matches the patched code.
