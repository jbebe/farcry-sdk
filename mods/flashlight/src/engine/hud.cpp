// Magma objects a loaded UI package exports by name, and the calls the HUD drives its groups with.
// See docs/docs/magma-ui/engine-interop.md#driving-an-areas-timeline-from-code.
#include "engine/hud.h"

#include "fcse_api.h"

namespace {
    using FindGenericFn = void*(__thiscall*)(void* server, const uint32_t* id);
    using GenericTargetFn = void*(__thiscall*)(void* generic);
    using SetVisibleFn = void(__thiscall*)(void* element, int32_t visible);
    using SetTimeFn = uint8_t(__thiscall*)(void* area, uint32_t milliseconds, bool executeActions, bool flag);
    using SetPlayingFn = void(__thiscall*)(void* area, bool playing, bool flag);

    // CMagmaFacade::GetGenericObject<Keyframe>, whose second instruction loads the server singleton.
    FCSE::Relocation<uint8_t*> g_getKeyframe{FCSE::Uplay(0x00534500)};
    FCSE::Relocation<FindGenericFn> g_findGeneric{FCSE::Uplay(0x00AA6F60)};
    FCSE::Relocation<GenericTargetFn> g_genericTarget{FCSE::Uplay(0x00AA65C0)};
    FCSE::Relocation<SetVisibleFn> g_setVisible{FCSE::Uplay(0x00AB13F0)};
    FCSE::Relocation<SetTimeFn> g_setTime{FCSE::Uplay(0x00A973E0)};
    FCSE::Relocation<SetPlayingFn> g_setPlaying{FCSE::Uplay(0x00A973A0)};

    // `mov ecx, [<GenericObjectServer>]`, and where its operand sits.
    constexpr uint16_t kMovEcxMem = 0x0D8B;
    constexpr size_t kServerOperand = 0x06;
    // What a magma::GenericObject resolves to is reached through this offset in it.
    constexpr ptrdiff_t kGenericTarget = 0x0C;
    // magma::Area's milliseconds per frame.
    constexpr ptrdiff_t kAreaFrameTime = 0x18;

    void* const* g_server = nullptr;
}

namespace Flashlight::Hud {

void Install() {
    const uint8_t* getKeyframe = g_getKeyframe.get();
    if (getKeyframe == nullptr ||
        *reinterpret_cast<const uint16_t*>(getKeyframe + kServerOperand - 2) != kMovEcxMem ||
        !g_findGeneric || !g_genericTarget || !g_setVisible || !g_setTime || !g_setPlaying) {
        FCSE::ApiPointer()->Log("hud: the Magma calls were not found in this build - no icon");
        return;
    }
    g_server = *reinterpret_cast<void* const* const*>(getKeyframe + kServerOperand);
}

void* Find(uint32_t id) {
    void* server = g_server != nullptr ? *g_server : nullptr;
    auto* generic = server != nullptr ? static_cast<uint8_t*>(g_findGeneric(server, &id)) : nullptr;
    return generic != nullptr ? g_genericTarget(generic + kGenericTarget) : nullptr;
}

void SetVisible(void* element, bool visible) { g_setVisible(element, visible); }

void PlayFrom(void* area, int frame) {
    const uint16_t frameTime = *reinterpret_cast<const uint16_t*>(static_cast<uint8_t*>(area) + kAreaFrameTime);
    g_setTime(area, static_cast<uint32_t>(frameTime) * frame, true, false);
    g_setPlaying(area, true, false);
}

}
