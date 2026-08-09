#include "crash_log.h"

#include "log.h"

#include <cstdio>
#include <cstring>
#include <windows.h>

namespace FCSE {

namespace {
    void* g_handle = nullptr;

    // Guards against a fault raised by the handler's own logging turning into infinite recursion.
    // Not thread-safe by design: a second thread faulting while this is set loses its report, which
    // beats two handlers interleaving lines into the same file.
    bool g_inHandler = false;

    // Enough to catch the interesting one and its immediate aftermath, few enough that a
    // first-chance-heavy path cannot fill the disk. Raised only if a real investigation needs it.
    constexpr int kMaxReports = 8;
    int g_reports = 0;

    // How far up the stack to look for return addresses, in dwords. 512 covers a deep menu dispatch
    // without walking into another thread's territory.
    constexpr int kStackScanDwords = 512;
    constexpr int kMaxFramesLogged = 24;

    // A vectored handler sees every exception in the process, first chance, and most of them are a
    // normal part of a running Windows program - so the filter has to be a rule, not a blocklist. An
    // earlier blocklist version let RPC status codes from the network stack through, and they used
    // up the report budget before the access violation that actually mattered ever reached the log.
    //
    // NTSTATUS puts severity in the top two bits, and only 0b11 means "error". That admits every
    // STATUS_* fault worth seeing (access violation, illegal instruction, stack overflow, …) and
    // rejects informational and warning codes, plain Win32 error values raised as SEH, and the
    // debugger chatter, all without naming any of them. C++ exceptions carry the error severity too
    // and have to be excluded by name: a `throw` is control flow, not a crash.
    constexpr DWORD kCppExceptionCode = 0xE06D7363; // 'msc' - MSVC's throw

    bool IsFatal(DWORD code) {
        return (code & 0xC0000000u) == 0xC0000000u && code != kCppExceptionCode;
    }

    const char* CodeName(DWORD code) {
        switch (code) {
        case EXCEPTION_ACCESS_VIOLATION:
            return "ACCESS_VIOLATION";
        case EXCEPTION_ILLEGAL_INSTRUCTION:
            return "ILLEGAL_INSTRUCTION";
        case EXCEPTION_PRIV_INSTRUCTION:
            return "PRIV_INSTRUCTION";
        case EXCEPTION_INT_DIVIDE_BY_ZERO:
            return "INT_DIVIDE_BY_ZERO";
        case EXCEPTION_STACK_OVERFLOW:
            return "STACK_OVERFLOW";
        case EXCEPTION_ARRAY_BOUNDS_EXCEEDED:
            return "ARRAY_BOUNDS_EXCEEDED";
        case EXCEPTION_DATATYPE_MISALIGNMENT:
            return "DATATYPE_MISALIGNMENT";
        case EXCEPTION_IN_PAGE_ERROR:
            return "IN_PAGE_ERROR";
        default:
            return "exception";
        }
    }

    // `module+0xRVA`, which is the form that pastes straight into Ghidra's or IDA's goto-address
    // box once the module is loaded at its preferred base. Falls back to a bare address for
    // anything that is not inside a loaded module - a heap or stack address, typically, which is
    // itself the useful signal.
    void DescribeAddress(const void* address, char* out, size_t outSize) {
        HMODULE module = nullptr;
        if (GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                                    GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                                reinterpret_cast<LPCWSTR>(address), &module) == 0 ||
            module == nullptr) {
            std::snprintf(out, outSize, "0x%08X (not in a loaded module)",
                          static_cast<unsigned>(reinterpret_cast<uintptr_t>(address)));
            return;
        }

        wchar_t path[MAX_PATH] = {};
        const wchar_t* name = L"?";
        if (GetModuleFileNameW(module, path, MAX_PATH) != 0) {
            const wchar_t* slash = std::wcsrchr(path, L'\\');
            name = slash != nullptr ? slash + 1 : path;
        }

        char narrow[MAX_PATH] = {};
        WideCharToMultiByte(CP_UTF8, 0, name, -1, narrow, sizeof(narrow) - 1, nullptr, nullptr);

        uintptr_t rva = reinterpret_cast<uintptr_t>(address) - reinterpret_cast<uintptr_t>(module);
        std::snprintf(out, outSize, "%s+0x%X  (loaded at 0x%08X, so this instruction is 0x%08X)",
                      narrow, static_cast<unsigned>(rva),
                      static_cast<unsigned>(reinterpret_cast<uintptr_t>(module)),
                      static_cast<unsigned>(reinterpret_cast<uintptr_t>(address)));
    }

    bool SafeReadDword(const void* address, DWORD* out) {
        __try {
            *out = *static_cast<const DWORD*>(address);
            return true;
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            return false;
        }
    }

