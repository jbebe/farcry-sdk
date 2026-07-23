"""
Replaces the incHB/SaveGame shared stub (0x4010d0) in FarCry2.exe.

Original (3 bytes):
  33 C0   XOR EAX,EAX
  C3      RET
-> always returns 0, ignoring both arguments.

New (5 bytes, fits in the 13-byte alignment gap before RegisterDebugCommands @ 0x4010e0):
  8B 44 24 04   MOV EAX,[ESP+4]   ; load arg1's raw bytes into EAX
  C3            RET               ; return via EAX, same as every other handler in this table

(First attempt used `FLD dword ptr [ESP+4]` to return via the FPU's ST(0) -- that assumed a
genuine FPU-convention float return, but the dispatcher is declared as a plain 4-byte `undefined4`
return, so the caller's `(float)` cast is most likely a numeric int->float conversion in its own
code, not "the callee already left a float on the FPU stack." The FLD version pushed an unpopped
value onto the 8-deep x87 stack on every call -- this function runs frequently -- which overflowed
the stack and crashed the game within seconds of testing. This version never touches the FPU at
all, so it can't desync anything.)

Net effect: incHB now echoes back whatever time value it's given instead of discarding it,
restoring whatever real-time-driven behavior this hook was meant to drive.

Run against a backup-safe copy; see EXE_PATH below.
"""

import pefile
import shutil
import os

EXE_PATH = r"C:\Program Files (x86)\Steam\steamapps\common\Far Cry 2\bin\FarCry2.exe"
BACKUP_PATH = EXE_PATH + ".orig"

IMAGE_BASE = 0x00400000

PATCHES = {
    0x004010D0: bytes([0x8B, 0x44, 0x24, 0x04, 0xC3]),
}


def main():
    if not os.path.exists(BACKUP_PATH):
        shutil.copy2(EXE_PATH, BACKUP_PATH)
        print(f"Backed up original to {BACKUP_PATH}")
    else:
        print(f"Backup already exists at {BACKUP_PATH}, not overwriting it")

    pe = pefile.PE(EXE_PATH, fast_load=True)
    offsets = {}
    for va, data in PATCHES.items():
        rva = va - IMAGE_BASE
        offset = pe.get_offset_from_rva(rva)
        offsets[va] = offset
        print(f"VA 0x{va:08X} -> file offset 0x{offset:X} ({len(data)} bytes)")
    pe.close()

    with open(EXE_PATH, "r+b") as f:
        for va, data in PATCHES.items():
            f.seek(offsets[va])
            f.write(data)

    print("Patch applied to", EXE_PATH)


if __name__ == "__main__":
    main()
