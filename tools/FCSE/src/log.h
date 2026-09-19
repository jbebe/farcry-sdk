#pragma once

#include <string>

// Flat-file logging for the two files under bin\. fcse.log takes every line written by FCSE.exe
// itself or by a plugin through FCSE_PluginAPI::Log; Dunia.log takes what the engine writes to its
// own sinks (engine/dunia_log.h). Deliberately minimal (no formatting libraries): this runs early in the game process, before
// anything else has had a chance to allocate much.
//
// A line costs one synchronous unbuffered write and there is no rate limiting, so nothing on the
// per-frame path may log unconditionally.
namespace FCSE {

class Log {
public:
    // Opens fcse.log and Dunia.log in `directory` (truncating any previous run's). Safe to call
    // once, before anything else in the loader runs; an empty directory leaves logging disabled.
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

    // The same line shape, in Dunia.log. The tag names the engine sink the line came from.
    static void Engine(const std::string& tag, const std::string& message);
};

} // namespace FCSE
