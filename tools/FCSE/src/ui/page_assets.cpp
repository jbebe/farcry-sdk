#include "ui/page_assets.h"

#include "engine/address_library.h"
#include "engine/address_symbols.h"
#include "engine/dunia_api.h"
#include "log.h"

#include <cstdint>
#include <windows.h>

namespace FCSE {

namespace {
    // Resource names, not paths. Embedded by CMake from assets/fcse.rc.in; unquoted non-numeric
    // names in a .rc are string names, so there is no resource.h to keep in sync.
    constexpr wchar_t kNormalResource[] = L"FCSE_MGB";
    constexpr wchar_t kWidescreenResource[] = L"FCSE_MGB_WIDESCREEN";

    // How the engine itself chooses between the `pc` and `pcwidescreen` UI sets.
    //
    // CMagmaLocalizationUtil::GetLocalizedPackageName (0x10554fc0) builds "\pc", then appends
    // "widescreen" when *(char*)(FUN_1032d910() + 1) is non-zero, then the language folder. That
    // accessor is a lazy-initialised getter for a small display-config struct and returns either a
    // live pointer or a static default, so it is safe to call at any point.
    //
    // Reading the same byte means FCSE's page can never disagree with the rest of the menu about
    // which aspect the game is running.
    constexpr ptrdiff_t kWidescreenFlagOffset = 1;

    using DisplayConfigFn = unsigned char*(__cdecl*)();

    // magma::BinaryLoadVisitor::ReadHeader's own first two checks. These used to guard against a
    // player's loose file being the wrong one; with the package embedded they instead catch a build
    // that embedded something wrong, which is worth the same few lines.
    constexpr char kMagic[] = {'M', 'A', 'G', 'M', 'A'};
    constexpr uint32_t kExpectedVersion = 0x1EAB90;
    constexpr size_t kVersionOffset = 9;  // magic(5) + sentinel(4)
    constexpr size_t kHeaderPrefix = 13;  // ... + version(4)

    bool g_attempted = false;
    PackageBytes g_package;

    bool SafeReadWidescreenFlag(bool* outWidescreen, DWORD* outCode) {
        auto getConfig = AddressLibrary::Function<DisplayConfigFn>(Symbols::kDisplayConfig);
        if (getConfig == nullptr) {
            *outCode = 0;
            return false;
        }
        __try {
            unsigned char* config = getConfig();
            *outWidescreen = config != nullptr && config[kWidescreenFlagOffset] != 0;
            return true;
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            *outCode = GetExceptionCode();
            return false;
        }
    }

    // The resource lives in FCSE.exe's own image, not in Dunia.dll - hence GetModuleHandleW(nullptr).
    // Nothing here needs freeing: LockResource hands back a pointer into the mapped image, valid as
    // long as the module is loaded, which for our own exe is the process lifetime.
    PackageBytes FindEmbedded(const wchar_t* name) {
        HMODULE self = GetModuleHandleW(nullptr);
        // MAKEINTRESOURCEW(10), not RT_RCDATA: this target does not define UNICODE, so RT_RCDATA
        // expands to the ANSI form and will not pass to FindResourceW. Same reason the rest of this
        // codebase names the W functions explicitly.
        HRSRC found = FindResourceW(self, name, MAKEINTRESOURCEW(10));
        if (found == nullptr) {
            return {};
        }
        HGLOBAL block = LoadResource(self, found);
        if (block == nullptr) {
            return {};
        }
        PackageBytes bytes;
        bytes.data = static_cast<const unsigned char*>(LockResource(block));
        bytes.size = SizeofResource(self, found);
        return bytes;
    }
}

PackageBytes PageAssets::Locate() {
    if (g_attempted) {
        return g_package;
    }
    g_attempted = true;

    bool widescreen = false;
    DWORD code = 0;
    if (!SafeReadWidescreenFlag(&widescreen, &code)) {
        // Falling back to the 4:3 layout rather than declining: a page with slightly wrong
        // decoration geometry is a far better outcome than no page at all.
        Log::Loader("Page assets: faulted reading the engine's widescreen flag (" +
                    std::to_string(code) + ") - assuming 4:3");
    }
    Log::Loader(std::string("Page assets: engine reports ") +
                (widescreen ? "widescreen (pcwidescreen)" : "4:3 (pc)") + " UI");

    const wchar_t* name = widescreen ? kWidescreenResource : kNormalResource;
    PackageBytes bytes = FindEmbedded(name);
    if (!bytes) {
        // Overwhelmingly the build's fault rather than the machine's: a .rc added to a project
        // without enable_language(RC) is skipped without a word, producing exactly this.
        Log::Loader("Page assets: this FCSE.exe was built without its embedded UI package - check "
                    "enable_language(RC) and assets/fcse.rc.in in CMakeLists.txt");
        return {};
    }

    if (bytes.size < kHeaderPrefix) {
        Log::Loader("Page assets: the embedded package is too small to be a .mgb");
        return {};
    }
    if (memcmp(bytes.data, kMagic, sizeof(kMagic)) != 0) {
        Log::Loader("Page assets: the embedded package is not a .mgb (no \"MAGMA\" magic)");
        return {};
    }

    uint32_t version = 0;
    memcpy(&version, bytes.data + kVersionOffset, sizeof(version));
    if (version != kExpectedVersion) {
        // The .mgb and .mgb.desc formats share one version epoch, and the engine rejects a
        // mismatch outright - so a package built for a different Magma build is worth naming here
        // rather than letting it fail deep inside the loader.
        char message[128];
        sprintf_s(message, "Page assets: the embedded package is version 0x%06X, this engine wants "
                           "0x%06X", version, kExpectedVersion);
        Log::Loader(message);
        return {};
    }

    char message[96];
    sprintf_s(message, "Page assets: using the embedded package (%zu bytes)", bytes.size);
    Log::Loader(message);

    g_package = bytes;
    return g_package;
}

} // namespace FCSE
