#include "log.h"

#include "caller_identity.h"

#include <cstdio>

namespace FCSE {

namespace {
    HANDLE g_file = INVALID_HANDLE_VALUE;
    std::wstring g_loaderDir;

    void WriteRaw(const std::string& text) {
        if (g_file == INVALID_HANDLE_VALUE) {
            return;
        }
        DWORD written = 0;
        WriteFile(g_file, text.data(), static_cast<DWORD>(text.size()), &written, nullptr);
    }
}

void Log::Init(HMODULE hLoaderModule) {
    if (g_file != INVALID_HANDLE_VALUE) {
        return; // already initialized
    }

    wchar_t path[MAX_PATH];
    DWORD len = GetModuleFileNameW(hLoaderModule, path, MAX_PATH);
    if (len == 0 || len == MAX_PATH) {
        return; // can't resolve our own path - logging stays disabled, loading still proceeds
    }

    std::wstring modulePath(path, len);
    size_t slash = modulePath.find_last_of(L"\\/");
    g_loaderDir = (slash == std::wstring::npos) ? L"" : modulePath.substr(0, slash + 1);

    std::wstring logPath = g_loaderDir + L"fcse.log";

    // FILE_SHARE_READ so the file can be tailed/opened for viewing while the game is running.
    // CREATE_ALWAYS truncates any previous run's log - each launch gets a fresh file.
    g_file = CreateFileW(logPath.c_str(), GENERIC_WRITE, FILE_SHARE_READ, nullptr, CREATE_ALWAYS,
                          FILE_ATTRIBUTE_NORMAL, nullptr);
    if (g_file == INVALID_HANDLE_VALUE) {
        return;
    }

    Loader("fcse.log opened");
}

void Log::Shutdown() {
    if (g_file != INVALID_HANDLE_VALUE) {
        Loader("shutting down");
        CloseHandle(g_file);
        g_file = INVALID_HANDLE_VALUE;
    }
}

const std::wstring& Log::LoaderDirectory() {
    return g_loaderDir;
}

void Log::Loader(const std::string& message) {
    Write("fcse", message);
}

void Log::FromCaller(void* returnAddress, const std::string& message) {
    Write(ResolveCallerModuleName(returnAddress), message);
}

void Log::Write(const std::string& tag, const std::string& message) {
    if (g_file == INVALID_HANDLE_VALUE) {
        return;
    }

    // 100ns-resolution local timestamp (Windows FILETIME's native tick size) so lines from the
    // loader and multiple plugins interleaving within the same millisecond stay distinguishable -
    // GetLocalTime()-based millisecond timestamps (what modpatcher's logger uses) aren't fine
    // enough for that. Formatted as 8 fractional digits: 7 real 100ns digits plus one padding
    // zero, matching the requested "[yyyy-MM-dd HH:mm:ss.ffffffff]" line shape.
    FILETIME utc;
    GetSystemTimePreciseAsFileTime(&utc);
    FILETIME local;
    FileTimeToLocalFileTime(&utc, &local);
    SYSTEMTIME st;
    FileTimeToSystemTime(&local, &st);

    ULARGE_INTEGER ticks;
    ticks.LowPart = local.dwLowDateTime;
    ticks.HighPart = local.dwHighDateTime;
    unsigned long long fractional100ns = ticks.QuadPart % 10000000ULL;

    char prefix[80];
    std::snprintf(prefix, sizeof(prefix), "[%04u-%02u-%02u %02u:%02u:%02u.%07llu0][%s] ", st.wYear,
                  st.wMonth, st.wDay, st.wHour, st.wMinute, st.wSecond, fractional100ns,
                  tag.c_str());

    WriteRaw(prefix);
    WriteRaw(message);
    WriteRaw("\r\n");
}

} // namespace FCSE
