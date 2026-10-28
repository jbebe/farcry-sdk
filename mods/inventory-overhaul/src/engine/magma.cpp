// Magma's GenericObjectServer, as mods/flashlight reaches it, and the string assignment
// CMagmaFacade::SetLabel makes. See docs/docs/magma-ui/engine-interop.md.
#include "engine/magma.h"

#include "engine/memory.h"
#include "fcse_api.h"

#include <cwchar>

namespace InventoryOverhaul::Magma {

namespace {
    using FindGenericFn = void*(__thiscall*)(void* server, const uint32_t* id);
    using GenericTargetFn = void*(__thiscall*)(void* generic);
    using SetVisibleFn = void(__thiscall*)(void* element, int32_t visible);
    using SetStringFn = void(__thiscall*)(void* text, const EngineString<wchar_t>* string);
    using SetTimeFn = uint8_t(__thiscall*)(void* area, uint32_t milliseconds, bool executeActions, bool flag);

    // CMagmaFacade::GetGenericObject<Keyframe>, whose second instruction loads the server singleton.
    FCSE::Relocation<uint8_t*> g_getKeyframe{FCSE::Uplay(0x00534500)};
    FCSE::Relocation<FindGenericFn> g_findGeneric{FCSE::Uplay(0x00AA6F60)};
    FCSE::Relocation<GenericTargetFn> g_genericTarget{FCSE::Uplay(0x00AA65C0)};
    FCSE::Relocation<SetVisibleFn> g_setVisible{FCSE::Uplay(0x00AB13F0)};
    // A Text's string set and measured again, the part of SetLabel after it has made the wstring.
    FCSE::Relocation<SetStringFn> g_setString{FCSE::Uplay(0x00535560)};
    // magma::Area's SetTime, as the HUD shows its pills.
    FCSE::Relocation<SetTimeFn> g_setTime{FCSE::Uplay(0x00A973E0)};

    // `mov ecx, [<GenericObjectServer>]`, and where its operand sits.
    constexpr uint16_t kMovEcxMem = 0x0D8B;
    constexpr size_t kServerOperand = 0x06;
    // What a magma::GenericObject resolves to is reached through this offset in it.
    constexpr ptrdiff_t kGenericTarget = 0x0C;
    // An element's widget, which for a Text element is the magma::Text, and an Image's Material*.
    constexpr ptrdiff_t kElementWidget = 0x14;
    constexpr ptrdiff_t kImageMaterial = 0x20;
    // magma::Area's milliseconds per frame.
    constexpr ptrdiff_t kAreaFrameTime = 0x18;

    void* const* g_server = nullptr;
}

bool Install() {
    const uint8_t* getKeyframe = g_getKeyframe.get();
    if (getKeyframe == nullptr || Read<uint16_t>(getKeyframe, kServerOperand - 2) != kMovEcxMem ||
        !g_findGeneric || !g_genericTarget || !g_setVisible || !g_setString || !g_setTime) {
        return false;
    }
    g_server = Read<void* const*>(getKeyframe, kServerOperand);
    return true;
}

void* Find(const char* name) {
    const uint32_t id = FCSE::Crc32(name);
    void* server = g_server != nullptr ? *g_server : nullptr;
    auto* generic = server != nullptr ? static_cast<uint8_t*>(g_findGeneric(server, &id)) : nullptr;
    return generic != nullptr ? g_genericTarget(generic + kGenericTarget) : nullptr;
}

void SetVisible(void* element, bool visible) { g_setVisible(element, visible); }

void SetText(void* element, const wchar_t* text) {
    const auto string = EngineString<wchar_t>::Of(text, std::wcslen(text));
    g_setString(Read<void*>(element, kElementWidget), &string);
}

void SetPicture(void* image, const void* from) {
    Write(Read<void*>(image, kElementWidget), kImageMaterial,
          Read<void*>(Read<const void*>(from, kElementWidget), kImageMaterial));
}

void SetTime(void* area, uint32_t milliseconds) { g_setTime(area, milliseconds, false, false); }

uint16_t TimePerFrame(const void* area) { return Read<uint16_t>(area, kAreaFrameTime); }

}
