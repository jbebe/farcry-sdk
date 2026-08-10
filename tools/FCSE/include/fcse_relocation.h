// Typed, build-agnostic engine addresses for FCSE plugins - the C++ convenience layer over
// FCSE_PluginAPI::ResolveFrom.
//
// Far Cry 2 v1.03 shipped as two different PC builds whose Dunia.dll images place the same code at
// different addresses. A plugin that writes `api->duniaBase + 0x0081E9C0` works on exactly one of
// them and jumps into unrelated code on the other. This header is how a plugin stops caring:
//
//     // the same function, named from whichever build you happened to open
//     FCSE::Relocation<void(__thiscall*)(void*)> Ctor{ FCSE::Uplay (0x0081E9C0) };
//     FCSE::Relocation<void(__thiscall*)(void*)> Same{ FCSE::Retail(0x00811C00) };
//
//     if (!Ctor) { /* not on this build - disable the feature, do not call */ }
//     Ctor(page);
//
// Name an address the way you found it. You opened a Dunia.dll in Ghidra, IDA or Cheat Engine and
// wrote down an address; the only thing that matters is *which build that was*:
//
//   Uplay(rva)   found in the Steam / Ubisoft Connect DLL
//   Retail(rva)  found in the GOG / patched-retail DLL
//
// Either resolves correctly on either build, so a plugin works everywhere regardless of which copy
// of the game its author owns. The offset between the two builds takes 2,608 different values, so
// this lookup is the only correct way to translate - there is no arithmetic that does it.
//
// Header-only, C++11, and depends on nothing but plugin_api.h - copy both into a plugin project.
#pragma once

#include "plugin_api.h"

#ifdef __cplusplus

#include <cstdint>

namespace FCSE {

// Tag types, so the two kinds of uint32_t cannot be confused. A Steam RVA and a GOG RVA are both
// uint32_t and mean entirely different things; passing one where the other was meant would resolve
// to a plausible wrong address rather than fail, which is the worst way to be wrong.

// An address as found in the Steam / Ubisoft Connect DLL.
struct Uplay {
    uint32_t rva;
    explicit constexpr Uplay(uint32_t address) : rva(address) {}
};

// An address as found in the GOG / patched-retail DLL.
struct Retail {
    uint32_t rva;
    explicit constexpr Retail(uint32_t address) : rva(address) {}
};

// The API pointer every Relocation resolves through. Set it once, first thing in FCSE_Load.
// Kept as a plain global rather than passed to each Relocation so that relocations can be declared
// at namespace scope, which is where they read best.
inline const FCSE_PluginAPI*& ApiPointer() {
    static const FCSE_PluginAPI* api = nullptr;
    return api;
}

// Call once from FCSE_Load before touching any Relocation. Returns false if this FCSE is older
// than the address library (API v5), in which case nothing here can work and the plugin should
// either fall back to duniaBase or refuse to load.
inline bool Bind(const FCSE_PluginAPI* api) {
    if (api == nullptr || api->apiVersion < 5 || api->ResolveFrom == nullptr) {
        return false;
    }
    ApiPointer() = api;
    return true;
}

// Which build the game is running, for the rare plugin that must branch on it.
inline FCSE_GameBuild RunningBuild() {
    const FCSE_PluginAPI* api = ApiPointer();
    return api == nullptr ? FCSE_GAME_BUILD_UNKNOWN : api->gameBuild;
}

// A lazily-resolved engine address, typed.
//
// Resolution happens on first use rather than at construction, because a namespace-scope
// Relocation is constructed before FCSE_Load runs and therefore before there is an API to ask.
// The result is cached, so steady-state cost is a null check.
template <class T>
class Relocation {
public:
    explicit constexpr Relocation(Uplay a)
        : m_rva(a.rva), m_source(FCSE_GAME_BUILD_103_UPLAY), m_resolved(0) {}
    explicit constexpr Relocation(Retail a)
        : m_rva(a.rva), m_source(FCSE_GAME_BUILD_103_RETAIL), m_resolved(0) {}

    uintptr_t address() const {
        if (m_resolved == 0) {
            const FCSE_PluginAPI* api = ApiPointer();
            if (api != nullptr) {
                m_resolved = api->ResolveFrom(m_source, m_rva);
            }
        }
        return m_resolved;
    }

    // False when this build has no counterpart for the address. Check it before calling, once,
    // when the feature initialises - a missing address must never become a jump.
    explicit operator bool() const { return address() != 0; }

    T get() const { return reinterpret_cast<T>(address()); }
    operator T() const { return get(); }

    template <class... Args>
    auto operator()(Args&&... args) const -> decltype(get()(static_cast<Args&&>(args)...)) {
        return get()(static_cast<Args&&>(args)...);
    }

private:
    uint32_t m_rva;                // an RVA in m_source's build
    FCSE_GameBuild m_source;       // which build m_rva was read from
    mutable uintptr_t m_resolved;
};

// Convenience for data rather than functions:
//     auto* singleton = FCSE::Data<void*>(FCSE::Uplay(0x00FE3178));
template <class T>
inline T* Data(Uplay a) {
    const FCSE_PluginAPI* api = ApiPointer();
    return api == nullptr ? nullptr
                          : reinterpret_cast<T*>(
                                api->ResolveFrom(FCSE_GAME_BUILD_103_UPLAY, a.rva));
}

template <class T>
inline T* Data(Retail a) {
    const FCSE_PluginAPI* api = ApiPointer();
    return api == nullptr ? nullptr
                          : reinterpret_cast<T*>(
                                api->ResolveFrom(FCSE_GAME_BUILD_103_RETAIL, a.rva));
}

} // namespace FCSE

#endif // __cplusplus
