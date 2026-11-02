// The combat log: every line also goes to fcse.log, and to this session's file in
// bin\difficulty-overhaul-logs, which keeps the last sessions that fcse.log loses at each launch.
#include "engine/combat_log.h"

#include "fcse_api.h"

#include <windows.h>

#include <algorithm>
#include <cstdarg>
#include <cstdio>
#include <filesystem>
#include <share.h>
#include <vector>

namespace {
    // How many session files the folder keeps, this one included.
    constexpr size_t kKeptSessions = 20;

    // Opens this session's file next to the game's executable, removing the oldest beyond the kept.
    FILE* Open() {
        wchar_t exe[MAX_PATH];
        if (GetModuleFileNameW(nullptr, exe, MAX_PATH) == 0) {
            return nullptr;
        }
        std::error_code error;
        const auto folder = std::filesystem::path(exe).parent_path() / L"difficulty-overhaul-logs";
        std::filesystem::create_directories(folder, error);

        std::vector<std::filesystem::path> sessions;
        for (const auto& entry : std::filesystem::directory_iterator(folder, error)) {
            if (entry.path().extension() == L".log") {
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
}

namespace DifficultyOverhaul {

void CombatLogf(const char* format, ...) {
    char line[1024];
    va_list args;
    va_start(args, format);
    const int length = std::vsnprintf(line, sizeof(line), format, args);
    va_end(args);
    if (length < 0) {
        return;
    }
    FCSE::ApiPointer()->Log(line);

    static FILE* const file = Open();
    if (file == nullptr) {
        return;
    }
    SYSTEMTIME now;
    GetLocalTime(&now);
    std::fprintf(file, "%02u:%02u:%02u.%03u %s\n", now.wHour, now.wMinute, now.wSecond,
                 now.wMilliseconds, line);
    std::fflush(file);
}

}
