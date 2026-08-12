// Field of view.
//
// Far Cry 2 renders first person at 75 degrees and offers no way to change it. The value arrives
// through CCameraComponent's `fFOV` property setter, registered by CCameraComponent::
// RegisterProperties (Steam Dunia.dll 0x105050B7) alongside fNearDistance and fFarDistance:
//
//     10504CC0  F3 0F 10 44 24 04         movss xmm0, dword ptr [esp+4]      ; degrees
//     10504CC6  F3 0F 59 05 D8 EE E0 10   mulss xmm0, dword ptr [0x10E0EED8] ; 0.017453292, pi/180
//     10504CCE  F3 0F 11 41 70            movss dword ptr [ecx+70h], xmm0    ; this->m_fov, radians
//     10504CD3  C2 04 00                  ret 4
//
// So the property is authored in degrees and the component stores radians. Hooking the setter and
// substituting the argument is all this takes - the conversion, and every other camera field, stay
// exactly as the engine wrote them. Far Cry 2 Multi Fixer patches the first instruction to load its
// own constant instead, which is the same idea done destructively.
//
// Two consequences worth knowing, both inherent to the seam rather than to this implementation:
//
//   - It applies to every camera that sets fFOV, not just the player's. That is what makes it work
//     at all - there is no separate "player camera" property - but it means a scripted camera
//     authored at a different angle is overridden too.
//   - The property is set when a camera entity is created, not per frame, so a change made in the
//     menu reaches the world on the next load rather than instantly.
//
// Because of the first point, the stock value means "leave the engine alone" rather than "force 75
// everywhere": at the default the detour passes the engine's own argument straight through, so
// installing UFCP changes nothing until the player moves the slider.
#include "fcse_api.h"

#include <cstdint>
#include <cstdio>

namespace {
    // The value the game itself uses for first person. The slider's default, and the point at which
    // this feature disables itself.
    constexpr int32_t kStockFov = 75;

    // __thiscall with one float argument, declared __fastcall because MSVC will not let a free
    // function be __thiscall. For a method taking one stack argument the two are the same ABI:
    // ECX carries `this`, EDX is unused, the float is on the stack, and the callee cleans 4 bytes.
    using SetFovFn = void(__fastcall*)(void* self, void* unused, float degrees);

    // Matched on the whole setter: load, convert, store, return. Only the mulss operand differs
    // between builds - it points at each build's own copy of pi/180 - so wildcarding those four
    // bytes gives one pattern that resolves on both, and it occurs exactly once in each.
    FCSE::Relocation<SetFovFn> g_setFov{FCSE::Pattern(
        "F3 0F 10 44 24 04 F3 0F 59 05 ?? ?? ?? ?? F3 0F 11 41 70 C2 04 00")};

    SetFovFn g_originalSetFov = nullptr;

    // Degrees, or 0 for "pass the engine's own value through untouched".
    float g_fovOverride = 0.0f;

    void __fastcall SetFovDetour(void* self, void* unused, float degrees) {
        g_originalSetFov(self, unused, g_fovOverride > 0.0f ? g_fovOverride : degrees);
    }
}

// Installs the setter hook. Call once from FCSE_Load, before the settings that drive it are
// registered. On failure the feature is inert and logs why - the slider still appears, and moving
// it does nothing, which is better than a row that vanishes for reasons the player cannot see.
void InstallFovHook() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!g_setFov) {
        api->Log("fov: the camera's fFOV setter was not found in this build - field of view cannot "
                 "be changed");
        return;
    }

    if (api->Hook(reinterpret_cast<void*>(g_setFov.address()),
                  reinterpret_cast<void*>(&SetFovDetour),
                  reinterpret_cast<void**>(&g_originalSetFov))) {
        char line[128];
        std::snprintf(line, sizeof(line), "fov: hooked the fFOV setter at 0x%08zX",
                      static_cast<size_t>(g_setFov.address()));
        api->Log(line);
    }
    // A rejected hook is already logged by FCSE, naming the plugin that owns the address.
    // g_originalSetFov stays null, and the detour is unreachable because it was never installed.
}

void __cdecl OnFovChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    g_fovOverride = value->asSlider == kStockFov ? 0.0f : static_cast<float>(value->asSlider);

    char line[128];
    if (g_fovOverride > 0.0f) {
        std::snprintf(line, sizeof(line), "fov: %d degrees (takes effect on the next load)",
                      value->asSlider);
    } else {
        std::snprintf(line, sizeof(line), "fov: %d degrees, the game's own value - not overridden",
                      kStockFov);
    }
    api->Log(line);
}
