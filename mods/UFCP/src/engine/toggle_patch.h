// A byte patch an option can turn on and off.
//
// Ported from FC2JackalFix (MIT, (c) 2026 Joshhhuaaa, TGP482) - source/core/common.ixx `raw_mem`.
#pragma once

#include <cstddef>
#include <cstdint>

namespace UFCP {

// Holds the bytes a site had before anything wrote to it, so the engine's own code can be put back
// when the player turns the option off. Resolve() must succeed before Set() does anything.
class TogglePatch {
public:
    // Every site this is used for is a branch byte or a two-byte NOP, so the storage is fixed and
    // an over-long patch is a compile error rather than a silent no-op.
    static constexpr size_t kMaxSize = 8;

    template <size_t N>
    explicit TogglePatch(const uint8_t (&patched)[N]) : m_address(0), m_size(N) {
        static_assert(N <= kMaxSize, "a toggleable patch is a branch byte or a short NOP run");
        for (size_t i = 0; i < N; ++i) {
            m_patched[i] = patched[i];
        }
    }

    // Reads and keeps the original bytes at `address`. False if the site was not found.
    bool Resolve(uintptr_t address);

    // Writes the patched bytes, or restores the engine's own. A no-op until Resolve() succeeds.
    void Set(bool on);

private:
    uintptr_t m_address;
    size_t m_size;
    uint8_t m_patched[kMaxSize];
    uint8_t m_original[kMaxSize];
};

}
