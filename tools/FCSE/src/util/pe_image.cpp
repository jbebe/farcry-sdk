#include "util/pe_image.h"

#include <cstring>

namespace FCSE {

const IMAGE_NT_HEADERS32* PeHeaders(const void* imageBase) {
    if (imageBase == nullptr) {
        return nullptr;
    }
    const auto* base = reinterpret_cast<const unsigned char*>(imageBase);
    const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE) {
        return nullptr;
    }
    const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS32*>(base + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE) {
        return nullptr;
    }
    return nt;
}

bool FindExecutableSection(const void* imageBase, const uint8_t** begin, size_t* size) {
    const IMAGE_NT_HEADERS32* nt = PeHeaders(imageBase);
    if (nt == nullptr) {
        return false;
    }

    const auto* base = reinterpret_cast<const uint8_t*>(imageBase);
    const auto* section = IMAGE_FIRST_SECTION(nt);
    for (unsigned i = 0; i < nt->FileHeader.NumberOfSections; ++i, ++section) {
        if ((section->Characteristics & IMAGE_SCN_MEM_EXECUTE) == 0) {
            continue;
        }
        const DWORD extent =
            section->Misc.VirtualSize != 0 ? section->Misc.VirtualSize : section->SizeOfRawData;
        if (extent == 0) {
            continue;
        }
        *begin = base + section->VirtualAddress;
        *size = extent;
        return true;
    }
    return false;
}

uintptr_t* FindImportSlot(uintptr_t imageBase, const char* moduleName, const char* functionName) {
    const IMAGE_NT_HEADERS32* headers = PeHeaders(reinterpret_cast<const void*>(imageBase));
    if (headers == nullptr) {
        return nullptr;
    }

    const IMAGE_DATA_DIRECTORY& imports =
        headers->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
    if (imports.VirtualAddress == 0 || imports.Size == 0) {
        return nullptr;
    }

    for (const auto* descriptor = reinterpret_cast<const IMAGE_IMPORT_DESCRIPTOR*>(
             imageBase + imports.VirtualAddress);
         descriptor->Name != 0; ++descriptor) {
        if (_stricmp(reinterpret_cast<const char*>(imageBase + descriptor->Name), moduleName) != 0) {
            continue;
        }

        if (descriptor->OriginalFirstThunk == 0) {
            return nullptr;
        }

        const auto* names = reinterpret_cast<const IMAGE_THUNK_DATA32*>(
            imageBase + descriptor->OriginalFirstThunk);
        auto* slots = reinterpret_cast<uintptr_t*>(imageBase + descriptor->FirstThunk);
        for (; names->u1.AddressOfData != 0; ++names, ++slots) {
            if (IMAGE_SNAP_BY_ORDINAL32(names->u1.Ordinal)) {
                continue;
            }
            const auto* imported =
                reinterpret_cast<const IMAGE_IMPORT_BY_NAME*>(imageBase + names->u1.AddressOfData);
            if (strcmp(reinterpret_cast<const char*>(imported->Name), functionName) == 0) {
                return slots;
            }
        }
        return nullptr;
    }
    return nullptr;
}

}
