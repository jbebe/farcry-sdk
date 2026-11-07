// The player seen over the right shoulder on foot. See third_person.h.
#include "third_person.h"

#include "engine/camera.h"
#include "engine/memory.h"
#include "engine/pawn.h"
#include "engine/ray.h"
#include "engine/third_camera.h"
#include "fcse_api.h"

#include <atomic>
#include <cmath>
#include <iterator>
#include <windows.h>

namespace {
    namespace Camera = PlayerBody::Camera;
    namespace Pawn = PlayerBody::Pawn;

    // In metres: the point the camera turns about, right of the feet and above them; how far back
    // from it along the view the camera stands; and how far a shot is aimed.
    constexpr float kRight = 0.55f;
    constexpr float kShoulder = 1.6f;
    constexpr float kBack = 1.6f;
    constexpr float kRange = 300.0f;

    constexpr int kKeys[] = {0, 'V', 'N', VK_F3, VK_F4, VK_F6, VK_F7, VK_F8};
    static_assert(std::size(PlayerBody::ThirdPerson::kKeyLabels) == std::size(kKeys));

    // CGameConfig::UseHelpCrosshair(difficulty), __thiscall, which the HUD asks every frame whether
    // to draw the game's own crosshair.
    using UseCrosshairFn = int32_t(__fastcall*)(void* config, void* unused, int32_t difficulty);
    FCSE::Relocation<UseCrosshairFn> g_useCrosshair{FCSE::Uplay(0x00048860)};
    UseCrosshairFn g_originalUseCrosshair = nullptr;

    // CWeaponFireBulletStrategy::SetupShot3rd, __thiscall(pawn, origin out, direction out, a fourth
    // it passes on): a shot from the gun's muzzle along its barrel.
    using SetupFn = void(__fastcall*)(uint8_t* strategy, void* unused, uint8_t* pawn, float* origin,
                                      float* direction, uint32_t fourth);
    FCSE::Relocation<SetupFn> g_setup3rd{FCSE::Uplay(0x00112080)};
    SetupFn g_originalSetup3rd = nullptr;

    // CWeaponFireBulletStrategy::ApplyRandomAngleSpread(angles, pellet, apply), __thiscall: the
    // random pitch, roll and yaw a shot such as a shotgun's pellet is spread by.
    using SpreadFn = void(__fastcall*)(uint8_t* strategy, void* unused, float* angles, uint32_t pellet,
                                       uint32_t apply);
    FCSE::Relocation<SpreadFn> g_spread{FCSE::Uplay(0x00111760)};
    SpreadFn g_originalSpread = nullptr;
    // The spread the shot being set up on this thread was given, in radians.
    thread_local float t_spread[3] = {};

    std::atomic<int> g_key{'V'};
    // Game thread: the toggle, and the key as last seen.
    bool g_wanted = false;
    bool g_keyDown = false;
    // Game thread: the third-person camera while it is up, the pawn it follows, and the point it
    // turns about and the way it looks, as last placed.
    uint8_t* g_camera = nullptr;
    uint8_t* g_pawn = nullptr;
    float g_pivot[3] = {};
    float g_forward[3] = {};

    // Whether the key went down this frame while the game has the keyboard.
    bool Pressed() {
        const int key = g_key;
        const bool down = key != 0 && (GetAsyncKeyState(key) & 0x8000) != 0;
        const bool pressed = down && !g_keyDown;
        g_keyDown = down;
        if (!pressed) {
            return false;
        }
        DWORD process = 0;
        GetWindowThreadProcessId(GetForegroundWindow(), &process);
        return process == GetCurrentProcessId();
    }

    bool Place(uint8_t* camera, PlayerBody::ThirdCamera::Pose& pose) {
        uint8_t* entity = camera == g_camera && g_pawn != nullptr ? Pawn::Entity(g_pawn) : nullptr;
        if (entity == nullptr) {
            return false;
        }
        const float* look = Pawn::Look(g_pawn);
        const float sp = std::sin(look[0]), cp = std::cos(look[0]);
        const float sy = std::sin(look[2]), cy = std::cos(look[2]);
        g_forward[0] = -sy * cp;
        g_forward[1] = cy * cp;
        g_forward[2] = sp;
        const float right[3] = {cy, sy, 0.0f};
        const float* feet = Pawn::Place(entity);
        for (int k = 0; k < 3; ++k) {
            g_pivot[k] = feet[k] + right[k] * kRight;
        }
        g_pivot[2] += kShoulder;
        for (int k = 0; k < 3; ++k) {
            pose.position[k] = g_pivot[k] - g_forward[k] * kBack;
        }
        pose.angles[0] = look[0];
        pose.angles[1] = 0.0f;
        pose.angles[2] = look[2];
        return true;
    }

