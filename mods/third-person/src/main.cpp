// Third Person - the player's own legs, walking, under the first-person view, and the player seen
// from outside. See README.md.
#include "body.h"
#include "engine/camera.h"
#include "engine/graphic.h"
#include "engine/pawn.h"
#include "engine/skeleton.h"
#include "engine/skin.h"
#include "fcse_api.h"
#include "legs.h"
#include "outside.h"
#include "shadow.h"

#include <algorithm>
#include <chrono>

namespace {
    // Seconds since the last frame, at most a tenth.
    float Elapsed() {
        using Clock = std::chrono::steady_clock;
        static Clock::time_point last = Clock::now();
        const Clock::time_point now = Clock::now();
        const float seconds = std::chrono::duration<float>(now - last).count();
        last = now;
        return (std::min)(seconds, 0.1f);
    }

    void OnTick(uint8_t* pawn, float* look) {
        const float seconds = Elapsed();
        ThirdPerson::Outside::Update(pawn, look);
        ThirdPerson::Legs::Update(pawn, seconds, ThirdPerson::Body::Follow(pawn));
        ThirdPerson::Shadow::Update(pawn);
    }

    void __cdecl OnLegsChanged(const FCSE_SettingValue* value, void*) {
        ThirdPerson::Body::SetEnabled(value->asCheckbox);
    }

    void __cdecl OnShadowChanged(const FCSE_SettingValue* value, void*) {
        ThirdPerson::Shadow::SetEnabled(value->asCheckbox);
    }
}

extern "C" __declspec(dllexport) bool FCSE_Load(const FCSE_PluginAPI* api) {
    if (api->apiVersion != FCSE_API_VERSION || !FCSE::Bind(api) ||
        !ThirdPerson::Graphic::Install(&ThirdPerson::Body::OnViewMode)) {
        return false;
    }
    // A hook is live from here, so the plugin has to stay loaded whatever happens next.
    ThirdPerson::Camera::Install();
    ThirdPerson::Skin::Install(&ThirdPerson::Body::OnSkinned);
    ThirdPerson::Skeleton::Install(&ThirdPerson::Legs::OnPosed);
    ThirdPerson::Pawn::Install(&OnTick);
    ThirdPerson::Shadow::Install();
    ThirdPerson::Outside::Install();

    const FCSE_Setting settings[] = {
        {"Legs in first person", FCSE_CHECKBOX(true), &OnLegsChanged, nullptr},
        {"Shadow in first person", FCSE_CHECKBOX(true), &OnShadowChanged, nullptr},
    };
    api->RegisterSettings("Third Person", settings, std::size(settings));
    api->Log("Third Person loaded");
    return true;
}
