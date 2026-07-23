#include "log.h"

#include <cstdio>

namespace LooseMods {

namespace {
    HANDLE g_file = INVALID_HANDLE_VALUE;
    std::wstring g_moduleDir;

    void WriteRaw(const std::wstring& text) {
        if (g_file == INVALID_HANDLE_VALUE) {
            return;
        }
        DWORD written = 0;
        WriteFile(g_file, text.c_str(), static_cast<DWORD>(text.size() * sizeof(wchar_t)),
                  &written, nullptr);
    }
}

void Log::Init(HMODULE hModule) {
    if (g_file != INVALID_HANDLE_VALUE) {
        return; // already initialized
    }

    wchar_t path[MAX_PATH];
    DWORD len = GetModuleFileNameW(hModule, path, MAX_PATH);
    if (len == 0 || len == MAX_PATH) {
        return; // can't resolve our own path - logging stays disabled, hook still works
    }

    std::wstring modulePath(path, len);
    size_t slash = modulePath.find_last_of(L"\\/");
    g_moduleDir = (slash == std::wstring::npos) ? L"" : modulePath.substr(0, slash + 1);

    std::wstring logPath = g_moduleDir + L"modpatcher.log";

    // FILE_SHARE_READ so the file can be tailed/opened for viewing while the game is running.
    g_file = CreateFileW(logPath.c_str(), GENERIC_WRITE, FILE_SHARE_READ, nullptr,
                          CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (g_file == INVALID_HANDLE_VALUE) {
        return;
    }

    // UTF-16LE BOM so Notepad/editors detect the encoding correctly.
    const wchar_t bom = 0xFEFF;
    DWORD written = 0;
    WriteFile(g_file, &bom, sizeof(bom), &written, nullptr);

    WriteRaw(L"[Init] LooseMods hook DLL loaded, log opened.\r\n");
}

void Log::Shutdown() {
    if (g_file != INVALID_HANDLE_VALUE) {
        WriteRaw(L"[Shutdown] LooseMods hook DLL unloading.\r\n");
        CloseHandle(g_file);
        g_file = INVALID_HANDLE_VALUE;
    }
}

const std::wstring& Log::ModuleDirectory() {
    return g_moduleDir;
}

void Log::Write(const wchar_t* level, const std::wstring& message) {
    if (g_file == INVALID_HANDLE_VALUE) {
        return;
    }

    SYSTEMTIME t;
    GetLocalTime(&t);

    wchar_t prefix[64];
    swprintf_s(prefix, L"[%02u:%02u:%02u.%03u][%s] ", t.wHour, t.wMinute, t.wSecond,
               t.wMilliseconds, level);

    WriteRaw(prefix);
    WriteRaw(message);
    WriteRaw(L"\r\n");
}

void Log::Info(const std::wstring& message) { Write(L"INFO", message); }
void Log::Warn(const std::wstring& message) { Write(L"WARN", message); }
void Log::Error(const std::wstring& message) { Write(L"ERROR", message); }

} // namespace LooseMods
