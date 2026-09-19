#include "log.h"

#include "caller_identity.h"

#include <cstdio>
#include <windows.h>

namespace FCSE {

namespace {
    struct Sink;

    // The sink this thread is inside WriteLine of. The crash handler logs from the faulting
    // thread, which may be the thread that was already writing - and an SRW lock is not recursive,
    // so taking it again would deadlock inside the one handler that has to keep working.
    thread_local const Sink* t_writing = nullptr;

    // One log file and the lock serialising writes to it.
    struct Sink {
        HANDLE file = INVALID_HANDLE_VALUE;
        SRWLOCK lock = SRWLOCK_INIT;

        bool Open(const std::wstring& path) {
            // FILE_SHARE_READ so the file can be tailed/opened for viewing while the game is
            // running. CREATE_ALWAYS truncates any previous run's log - each launch gets a fresh
            // file.
            file = CreateFileW(path.c_str(), GENERIC_WRITE, FILE_SHARE_READ, nullptr, CREATE_ALWAYS,
                               FILE_ATTRIBUTE_NORMAL, nullptr);
            return file != INVALID_HANDLE_VALUE;
        }

        void Close() {
            if (file != INVALID_HANDLE_VALUE) {
                CloseHandle(file);
                file = INVALID_HANDLE_VALUE;
            }
        }

        // One line, one WriteFile. The handle stays unbuffered so a line that was written is on
        // disk even if the process dies immediately after.
        void WriteLine(const std::string& text) {
            if (file == INVALID_HANDLE_VALUE) {
                return;
            }
            DWORD written = 0;
            if (t_writing == this) {
                WriteFile(file, text.data(), static_cast<DWORD>(text.size()), &written, nullptr);
                return;
            }

            const Sink* outer = t_writing;
            t_writing = this;
            AcquireSRWLockExclusive(&lock);
            WriteFile(file, text.data(), static_cast<DWORD>(text.size()), &written, nullptr);
            ReleaseSRWLockExclusive(&lock);
            t_writing = outer;
        }
    };

    Sink g_loader;
    Sink g_engine;

    // "[yyyy-MM-dd HH:mm:ss.ffffffff][tag] message\r\n", or empty if the prefix failed to format.
    std::string FormatLine(const std::string& tag, const std::string& message) {
        // 100ns-resolution local timestamp (Windows FILETIME's native tick size) so lines from the
        // loader and multiple plugins interleaving within the same millisecond stay
        // distinguishable - GetLocalTime()-based millisecond timestamps (what modpatcher's logger
        // uses) aren't fine enough for that. Formatted as 8 fractional digits: 7 real 100ns digits
        // plus one padding zero, matching the requested "[yyyy-MM-dd HH:mm:ss.ffffffff]" shape.
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
            return "";
        }
        // snprintf reports the length it wanted, which a long tag can push past the buffer.
        const size_t wanted = static_cast<size_t>(formatted);
        const size_t prefixLength = wanted < sizeof(prefix) ? wanted : sizeof(prefix) - 1;

        std::string line;
        line.reserve(prefixLength + message.size() + 2);
        line.append(prefix, prefixLength);
        line.append(message);
        line.append("\r\n");
        return line;
    }
}

void Log::Init(const std::wstring& directory) {
    if (g_loader.file != INVALID_HANDLE_VALUE) {
        return; // already initialized
    }
    if (directory.empty()) {
        return; // no resolved path - logging stays disabled, loading still proceeds
    }
    if (!g_loader.Open(directory + L"fcse.log")) {
        return;
    }
    Loader("fcse.log opened");

    if (g_engine.Open(directory + L"Dunia.log")) {
        Engine("fcse", "Dunia.log opened");
    } else {
        Loader("Dunia.log could not be opened - the engine's output is not captured");
    }
}

void Log::Shutdown() {
    Loader("shutting down");
    g_loader.Close();
    g_engine.Close();
}

void Log::Loader(const std::string& message) {
    Write("fcse", message);
}

void Log::FromCaller(void* returnAddress, const std::string& message) {
    Write(ResolveCallerModuleName(returnAddress), message);
}

void Log::Write(const std::string& tag, const std::string& message) {
    g_loader.WriteLine(FormatLine(tag, message));
}

void Log::Engine(const std::string& tag, const std::string& message) {
    g_engine.WriteLine(FormatLine(tag, message));
}

} // namespace FCSE
