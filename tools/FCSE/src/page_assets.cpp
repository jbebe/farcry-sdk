#include "page_assets.h"

#include "dunia_api.h"
#include "log.h"

#include <cstdint>
#include <vector>
#include <windows.h>

namespace FCSE {

namespace {
    // Relative to the loader's own directory (bin\). Kept under a subfolder rather than dropped
    // beside FCSE.exe so a future package - or a plugin's own - has somewhere obvious to live.
    constexpr wchar_t kNormalPath[] = L"fcse\\ui\\fcse.mgb";
    constexpr wchar_t kWidescreenPath[] = L"fcse\\ui\\fcse_widescreen.mgb";

    // How the engine itself chooses between the `pc` and `pcwidescreen` UI sets.
    //
    // CMagmaLocalizationUtil::GetLocalizedPackageName (0x10554fc0) builds "\pc", then appends
    // "widescreen" when *(char*)(FUN_1032d910() + 1) is non-zero, then the language folder. That
    // accessor is a lazy-initialised getter for a small display-config struct and returns either a
    // live pointer or a static default, so it is safe to call at any point.
    //
    // Reading the same byte means FCSE's page can never disagree with the rest of the menu about
    // which aspect the game is running.
    constexpr uintptr_t kDuniaPreferredBase = 0x10000000;
    constexpr uintptr_t kDisplayConfigRva = 0x1032D910;
    constexpr ptrdiff_t kWidescreenFlagOffset = 1;

    using DisplayConfigFn = unsigned char*(__cdecl*)();

    // magma::BinaryLoadVisitor::ReadHeader's own first two checks. Anything that fails these would
    // fail inside the engine too, so failing here turns a silent black screen into a log line.
    constexpr char kMagic[] = {'M', 'A', 'G', 'M', 'A'};
    constexpr uint32_t kExpectedVersion = 0x1EAB90;
    constexpr size_t kHeaderPrefix = 13; // magic(5) + sentinel(4) + version(4)

    bool g_attempted = false;
    bool g_available = false;
    std::string g_path;
    std::wstring g_pathWide;

    std::string Narrow(const std::wstring& text) {
        if (text.empty()) {
            return {};
        }
        int size = WideCharToMultiByte(CP_ACP, 0, text.c_str(), static_cast<int>(text.size()),
                                       nullptr, 0, nullptr, nullptr);
        if (size <= 0) {
            return {};
        }
        std::string narrow(static_cast<size_t>(size), '\0');
        WideCharToMultiByte(CP_ACP, 0, text.c_str(), static_cast<int>(text.size()), narrow.data(),
                            size, nullptr, nullptr);
        return narrow;
    }

    bool SafeReadWidescreenFlag(bool* outWidescreen, DWORD* outCode) {
        uintptr_t base = DuniaApi::Base();
        if (base == 0) {
            *outCode = 0;
            return false;
        }
        auto getConfig = reinterpret_cast<DisplayConfigFn>(
            base + (kDisplayConfigRva - kDuniaPreferredBase));
        __try {
            unsigned char* config = getConfig();
            *outWidescreen = config != nullptr && config[kWidescreenFlagOffset] != 0;
            return true;
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            *outCode = GetExceptionCode();
            return false;
        }
    }

    // Reads just the header. The rest of the file is the engine's problem; JackAll's MgbXmlTests
    // already prove the shipped package round-trips and that its page resolves by name.
    bool ReadHeader(const std::wstring& path, std::vector<uint8_t>& header) {
        HANDLE file = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr,
                                  OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file == INVALID_HANDLE_VALUE) {
            Log::Loader("Page assets: cannot open " + Narrow(path) + " (error " +
                        std::to_string(GetLastError()) + ")");
            return false;
        }
        header.assign(kHeaderPrefix, 0);
        DWORD read = 0;
        bool ok = ReadFile(file, header.data(), static_cast<DWORD>(header.size()), &read, nullptr) &&
                  read == header.size();
        CloseHandle(file);
        if (!ok) {
            Log::Loader("Page assets: " + Narrow(path) + " is too small to be a .mgb package");
        }
        return ok;
    }
}

bool PageAssets::Locate() {
    if (g_attempted) {
        return g_available;
    }
    g_attempted = true;

    const std::wstring& directory = Log::LoaderDirectory();
    if (directory.empty()) {
        Log::Loader("Page assets: loader directory unknown, cannot locate fcse.mgb");
        return false;
    }

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

    std::wstring path = directory + (widescreen ? kWidescreenPath : kNormalPath);

    std::vector<uint8_t> header;
    if (!ReadHeader(path, header)) {
        return false;
    }

    if (memcmp(header.data(), kMagic, sizeof(kMagic)) != 0) {
        Log::Loader("Page assets: " + Narrow(path) + " is not a .mgb package (no \"MAGMA\" magic)");
        return false;
    }

    uint32_t version = 0;
    memcpy(&version, header.data() + 9, sizeof(version));
    if (version != kExpectedVersion) {
        // The .mgb and .mgb.desc formats share one version epoch, and the engine rejects a
        // mismatch outright - so a package built for a different Magma build is worth naming here
        // rather than letting it fail deep inside the loader.
        char message[128];
        sprintf_s(message, "Page assets: fcse.mgb is version 0x%06X, this engine wants 0x%06X",
                  version, kExpectedVersion);
        Log::Loader(message);
        return false;
    }

    // The engine takes `char const*`. An install path with characters the active ANSI code page
    // cannot represent would arrive mangled, so decline rather than hand over a path that opens the
    // wrong file or nothing at all.
    std::string narrow = Narrow(path);
    if (narrow.empty() || narrow.find('?') != std::string::npos) {
        Log::Loader("Page assets: the path to fcse.mgb cannot be represented in the system ANSI "
                    "code page - move the game to a path without such characters");
        return false;
    }

    g_path = std::move(narrow);
    g_pathWide = std::move(path);
    g_available = true;
    Log::Loader("Page assets: fcse.mgb located at " + g_path);
    return true;
}

bool PageAssets::Available() {
    return g_available;
}

const std::string& PageAssets::PackagePath() {
    return g_path;
}

const std::wstring& PageAssets::PackagePathWide() {
    return g_pathWide;
}

} // namespace FCSE
