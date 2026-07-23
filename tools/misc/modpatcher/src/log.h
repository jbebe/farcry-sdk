#pragma once

#include <string>
#include <windows.h>

// Flat-file logger, modpatcher.log next to this DLL. Deliberately minimal - no dynamic
// allocation-heavy formatting libraries, this runs inside a hooked game process.
namespace LooseMods {

class Log {
public:
    // Opens modpatcher.log next to hModule (this DLL's own module handle, as passed to DllMain).
    // Safe to call once from DLL_PROCESS_ATTACH; no-op if already initialized or if the file
    // can't be opened.
    static void Init(HMODULE hModule);
    static void Shutdown();

    // Directory this DLL was loaded from, with a trailing backslash. Empty if Init hasn't run
    // or GetModuleFileNameW failed.
    static const std::wstring& ModuleDirectory();

    static void Info(const std::wstring& message);
    static void Warn(const std::wstring& message);
    static void Error(const std::wstring& message);

private:
    static void Write(const wchar_t* level, const std::wstring& message);
};

} // namespace LooseMods
