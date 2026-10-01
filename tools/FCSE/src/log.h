#pragma once

#include <string>

// Flat-file logger for bin\fcse.log. Every line - whether written by FCSE.exe itself or by a
// plugin through FCSE_PluginAPI::Log - goes through Write(), so the format can never drift
// between the two sources. Deliberately minimal (no formatting libraries): this runs early in the
// game process, before anything else has had a chance to allocate much.
//
// A line costs one synchronous unbuffered write and there is no rate limiting, so nothing on the
// per-frame path may log unconditionally.
namespace FCSE {

class Log {
public:
    // Opens fcse.log in `directory` (truncating any previous run's). Safe to call once, before
    // anything else in the loader runs; an empty directory leaves logging disabled.
    static void Init(const std::wstring& directory);
    static void Shutdown();

    // One line, tagged "[fcse]" - for the loader's own lifecycle messages.
    static void Loader(const std::string& message);

    // One line, tagged with the calling module's own name (resolved via caller_identity.h from
    // the caller's return address) - backs FCSE_PluginAPI::Log so a plugin never has to pass its
    // own identity in and can't get the tag wrong. `returnAddress` is the plugin's call site,
    // typically `_ReturnAddress()` captured at the FCSE_LogFn trampoline.
    static void FromCaller(void* returnAddress, const std::string& message);

    // One line under an already-resolved tag: "[yyyy-MM-dd HH:mm:ss.ffffffff][tag] message\r\n".
    // For callers that resolved the owning module once and log several lines under it.
    static void Write(const std::string& tag, const std::string& message);
};

} // namespace FCSE
