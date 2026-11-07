// Player Body - the player's own legs, walking, under the first-person view. See README.md.
#include "body.h"
#include "engine/camera.h"
#include "engine/graphic.h"
#include "engine/pawn.h"
#include "engine/skeleton.h"
#include "engine/skin.h"
#include "fcse_api.h"
#include "legs.h"
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

    void OnTick(uint8_t* pawn) {
        const float seconds = Elapsed();
        PlayerBody::Legs::Update(pawn, seconds, PlayerBody::Body::Follow(pawn));
        PlayerBody::Shadow::Update(pawn);
    }

    void __cdecl OnLegsChanged(const FCSE_SettingValue* value, void*) {
        PlayerBody::Body::SetEnabled(value->asCheckbox);
    }

    void __cdecl OnShadowChanged(const FCSE_SettingValue* value, void*) {
        PlayerBody::Shadow::SetEnabled(value->asCheckbox);
    }
}

extern "C" __declspec(dllexport) bool FCSE_Load(const FCSE_PluginAPI* api) {
    if (api->apiVersion != FCSE_API_VERSION || !FCSE::Bind(api) ||
        !PlayerBody::Graphic::Install(&PlayerBody::Body::OnViewMode)) {
        return false;
    }
    // A hook is live from here, so the plugin has to stay loaded whatever happens next.
    PlayerBody::Camera::Install();
    PlayerBody::Skin::Install(&PlayerBody::Body::OnSkinned);
    PlayerBody::Skeleton::Install(&PlayerBody::Legs::OnPosed);
    PlayerBody::Pawn::Install(&OnTick);
    PlayerBody::Shadow::Install();

    const FCSE_Setting settings[] = {
        {"Legs in first person", FCSE_CHECKBOX(true), &OnLegsChanged, nullptr},
        {"Shadow in first person", FCSE_CHECKBOX(true), &OnShadowChanged, nullptr},
    };
    api->RegisterSettings("Player Body", settings, std::size(settings));
    api->Log("Player Body loaded");
    return true;
}
