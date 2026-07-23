"""
Flips ToRed's (0x401000) handler in FarCry2.exe from `*param_1 = 1` to `*param_1 = 0`.

Original bytes at 0x401000:
  8B 44 24 04        MOV EAX,[ESP+4]
  C7 00 01 00 00 00  MOV dword ptr [EAX],0x1   <- the 4-byte immediate at 0x401006 is the target
  33 C0              XOR EAX,EAX
  C3                 RET

Only the immediate (0x401006, 4 bytes) changes: 01 00 00 00 -> 00 00 00 00.

Run against a backup-safe copy; see EXE_PATH below.
"""

import pefile
import shutil
import os

EXE_PATH = r"C:\Program Files (x86)\Steam\steamapps\common\Far Cry 2\bin\FarCry2.exe"
BACKUP_PATH = EXE_PATH + ".orig"

IMAGE_BASE = 0x00400000

PATCHES = {
    # The 4-byte immediate operand of `MOV dword ptr [EAX],0x1` -> 0x0
    0x00401006: bytes([0x00, 0x00, 0x00, 0x00]),
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
