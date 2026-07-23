#include "dunia.h"
#include "log.h"

#include <windows.h>

namespace LooseMods {

namespace {

// Steam v1.03 build, 20,183,176 bytes on disk (reverse/dunia/overview.md). Refuse to hook
// anything else rather than patch a possibly-wrong address in a different build/patch level.
//
// Deliberately the ON-DISK file size, not GetModuleInformation's SizeOfImage - that's the
// in-memory virtual image size (padded/aligned to section alignment), which is never
// bit-identical to the file's actual byte count on disk. This constant was sourced from the file
// size (cross-checked directly against a real install), so it must be compared against the file
// size too, not the loaded image size.
constexpr unsigned long long kExpectedDuniaFileSize = 20183176ULL;

} // namespace

bool GetVerifiedDuniaBase(uintptr_t& outBase) {
    HMODULE duniaModule = GetModuleHandleA("Dunia.dll");
    if (!duniaModule) {
        Log::Error(L"Dunia.dll not loaded yet - hook install skipped.");
        return false;
    }

    wchar_t duniaPath[MAX_PATH];
    if (GetModuleFileNameW(duniaModule, duniaPath, MAX_PATH) == 0) {
        Log::Error(L"GetModuleFileNameW on Dunia.dll failed - hook install skipped.");
        return false;
    }

    WIN32_FILE_ATTRIBUTE_DATA fileInfo{};
    if (!GetFileAttributesExW(duniaPath, GetFileExInfoStandard, &fileInfo)) {
        Log::Error(L"GetFileAttributesExW on Dunia.dll failed - hook install skipped.");
        return false;
    }

    unsigned long long actualSize =
        (static_cast<unsigned long long>(fileInfo.nFileSizeHigh) << 32) | fileInfo.nFileSizeLow;
    if (actualSize != kExpectedDuniaFileSize) {
        wchar_t msg[160];
        swprintf_s(msg, L"Dunia.dll size mismatch (expected v1.03's 20,183,176 bytes, found %llu) "
                         L"- refusing to hook a possibly-different build.",
                   actualSize);
        Log::Error(msg);
        return false;
    }

    outBase = reinterpret_cast<uintptr_t>(duniaModule);
    return true;
}

} // namespace LooseMods
