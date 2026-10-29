// Reading the engine's objects and hooking its functions.
#pragma once

#include "fcse_api.h"

#include <algorithm>
#include <cstddef>
#include <cstdint>
#include <string>

namespace InventoryOverhaul {

// The value of type T at `offset` into an engine object.
template <class T>
T Read(const void* object, ptrdiff_t offset) {
    return *reinterpret_cast<const T*>(static_cast<const uint8_t*>(object) + offset);
}

template <class T>
void Write(void* object, ptrdiff_t offset, T value) {
    *reinterpret_cast<T*>(static_cast<uint8_t*>(object) + offset) = value;
}

// The engine's std::string or std::wstring: an allocator word, the text inline until it outgrows 16
// bytes, its length and its capacity.
template <class Char>
struct EngineString {
    static constexpr uint32_t kInline = 16 / sizeof(Char);

    void* allocator;
    union {
        Char inlineText[kInline];
        const Char* text;
    };
    uint32_t length;
    uint32_t capacity;

    // A string the engine reads `text` from, without copying it.
    static EngineString Of(const Char* text, size_t length) {
        EngineString string{};
        string.text = text;
        string.length = static_cast<uint32_t>(length);
        string.capacity = std::max(string.length, kInline);
        return string;
    }

    const Char* Text() const { return capacity < kInline ? inlineText : text; }
};
static_assert(sizeof(EngineString<char>) == 0x1C);

inline std::string ReadString(const void* string) {
    const auto* engine = static_cast<const EngineString<char>*>(string);
    return std::string(engine->Text(), engine->length);
}

// The virtual function in `slot` of `object`'s vtable.
template <class Fn>
Fn Method(const void* object, size_t slot) {
    return (*reinterpret_cast<Fn* const*>(object))[slot];
}

// A class id the engine builds on first use: `build` makes it while its name is still unset.
using BuildClassIdFn = void(__fastcall*)(void* unused, void* unused2);
inline const uint32_t* ClassId(const FCSE::Relocation<BuildClassIdFn>& build,
                               const FCSE::Relocation<const void* const*>& name,
                               const FCSE::Relocation<const uint32_t*>& id) {
    if (*name.get() == nullptr) {
        build(nullptr, nullptr);
    }
    return id.get();
}

// Hooks the engine function `site`, keeping what it called in `original`. False when the function
// was not found, which is not logged here, or when FCSE refused it, which FCSE logs.
template <class Fn>
bool Hook(const FCSE::Relocation<Fn>& site, Fn detour, Fn* original) {
    return site && FCSE::ApiPointer()->Hook(reinterpret_cast<void*>(site.address()),
                                            reinterpret_cast<void*>(detour),
                                            reinterpret_cast<void**>(original));
}

}
