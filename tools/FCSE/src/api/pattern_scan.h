#pragma once

#include <cstddef>
#include <cstdint>
#include <string>
#include <vector>

// Byte-pattern scanning over Dunia.dll's code section.
//
// The address library is exact and verified, but it only knows the two builds it
// was generated from. A pattern is the opposite trade: nobody verified it, but it
// works on any build whose code still looks the same - including ones that were
// never mapped. Plugins ported from ASI mods are usually written this way, so
// FCSE offers it rather than making every author re-implement it.
//
// Two things this does that a hand-rolled scanner usually gets wrong:
//
//   - It scans `.text`, found from the module's own PE headers. The obvious
//     `duniaBase .. duniaBase + duniaSize` range is wrong: duniaSize is the file
//     size, which has no relationship to the mapped layout, and scanning data
//     sections can only produce false positives for a code pattern.
//   - It refuses on ambiguity. A pattern matching in several places and silently
//     returning the first is how a scan quietly patches the wrong function; the
//     match count comes back so the author can tighten the pattern instead.
namespace FCSE {

class PatternScan {
public:
    // Compiled pattern: `bytes[i]` matters only where `mask[i]` is true.
    struct Compiled {
        std::vector<uint8_t> bytes;
        std::vector<bool> mask;
        bool valid = false;
        std::string error;

        size_t size() const { return bytes.size(); }
    };

    // How lenient Compile is about wildcard spelling. The plugin C API takes `??`
    // only, so a pattern's written width always equals its byte length; the Lua
    // surface has always also accepted a lone `?` and keeps doing so.
    enum class Wildcards {
        DoubleOnly,
        AllowSingle,
    };

    // Parses IDA-style text: "8B 41 04 ?? 8B 40 4C". Case-insensitive,
    // whitespace-flexible. A pattern that is empty, malformed, or entirely
    // wildcards is rejected with a reason - the last because it would match
    // everywhere and is always an authoring mistake.
    static Compiled Compile(const char* pattern, Wildcards wildcards = Wildcards::DoubleOnly);

    // Every offset in [data, data+size) where `pattern` matches, up to `limit`
    // (0 for no limit). Pure over a buffer so it can be tested without a game
    // attached.
    static std::vector<size_t> Search(const uint8_t* data, size_t size,
                                      const Compiled& pattern, size_t limit);

    // The plugin-facing entry point: compile, scan Dunia.dll's .text, and return
    // the address only when exactly one site matched. `outCount` (optional)
    // receives how many were found, capped at kMaxMatches.
    static uintptr_t Find(const char* pattern, uint32_t* outCount,
                          void* callerReturnAddress);

    static constexpr size_t kMaxMatches = 64;
};

} // namespace FCSE
