#include "caller_identity.h"

#include <windows.h>

#include <cstdio>

namespace FCSE {

namespace {
    // Thread-local, though the Lua interpreter only ever runs on the game thread: an override left
    // visible to another thread would silently mistag that thread's log lines, and the cost of
    // ruling that out is one keyword.
    thread_local std::string g_identityOverride;
    thread_local bool g_hasIdentityOverride = false;
}

ScopedCallerIdentity::ScopedCallerIdentity(const std::string& name)
    : previous_(g_identityOverride), hadPrevious_(g_hasIdentityOverride) {
    g_identityOverride = name;
    g_hasIdentityOverride = true;
}

ScopedCallerIdentity::~ScopedCallerIdentity() {
    g_identityOverride = previous_;
    g_hasIdentityOverride = hadPrevious_;
}

std::string ResolveCallerModuleName(void* returnAddress) {
    if (g_hasIdentityOverride) {
        return g_identityOverride;
    }

    HMODULE hModule = nullptr;
    BOOL ok = GetModuleHandleExW(
        GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        static_cast<LPCWSTR>(returnAddress), &hModule);

    // Always tag the loader's own code "fcse" - matching Log::Loader()'s hardcoded tag exactly -
    // regardless of what FCSE.exe's on-disk filename happens to be (a user could rename it; the
    // module comparison below still identifies "this process' own main module" correctly either
    // way, since GetModuleHandleW(nullptr) always means the current process' own exe).
    if (ok && hModule == GetModuleHandleW(nullptr)) {
        return "fcse";
    }

    if (ok && hModule != nullptr) {
        wchar_t path[MAX_PATH];
        DWORD len = GetModuleFileNameW(hModule, path, MAX_PATH);
        if (len > 0 && len < MAX_PATH) {
            std::wstring wpath(path, len);
            size_t slash = wpath.find_last_of(L"\\/");
            std::wstring fileName = (slash == std::wstring::npos) ? wpath : wpath.substr(slash + 1);
            size_t dot = fileName.find_last_of(L'.');
            if (dot != std::wstring::npos) {
                fileName = fileName.substr(0, dot);
            }

            // Narrow via the system codepage - plugin/loader file names are expected to be plain
            // ASCII, so this is a lossless conversion in practice.
            int narrowLen = WideCharToMultiByte(CP_ACP, 0, fileName.c_str(),
                                                 static_cast<int>(fileName.size()), nullptr, 0,
                                                 nullptr, nullptr);
            if (narrowLen > 0) {
                std::string result(narrowLen, '\0');
                WideCharToMultiByte(CP_ACP, 0, fileName.c_str(), static_cast<int>(fileName.size()),
                                    result.data(), narrowLen, nullptr, nullptr);
                return result;
            }
        }
    }

    char fallback[32];
    std::snprintf(fallback, sizeof(fallback), "0x%p", returnAddress);
    return fallback;
}

} // namespace FCSE
