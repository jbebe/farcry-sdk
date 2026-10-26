// Field of view on foot, weapon and arms with it. Sights, vehicles and computers keep the game's own.
//
// Ported from FC2JackalFix (MIT, (c) 2026 Joshhhuaaa, TGP482) - source/display/fov.ixx, which in
// turn credits FoxAhead's Far Cry 2 Multi Fixer for the original fFOV substitution.
//
// Far Cry 2 renders first person at 75 degrees and offers no way to change it. The value arrives
// through CCameraComponent's fFOV property setter, which takes degrees on the stack and stores
// radians at this+0x70.
//
// The weapon and arms need nothing of their own: their nearer pass builds its projection from the
// camera's field of view.
#include "fcse_api.h"

#include <algorithm>
#include <cmath>
#include <cstdint>

namespace {
    // Degrees, as the setting delivers it.
    float g_fieldOfView = 75.0f;

    constexpr float kRadiansPerDegree = 3.14159265f / 180.0f;

    // tan(FOV / 2) for the game's own 75 degrees.
    constexpr float kStockHalfTangent = 0.76732699f;

    // The first-person camera's FOV, on the primary base.
    constexpr uintptr_t kPawnCameraFieldOfView = 0x70;

    // The camera's widescreen weight, on the primary base. Only a computer fades it: to zero as the
    // player sits down at one, back to one over two seconds as he gets up.
    constexpr uintptr_t kPawnCameraWidescreenWeight = 0x5C;

    // The setting, eased back to the game's own in tangent space as far as a computer has faded that
    // weight, so the field of view and the game's zoom move as one.
    float PawnCameraFieldOfView(uintptr_t camera) {
        const float setting = g_fieldOfView * kRadiansPerDegree;
        const float weight = std::clamp(
            *reinterpret_cast<float*>(camera + kPawnCameraWidescreenWeight), 0.0f, 1.0f);
        if (weight >= 1.0f) {
            return setting;
        }

        return 2.0f * std::atan(kStockHalfTangent +
                                (std::tan(setting * 0.5f) - kStockHalfTangent) * weight);
    }

    void FieldOfViewSetterHandler(FCSE_MidHookContext* ctx) {
        *reinterpret_cast<float*>(ctx->esp + 4) = g_fieldOfView;
    }

    // With the Widescreen option off the field of view goes unwidened, so the branch is forced. Its
    // weight is left to the game, whose only fade of it is a computer's zoom.
    void WidescreenHandler(FCSE_MidHookContext* ctx) {
        constexpr uintptr_t kZeroFlag = 0x40;

        ctx->eflags &= ~kZeroFlag;
    }

    // At the head of the frame's FOV blend, which rebuilds the camera's field every frame. ESI is
    // the camera.
    void CameraBlendHandler(FCSE_MidHookContext* ctx) {
        *reinterpret_cast<float*>(ctx->esi + kPawnCameraFieldOfView) =
            PawnCameraFieldOfView(ctx->esi);
    }

    FCSE::Relocation<uint8_t*> g_setter{
        FCSE::Pattern("F3 0F 10 44 24 04 F3 0F 59 05 ?? ?? ?? ?? F3 0F 11 41 70 C2 04 00")};

    // Into the match, the branch the widescreen hook stands on.
    constexpr ptrdiff_t kWidescreenBranch = 10;
    FCSE::Relocation<uint8_t*> g_widescreen{FCSE::Pattern(
        "8B 0D ?? ?? ?? ?? 83 79 40 00 0F 84 C4 00 00 00 E8 ?? ?? ?? ?? F3 0F 10 40 10")};

    FCSE::Relocation<uint8_t*> g_cameraBlend{
        FCSE::Pattern("F3 0F 10 4E 70 F3 0F 10 47 14 0F 57 D2 F3 0F 5C C1 F3 0F 59 47 10")};
}

void InstallFovHook() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!g_setter) {
        api->Log("fov: the camera's fFOV setter was not found in this build - field of view cannot "
                 "be changed");
        return;
    }

    api->MidHook(reinterpret_cast<void*>(g_setter.address()), &FieldOfViewSetterHandler);

    if (g_widescreen) {
        api->MidHook(reinterpret_cast<void*>(g_widescreen.address() + kWidescreenBranch),
                     &WidescreenHandler);
    }

    if (g_cameraBlend) {
        api->MidHook(reinterpret_cast<void*>(g_cameraBlend.address()), &CameraBlendHandler);
    }

    FCSE::Logf("fov: hooked the camera at 0x%08zX", static_cast<size_t>(g_setter.address()));
}

void __cdecl OnFovChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
    g_fieldOfView = static_cast<float>(value->asSlider);
}
