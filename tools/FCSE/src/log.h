#pragma once

#include <string>
#include <windows.h>

// Flat-file logger for bin\fcse.log. Every line - whether written by FCSE.exe itself or by a
// plugin through FCSE_PluginAPI::Log - goes through Write(), so the format can never drift
// between the two sources. Deliberately minimal (no formatting libraries): this runs early in the
// game process, before anything else has had a chance to allocate much.
namespace FCSE {

class Log {
public:
    // Opens bin\fcse.log next to hLoaderModule (truncates any previous run's log). Safe to call
    // once, before anything else in the loader runs.
    static void Init(HMODULE hLoaderModule);
    static void Shutdown();

    // Directory the loader itself was loaded from, with a trailing backslash. Empty if Init
    // hasn't run or GetModuleFileNameW failed.
    static const std::wstring& LoaderDirectory();

    // One line, tagged "[fcse]" - for the loader's own lifecycle messages.
    static void Loader(const std::string& message);

    // One line, tagged with the calling module's own name (resolved via caller_identity.h from
    // the caller's return address) - backs FCSE_PluginAPI::Log so a plugin never has to pass its
    // own identity in and can't get the tag wrong. `returnAddress` is the plugin's call site,
    // typically `_ReturnAddress()` captured at the FCSE_LogFn trampoline.
    static void FromCaller(void* returnAddress, const std::string& message);

private:
    // Core line writer: "[yyyy-MM-dd HH:mm:ss.ffffffff][tag] message\r\n".
    static void Write(const std::string& tag, const std::string& message);
};

} // namespace FCSE
