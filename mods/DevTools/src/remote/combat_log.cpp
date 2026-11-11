#include "remote/combat_log.h"

#include "engine/bin_folder.h"
#include "fcse_api.h"
#include "remote/hit_log.h"

#include <windows.h>

#include <algorithm>
#include <cstdio>
#include <cstring>
#include <filesystem>
#include <share.h>
#include <vector>

namespace {
    constexpr char kSwitch[] = "-combatlog";

    // How many combat logs the folder keeps, this one included.
    constexpr size_t kKeptSessions = 20;

    FILE* g_file = nullptr;

    // Opens this session's file, removing the oldest beyond the kept.
    FILE* Open() {
        std::error_code error;
        const auto folder = DevTools::BinFolder::File(L"devtools-logs");
        std::filesystem::create_directories(folder, error);

        std::vector<std::filesystem::path> sessions;
        for (const auto& entry : std::filesystem::directory_iterator(folder, error)) {
            const auto name = entry.path().filename().wstring();
            if (name.starts_with(L"combat-") && name.ends_with(L".log")) {
                sessions.push_back(entry.path());
            }
        }
        std::sort(sessions.begin(), sessions.end());
        for (size_t i = 0; i + kKeptSessions <= sessions.size(); ++i) {
            std::filesystem::remove(sessions[i], error);
        }

        SYSTEMTIME now;
        GetLocalTime(&now);
        wchar_t name[64];
        swprintf_s(name, L"combat-%04u-%02u-%02u_%02u-%02u-%02u.log", now.wYear, now.wMonth,
                   now.wDay, now.wHour, now.wMinute, now.wSecond);
        return _wfsopen((folder / name).c_str(), L"w", _SH_DENYWR);
    }

    // One line, stamped with the local time and its sender. Senders are on any thread; each
    // fprintf holds the stream's lock, so lines never interleave.
    void OnLine(const char* sender, const void* data, size_t size, void*) {
        const auto* text = static_cast<const char*>(data);
        const size_t length = text != nullptr ? strnlen(text, size) : 0;

        SYSTEMTIME now;
        GetLocalTime(&now);
        std::fprintf(g_file, "%02u:%02u:%02u.%03u [%s] %.*s\n", now.wHour, now.wMinute,
                     now.wSecond, now.wMilliseconds, sender, static_cast<int>(length), text);
        std::fflush(g_file);
    }
}

namespace DevTools::CombatLog {

void Start() {
    if (std::strstr(GetCommandLineA(), kSwitch) == nullptr) {
        return;
    }

    const FCSE_PluginAPI* api = FCSE::ApiPointer();
    g_file = Open();
    if (g_file == nullptr) {
        api->Log("combat log: bin\\devtools-logs could not be written - off");
        return;
    }
    api->Listen("combat", &OnLine, nullptr);
    HitLog::Install();
    api->Log("combat log: on - every combat line goes to a file in bin\\devtools-logs");
}

}
