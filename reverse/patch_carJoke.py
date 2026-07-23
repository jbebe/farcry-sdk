"""
Registers "carJoke" in FarCry2.exe's RegisterDebugCommands (0x4010e0) by detouring
its final 5 bytes into a new code cave at 0x401a20 (unused space inside .text,
before the 0x401bff section boundary). The cave adds one more AddFunctionCB
registration, then replicates the original function's stack cleanup/return.

New handler just does `*(char*)arg1 = 0` -- flips the caller's veto flag to false.

Run against a backup-safe copy; see EXE_PATH below.
"""

import pefile
import shutil
import os

EXE_PATH = r"C:\Program Files (x86)\Steam\steamapps\common\Far Cry 2\bin\FarCry2.exe"
BACKUP_PATH = EXE_PATH + ".orig"

IMAGE_BASE = 0x00400000

PATCHES = {
    # New handler: MOV EAX,[ESP+4] ; MOV BYTE PTR [EAX],0 ; RET
    0x00401A20: bytes([0x8B, 0x44, 0x24, 0x04, 0xC6, 0x00, 0x00, 0xC3]),
    # String "carJoke\0"
    0x00401A28: b"carJoke\x00",
    # PUSH str ; PUSH handler ; CALL ESI ; ADD ESP,8 ; ADD ESP,0x38 ; POP ESI ; RET
    0x00401A30: bytes([
        0x68, 0x28, 0x1A, 0x40, 0x00,  # PUSH 0x00401A28
        0x68, 0x20, 0x1A, 0x40, 0x00,  # PUSH 0x00401A20
        0xFF, 0xD6,                    # CALL ESI
        0x83, 0xC4, 0x08,              # ADD ESP,8
        0x83, 0xC4, 0x38,              # ADD ESP,0x38  (original tail begins here)
        0x5E,                          # POP ESI
        0xC3,                          # RET
    ]),
    # Hook: replace RegisterDebugCommands' original tail (ADD ESP,0x38 / POP ESI / RET)
    # with JMP 0x00401A30
    0x0040119E: bytes([0xE9, 0x8D, 0x08, 0x00, 0x00]),
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