    int32_t __fastcall UseCrosshairDetour(void* config, void* unused, int32_t difficulty) {
        return g_camera != nullptr ? 1 : g_originalUseCrosshair(config, unused, difficulty);
    }

    void __fastcall SpreadDetour(uint8_t* strategy, void* unused, float* angles, uint32_t pellet,
                                 uint32_t apply) {
        const float before[3] = {angles[0], angles[1], angles[2]};
        g_originalSpread(strategy, unused, angles, pellet, apply);
        for (int k = 0; k < 3; ++k) {
            t_spread[k] = angles[k] - before[k];
        }
    }

    // Aims the player's shot from the muzzle at what lies under the screen's centre, looked for from
    // the camera's pivot, spread as the engine spread it.
    void __fastcall Setup3rdDetour(uint8_t* strategy, void* unused, uint8_t* pawn, float* origin,
                                   float* direction, uint32_t fourth) {
        t_spread[0] = t_spread[1] = t_spread[2] = 0.0f;
        g_originalSetup3rd(strategy, unused, pawn, origin, direction, fourth);
        if (g_camera == nullptr || pawn != g_pawn) {
            return;
        }
        const float reach = PlayerBody::Ray::Cast(g_pivot, g_forward, kRange);
        float aim[3];
        for (int k = 0; k < 3; ++k) {
            aim[k] = g_pivot[k] + g_forward[k] * reach - origin[k];
        }
        const float length = std::hypot(aim[0], aim[1], aim[2]);
        if (length <= 0.0f) {
            return;
        }
        // Pitch, roll and yaw as the engine turns them into a direction.
        const float pitch = std::asin(aim[2] / length) + t_spread[0];
        const float roll = t_spread[1];
        const float yaw = std::atan2(-aim[0], aim[1]) + t_spread[2];
        const float sp = std::sin(pitch), cp = std::cos(pitch);
        const float sr = std::sin(roll), cr = std::cos(roll);
        const float sy = std::sin(yaw), cy = std::cos(yaw);
        direction[0] = cy * sr * sp - sy * cp;
        direction[1] = sy * sr * sp + cy * cp;
        direction[2] = cr * sp;
    }
}

bool PlayerBody::ThirdPerson::Install() {
    Ray::Install();
    if (!Hook(g_useCrosshair.address(), &UseCrosshairDetour, &g_originalUseCrosshair) ||
        !Hook(g_spread.address(), &SpreadDetour, &g_originalSpread) ||
        !Hook(g_setup3rd.address(), &Setup3rdDetour, &g_originalSetup3rd)) {
        FCSE::ApiPointer()->Log("third person: the crosshair or the third-person shot was not found");
    }
    return ThirdCamera::Install(&Place);
}

void PlayerBody::ThirdPerson::SetKey(uint32_t choice) {
    g_key = choice < std::size(kKeys) ? kKeys[choice] : 0;
}

void PlayerBody::ThirdPerson::Update(uint8_t* pawn) {
    if (pawn != g_pawn) {
        g_camera = nullptr;
        g_pawn = pawn;
    }
    const bool pressed = Pressed();
    if (!pressed && !g_wanted && g_camera == nullptr) {
        return;
    }
    const bool onFoot = Pawn::OnFoot(pawn);
    if (pressed && onFoot) {
        g_wanted = !g_wanted;
    }
    const bool wanted = g_wanted && onFoot && g_key != 0;
    if (wanted != (g_camera != nullptr)) {
        if (uint8_t* camera = Camera::Activate(wanted ? Camera::kThird : Camera::kFirst)) {
            Pawn::SetFirstPerson(pawn, !wanted);
            g_camera = wanted ? camera : nullptr;
        }
    }
    if (g_camera != nullptr) {
        Pawn::DropSights(pawn);
    }
}
