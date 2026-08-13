#include "log.h"

#include "caller_identity.h"

#include <cstdio>
#include <windows.h>

namespace FCSE {

namespace {
    HANDLE g_file = INVALID_HANDLE_VALUE;
    SRWLOCK g_writeLock = SRWLOCK_INIT;

    // Set while this thread is inside WriteLine. The crash handler logs from the faulting thread,
    // which may be the thread that was already writing - and an SRW lock is not recursive, so
    // taking it again would deadlock inside the one handler that has to keep working.
    thread_local bool t_writing = false;

    // One line, one WriteFile. The handle stays unbuffered so a line that was written is on disk
    // even if the process dies immediately after.
    void WriteLine(const std::string& text) {
        if (g_file == INVALID_HANDLE_VALUE) {
            return;
        }
        DWORD written = 0;
        if (t_writing) {
            WriteFile(g_file, text.data(), static_cast<DWORD>(text.size()), &written, nullptr);
            return;
        }

        t_writing = true;
        AcquireSRWLockExclusive(&g_writeLock);
        WriteFile(g_file, text.data(), static_cast<DWORD>(text.size()), &written, nullptr);
        ReleaseSRWLockExclusive(&g_writeLock);
        t_writing = false;
    }
}

void Log::Init(const std::wstring& directory) {
    if (g_file != INVALID_HANDLE_VALUE) {
        return; // already initialized
    }
    if (directory.empty()) {
        return; // no resolved path - logging stays disabled, loading still proceeds
    }

    const std::wstring logPath = directory + L"fcse.log";

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
    const int formatted =
        std::snprintf(prefix, sizeof(prefix), "[%04u-%02u-%02u %02u:%02u:%02u.%07llu0][%s] ",
                      st.wYear, st.wMonth, st.wDay, st.wHour, st.wMinute, st.wSecond,
                      fractional100ns, tag.c_str());
    if (formatted < 0) {
        return;
    }
    // snprintf reports the length it wanted, which a long tag can push past the buffer.
    const size_t wanted = static_cast<size_t>(formatted);
    const size_t prefixLength = wanted < sizeof(prefix) ? wanted : sizeof(prefix) - 1;

    std::string line;
    line.reserve(prefixLength + message.size() + 2);
    line.append(prefix, prefixLength);
    line.append(message);
    line.append("\r\n");
    WriteLine(line);
}

} // namespace FCSE
