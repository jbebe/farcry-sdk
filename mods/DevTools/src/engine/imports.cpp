#include "engine/imports.h"

#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include <cstdint>

namespace DevTools::Imports {

bool Hook(void* module, const char* fromModule, const char* function, void* detour,
          void** original) {
    auto* base = static_cast<uint8_t*>(module);
    auto* dos = reinterpret_cast<IMAGE_DOS_HEADER*>(base);
    if (base == nullptr || dos->e_magic != IMAGE_DOS_SIGNATURE) {
        return false;
    }

    auto* nt = reinterpret_cast<IMAGE_NT_HEADERS*>(base + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE) {
        return false;
    }

    const IMAGE_DATA_DIRECTORY& dir =
        nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
    if (dir.VirtualAddress == 0) {
        return false;
    }

    const bool byOrdinal = IS_INTRESOURCE(function);
    for (auto* import = reinterpret_cast<IMAGE_IMPORT_DESCRIPTOR*>(base + dir.VirtualAddress);
         import->Name != 0; ++import) {
        if (lstrcmpiA(reinterpret_cast<const char*>(base + import->Name), fromModule) != 0) {
            continue;
        }

        // Names come from OriginalFirstThunk, or from FirstThunk when it is absent.
        auto* slot = reinterpret_cast<IMAGE_THUNK_DATA*>(base + import->FirstThunk);
        auto* name = reinterpret_cast<IMAGE_THUNK_DATA*>(
            base +
            (import->OriginalFirstThunk != 0 ? import->OriginalFirstThunk : import->FirstThunk));

        for (; name->u1.AddressOfData != 0; ++name, ++slot) {
            if (IMAGE_SNAP_BY_ORDINAL(name->u1.Ordinal)) {
                if (!byOrdinal ||
                    IMAGE_ORDINAL(name->u1.Ordinal) != reinterpret_cast<uintptr_t>(function)) {
                    continue;
                }
            } else {
                auto* imported =
                    reinterpret_cast<IMAGE_IMPORT_BY_NAME*>(base + name->u1.AddressOfData);
                if (byOrdinal ||
                    lstrcmpA(reinterpret_cast<const char*>(imported->Name), function) != 0) {
                    continue;
                }
            }

            *original = reinterpret_cast<void*>(slot->u1.Function);

            DWORD old = 0;
            if (!VirtualProtect(&slot->u1.Function, sizeof(slot->u1.Function), PAGE_READWRITE,
                                &old)) {
                return false;
            }

            slot->u1.Function = reinterpret_cast<uintptr_t>(detour);
            VirtualProtect(&slot->u1.Function, sizeof(slot->u1.Function), old, &old);
            return true;
        }
    }

    return false;
}

}