    // A scan, not a walk. Far Cry 2 is a 2008 MSVC x86 build with frame-pointer omission all over
    // it, so an EBP chain runs out immediately and StackWalk64 needs symbols FCSE does not have.
    // Reading every dword above ESP and keeping the ones that point into a loaded module's code
    // recovers the call path well enough to name the function that faulted and who called it - with
    // some false positives, which is why the output says "candidate".
    void LogStackCandidates(const CONTEXT* context) {
        Log::Loader("  return-address candidates, nearest first (stale values are possible - these "
                    "are scanned off the stack, not walked):");

        auto stack = reinterpret_cast<const DWORD*>(static_cast<uintptr_t>(context->Esp));
        int logged = 0;
        for (int i = 0; i < kStackScanDwords && logged < kMaxFramesLogged; ++i) {
            DWORD value = 0;
            if (!SafeReadDword(stack + i, &value)) {
                break; // ran off the end of the committed stack
            }
            if (value < 0x10000) {
                continue;
            }

            HMODULE module = nullptr;
            if (GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                                        GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                                    reinterpret_cast<LPCWSTR>(static_cast<uintptr_t>(value)),
                                    &module) == 0) {
                continue;
            }

            char described[320];
            DescribeAddress(reinterpret_cast<const void*>(static_cast<uintptr_t>(value)), described,
                            sizeof(described));
            char line[400];
            std::snprintf(line, sizeof(line), "    [esp+0x%03X]  %s", i * 4, described);
            Log::Loader(line);
            ++logged;
        }

        if (logged == 0) {
            Log::Loader("    (none - the stack pointer itself is probably bad)");
        }
    }

    void Report(const EXCEPTION_RECORD* record, const CONTEXT* context) {
        char described[320];
        DescribeAddress(record->ExceptionAddress, described, sizeof(described));

        char line[512];
        std::snprintf(line, sizeof(line), "CRASH: %s (0x%08X) at %s", CodeName(record->ExceptionCode),
                      static_cast<unsigned>(record->ExceptionCode), described);
        Log::Loader(line);

        if (record->ExceptionCode == EXCEPTION_ACCESS_VIOLATION &&
            record->NumberParameters >= 2) {
            const char* what = record->ExceptionInformation[0] == 0   ? "reading"
                               : record->ExceptionInformation[0] == 1 ? "writing"
                                                                      : "executing";
            std::snprintf(line, sizeof(line), "  while %s 0x%08X", what,
                          static_cast<unsigned>(record->ExceptionInformation[1]));
            Log::Loader(line);
        }

        std::snprintf(line, sizeof(line),
                      "  eip=%08X esp=%08X ebp=%08X  eax=%08X ecx=%08X edx=%08X",
                      static_cast<unsigned>(context->Eip), static_cast<unsigned>(context->Esp),
                      static_cast<unsigned>(context->Ebp), static_cast<unsigned>(context->Eax),
                      static_cast<unsigned>(context->Ecx), static_cast<unsigned>(context->Edx));
        Log::Loader(line);
        std::snprintf(line, sizeof(line), "  ebx=%08X esi=%08X edi=%08X",
                      static_cast<unsigned>(context->Ebx), static_cast<unsigned>(context->Esi),
                      static_cast<unsigned>(context->Edi));
        Log::Loader(line);

        // ECX is `this` for every __thiscall the engine makes, so on a settings-page fault it is
        // usually the page or the CUISettingBase - worth calling out rather than leaving to be
        // spotted in the register dump.
        LogStackCandidates(context);
    }

    LONG CALLBACK Handler(EXCEPTION_POINTERS* info) {
        if (info == nullptr || info->ExceptionRecord == nullptr || info->ContextRecord == nullptr) {
            return EXCEPTION_CONTINUE_SEARCH;
        }
        DWORD code = info->ExceptionRecord->ExceptionCode;
        if (!IsFatal(code) || g_inHandler || g_reports >= kMaxReports) {
            return EXCEPTION_CONTINUE_SEARCH;
        }

        g_inHandler = true;
        ++g_reports;
        if (g_reports == kMaxReports) {
            Log::Loader("CrashLog: report limit reached - further exceptions go unlogged this run");
        }
        Report(info->ExceptionRecord, info->ContextRecord);
        g_inHandler = false;

        // Always. This handler exists to observe, never to swallow: a fault the engine would have
        // handled still gets handled, and one it would not still ends the process exactly as before.
        return EXCEPTION_CONTINUE_SEARCH;
    }
}

void CrashLog::Install() {
    if (g_handle != nullptr) {
        return;
    }
    // First in the chain (1 = front), so the report is written before any handler that might swallow
    // the exception or tear the process down.
    g_handle = AddVectoredExceptionHandler(1, &Handler);
    Log::Loader(g_handle != nullptr
                    ? "CrashLog: installed - a fault will be logged as module+RVA before the game "
                      "goes down"
                    : "CrashLog: AddVectoredExceptionHandler failed - a crash this run will leave "
                      "nothing behind");
}

void CrashLog::Shutdown() {
    if (g_handle != nullptr) {
        RemoveVectoredExceptionHandler(g_handle);
        g_handle = nullptr;
    }
}

} // namespace FCSE
