#include "util/resource.h"

#include <windows.h>

namespace FCSE {

bool FindRcData(const wchar_t* name, const void** data, size_t* size) {
    // GetModuleHandleW(nullptr) because these resources are in FCSE.exe, not in Dunia.dll.
    HMODULE self = GetModuleHandleW(nullptr);
    // MAKEINTRESOURCEW(10) rather than RT_RCDATA: this target does not define UNICODE, so
    // RT_RCDATA expands to the ANSI form and will not pass to FindResourceW.
    HRSRC found = FindResourceW(self, name, MAKEINTRESOURCEW(10));
    if (found == nullptr) {
        return false;
    }
    HGLOBAL block = LoadResource(self, found);
    if (block == nullptr) {
        return false;
    }
    const void* bytes = LockResource(block);
    DWORD length = SizeofResource(self, found);
    if (bytes == nullptr || length == 0) {
        return false;
    }
    *data = bytes;
    *size = length;
    return true;
}

}
