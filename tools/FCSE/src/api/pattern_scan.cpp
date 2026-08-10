#include "api/pattern_scan.h"

#include <cstring>

#include "engine/dunia_api.h"
#include "log.h"

namespace FCSE {

namespace {

    int HexDigit(char c) {
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'a' && c <= 'f') return c - 'a' + 10;
        if (c >= 'A' && c <= 'F') return c - 'A' + 10;
        return -1;
    }

    bool IsSpace(char c) {
        return c == ' ' || c == '\t' || c == '\r' || c == '\n';
    }

} // namespace

PatternScan::Compiled PatternScan::Compile(const char* pattern) {
    Compiled out;
    if (pattern == nullptr) {
        out.error = "pattern is null";
        return out;
    }

    size_t fixed = 0;
    for (const char* p = pattern; *p != '\0';) {
        if (IsSpace(*p)) {
            ++p;
            continue;
        }
        if (*p == '?') {
            // "??" and nothing else. Two characters per byte, exactly like the
            // hex it stands in for, so a pattern lines up column-for-column
            // whether or not it has wildcards in it. Accepting "?" as well would
            // mean two spellings for one thing and a pattern that reads as one
            // width but parses as another.
            if (*(p + 1) != '?') {
                out.error = "a lone '?' is not a wildcard - write '??', one per byte";
                return out;
            }
            p += 2;
            out.bytes.push_back(0);
            out.mask.push_back(false);
            continue;
        }
        if (*p == '*') {
            out.error = "'*' is not a wildcard here - write '??'";
            return out;
        }
        const int hi = HexDigit(*p);
        if (hi < 0) {
            out.error = std::string("unexpected character '") + *p + "'";
            return out;
        }
        const int lo = HexDigit(*(p + 1));
        if (lo < 0) {
            // A single hex digit is far more likely a typo for a byte than an
            // intentional nibble, so it is rejected rather than guessed at.
            out.error = std::string("'") + *p + "' is a lone hex digit; bytes "
                        "need two (use ?? for a wildcard)";
            return out;
        }
        out.bytes.push_back(static_cast<uint8_t>((hi << 4) | lo));
        out.mask.push_back(true);
        ++fixed;
        p += 2;
    }

    if (out.bytes.empty()) {
        out.error = "pattern is empty";
        return out;
    }
    if (fixed == 0) {
        out.error = "pattern is all wildcards, which matches everywhere";
        return out;
    }
    out.valid = true;
    return out;
}

std::vector<size_t> PatternScan::Search(const uint8_t* data, size_t size,
                                        const Compiled& pattern, size_t limit) {
    std::vector<size_t> hits;
    if (!pattern.valid || data == nullptr || size < pattern.size()) {
        return hits;
    }

    // Anchor on the first fixed byte so the common case is a memchr rather than
    // a byte-by-byte walk. .text is ~14 MB and a plugin may scan for dozens of
    // patterns at load, so the difference is seconds of startup, not noise.
    size_t anchor = 0;
    while (anchor < pattern.size() && !pattern.mask[anchor]) {
        ++anchor;
    }
    const uint8_t anchorByte = pattern.bytes[anchor];
    const size_t last = size - pattern.size();

    size_t at = 0;
    while (at <= last) {
        const void* found = std::memchr(data + at + anchor, anchorByte,
                                        last - at + 1);
        if (found == nullptr) {
            break;
        }
        const size_t start = static_cast<size_t>(
            static_cast<const uint8_t*>(found) - data) - anchor;

        bool ok = true;
        for (size_t i = 0; i < pattern.size(); ++i) {
            if (pattern.mask[i] && data[start + i] != pattern.bytes[i]) {
                ok = false;
                break;
            }
        }
        if (ok) {
            hits.push_back(start);
            if (hits.size() >= limit) {
                break;
            }
        }
        at = start + 1;
    }
    return hits;
}

bool PatternScan::CodeSection(HMODULE module, const uint8_t** begin, size_t* size) {
    if (module == nullptr) {
        return false;
    }
    const auto* base = reinterpret_cast<const uint8_t*>(module);
    const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE) {
        return false;
    }
    const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS*>(base + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE) {
        return false;
    }

    const auto* section = IMAGE_FIRST_SECTION(nt);
    for (unsigned i = 0; i < nt->FileHeader.NumberOfSections; ++i, ++section) {
        // By characteristics rather than by the name ".text": the name is a
        // convention, the executable flag is what actually decides whether code
        // can live there.
        if ((section->Characteristics & IMAGE_SCN_MEM_EXECUTE) == 0) {
            continue;
        }
        const DWORD extent = section->Misc.VirtualSize != 0 ? section->Misc.VirtualSize
                                                            : section->SizeOfRawData;
        if (extent == 0) {
            continue;
        }
        *begin = base + section->VirtualAddress;
        *size = extent;
        return true;
    }
    return false;
}

uintptr_t PatternScan::Find(const char* pattern, uint32_t* outCount,
                            void* callerReturnAddress) {
    if (outCount != nullptr) {
        *outCount = 0;
    }

    const Compiled compiled = Compile(pattern);
    if (!compiled.valid) {
        Log::FromCaller(callerReturnAddress,
                        std::string("FindPattern: rejected pattern - ") + compiled.error);
        return 0;
    }

    const uint8_t* code = nullptr;
    size_t codeSize = 0;
    if (!CodeSection(DuniaApi::Module(), &code, &codeSize)) {
        Log::FromCaller(callerReturnAddress,
                        "FindPattern: could not locate Dunia.dll's code section");
        return 0;
    }

    const std::vector<size_t> hits = Search(code, codeSize, compiled, kMaxMatches);
    if (outCount != nullptr) {
        *outCount = static_cast<uint32_t>(hits.size());
    }

    if (hits.empty()) {
        Log::FromCaller(callerReturnAddress,
                        std::string("FindPattern: no match for \"") + pattern +
                            "\" - this game build may differ, or the pattern is wrong");
        return 0;
    }
    if (hits.size() > 1) {
        // Returning the first match here is how a scan silently patches the
        // wrong function. Refusing costs the author one round of tightening the
        // pattern and costs a player nothing.
        Log::FromCaller(callerReturnAddress,
                        std::string("FindPattern: \"") + pattern + "\" matched " +
                            std::to_string(hits.size()) +
                            (hits.size() >= kMaxMatches ? "+" : "") +
                            " places - refusing to guess which. Lengthen the "
                            "pattern until it is unique.");
        return 0;
    }
    return reinterpret_cast<uintptr_t>(code + hits[0]);
}

} // namespace FCSE
