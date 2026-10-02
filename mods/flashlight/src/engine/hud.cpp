// Magma objects a loaded UI package exports by name, and the calls the HUD drives its groups with.
// See docs/docs/magma-ui/engine-interop.md#driving-an-areas-timeline-from-code.
#include "engine/hud.h"

#include "fcse_api.h"

#include <cstdint>

namespace {
    using FindGenericFn = void*(__thiscall*)(void* server, const uint32_t* id);
    using GenericTargetFn = void*(__thiscall*)(void* generic);
    using SetVisibleFn = void(__thiscall*)(void* element, int32_t visible);
    using SetTimeFn = uint8_t(__thiscall*)(void* area, uint32_t milliseconds, bool executeActions, bool flag);
    using SetPlayingFn = void(__thiscall*)(void* area, bool playing, bool flag);

    // CMagmaFacade::GetGenericObject<Keyframe>, whose second instruction loads the server singleton.
    FCSE::Relocation<uint8_t*> g_getKeyframeSite{FCSE::Uplay(0x00534500)};
    FCSE::Relocation<FindGenericFn> g_findGenericSite{FCSE::Uplay(0x00AA6F60)};
    FCSE::Relocation<GenericTargetFn> g_genericTargetSite{FCSE::Uplay(0x00AA65C0)};
    FCSE::Relocation<SetVisibleFn> g_setVisibleSite{FCSE::Uplay(0x00AB13F0)};
    FCSE::Relocation<SetTimeFn> g_setTimeSite{FCSE::Uplay(0x00A973E0)};
    FCSE::Relocation<SetPlayingFn> g_setPlayingSite{FCSE::Uplay(0x00A973A0)};

    // `mov ecx, [<GenericObjectServer>]`, and where its operand sits.
    constexpr uint16_t kMovEcxMem = 0x0D8B;
    constexpr size_t kServerOperand = 0x06;
    // What a magma::GenericObject resolves to is reached through this offset in it.
    constexpr ptrdiff_t kGenericTarget = 0x0C;
    // magma::Area's milliseconds per frame.
    constexpr ptrdiff_t kAreaFrameTime = 0x18;

    void* const* g_server = nullptr;
    FindGenericFn g_findGeneric = nullptr;
    GenericTargetFn g_genericTarget = nullptr;
    SetVisibleFn g_setVisible = nullptr;
    SetTimeFn g_setTime = nullptr;
    SetPlayingFn g_setPlaying = nullptr;

    // magma::Id: the CRC32 of a name.
    uint32_t MagmaId(const char* name) {
        uint32_t crc = 0xFFFFFFFF;
        for (const char* c = name; *c != '\0'; ++c) {
            crc ^= static_cast<uint8_t>(*c);
            for (int bit = 0; bit < 8; ++bit) {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
        }
        return ~crc;
    }
}

namespace Flashlight::Hud {

bool Install() {
    const uint8_t* getKeyframe = g_getKeyframeSite ? g_getKeyframeSite.get() : nullptr;
    if (getKeyframe == nullptr ||
        *reinterpret_cast<const uint16_t*>(getKeyframe + kServerOperand - 2) != kMovEcxMem ||
        !g_findGenericSite || !g_genericTargetSite || !g_setVisibleSite || !g_setTimeSite ||
        !g_setPlayingSite) {
        FCSE::ApiPointer()->Log("hud: the Magma calls were not found in this build - no icon");
        return false;
    }

    g_server = *reinterpret_cast<void* const* const*>(getKeyframe + kServerOperand);
    g_findGeneric = g_findGenericSite.get();
    g_genericTarget = g_genericTargetSite.get();
    g_setVisible = g_setVisibleSite.get();
    g_setTime = g_setTimeSite.get();
    g_setPlaying = g_setPlayingSite.get();
    return true;
}

void* Find(const char* name) {
    void* server = g_server != nullptr ? *g_server : nullptr;
    if (server == nullptr) {
        return nullptr;
    }
    // By reference: the lookup reads the id through this pointer.
    const uint32_t id = MagmaId(name);
    auto* generic = static_cast<uint8_t*>(g_findGeneric(server, &id));
    return generic != nullptr ? g_genericTarget(generic + kGenericTarget) : nullptr;
}

void SetVisible(void* element, bool visible) { g_setVisible(element, visible ? 1 : 0); }

void PlayFrom(void* area, int frame) {
    const uint16_t frameTime = *reinterpret_cast<const uint16_t*>(static_cast<uint8_t*>(area) + kAreaFrameTime);
    g_setTime(area, static_cast<uint32_t>(frameTime) * frame, true, false);
    g_setPlaying(area, true, false);
}

}
