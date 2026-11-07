// The player seen from outside. See outside.h.
#include "outside.h"

#include "boom.h"
#include "chase.h"
#include "ease.h"
#include "engine/camera.h"
#include "engine/driver.h"
#include "engine/input.h"
#include "engine/memory.h"
#include "engine/pawn.h"
#include "engine/ray.h"
#include "engine/terrain.h"
#include "engine/third_camera.h"
#include "fcse_api.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <windows.h>

namespace {
    namespace Camera = ThirdPerson::Camera;
    namespace Chase = ThirdPerson::Chase;
    namespace Input = ThirdPerson::Input;
    namespace Pawn = ThirdPerson::Pawn;

    // The camera up: the game's first-person one, over the shoulder on foot, or behind the car.
    enum class View { First, Shoulder, Chase };

    // The Third-person view control's signal.
    constexpr uint32_t kToggle = FCSE::Crc32("toggle_thirdperson");

    // In metres: how far right of a centre above the feet the camera stands, how high that centre is,
    // and how far back the camera stands along the view; and how far a shot is aimed.
    constexpr float kRight = 0.55f;
    constexpr float kShoulder = 1.6f;
    constexpr float kBack = 1.6f;
    constexpr float kRange = 300.0f;

    // Aiming: the camera's place right of the centre and back from it, in metres; the seconds it
    // takes to get there; and the crosshair's size against the game's.
    constexpr float kAimRight = 0.65f;
    constexpr float kAimBack = 0.8f;
    constexpr float kAimSeconds = 0.2f;
    constexpr float kAimCrosshair = 0.0f;

    // The aim key's press and release, and the fire key's.
    constexpr uint32_t kAim = FCSE::Crc32("startironsight");
    constexpr uint32_t kAimReleased = FCSE::Crc32("stopironsight");
    constexpr uint32_t kFire = FCSE::Crc32("startshooting");
    constexpr uint32_t kFireReleased = FCSE::Crc32("stopshooting");

    // How long the gun stays up after a shot, in milliseconds.
    constexpr DWORD kGunUpAfterShot = 1000;

    // CGameConfig::UseHelpCrosshair(difficulty), __thiscall, which the HUD asks every frame whether
    // to draw the game's own crosshair.
    using UseCrosshairFn = int32_t(__fastcall*)(void* config, void* unused, int32_t difficulty);
    FCSE::Relocation<UseCrosshairFn> g_useCrosshair{FCSE::Uplay(0x00048860)};
    UseCrosshairFn g_originalUseCrosshair = nullptr;

    // CWeapon::GetCurrentSpread, __fastcall on the weapon, which only the HUD's crosshair reads.
    using CurrentSpreadFn = float(__fastcall*)(uint8_t* weapon);
    FCSE::Relocation<CurrentSpreadFn> g_currentSpread{FCSE::Uplay(0x0012EB40)};
    CurrentSpreadFn g_originalCurrentSpread = nullptr;

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

    // Set by a press of the control, spent on the pawn tick.
    std::atomic<bool> g_pressed{false};
    // Game thread: the view chosen for the session, and whether it waits after the game took the
    // camera.
    bool g_wanted = false;
    bool g_held = false;
    // Game thread: the view up, its camera out of first person, and the pawn it follows.
    View g_view = View::First;
    uint8_t* g_camera = nullptr;
    uint8_t* g_pawn = nullptr;
    // Game thread: the view where the player is: on foot, at the wheel, or neither.
    View g_where = View::First;
    // Game thread: where the camera over the shoulder stands, and the way it looks; its boom.
    float g_eye[3] = {};
    float g_forward[3] = {};
    ThirdPerson::Boom g_boom;
    // Game thread: whether the aim key is held, and how far toward aiming the camera is, 0 to 1.
    bool g_aimHeld = false;
    float g_aim = 0.0f;
    // Game thread: whether the fire key is held. Any thread: the player's last shot, GetTickCount.
    bool g_fireHeld = false;
    std::atomic<DWORD> g_lastShot{0};

    // How far the camera has gone toward aiming, eased in and out.
    float Aimed() {
        return ThirdPerson::Smooth(g_aim);
    }

    ThirdPerson::ThirdCamera::Pose OverShoulder(uint8_t* entity, float seconds) {
        const float step = seconds / kAimSeconds;
        g_aim = std::clamp(g_aim + (g_aimHeld ? step : -step), 0.0f, 1.0f);
        const float aimed = Aimed();
        const float side = std::lerp(kRight, kAimRight, aimed);
        const float back = std::lerp(kBack, kAimBack, aimed);
        const float* look = Pawn::Look(g_pawn);
        const float sp = std::sin(look[0]), cp = std::cos(look[0]);
        const float sy = std::sin(look[2]), cy = std::cos(look[2]);
        g_forward[0] = -sy * cp;
        g_forward[1] = cy * cp;
        g_forward[2] = sp;
        const float right[3] = {cy, sy, 0.0f};
        const float* feet = Pawn::Place(entity);
        // The player's centre at shoulder height, and the way out from it to where the camera
        // wants to stand.
        const float centre[3] = {feet[0], feet[1], feet[2] + kShoulder};
        float out[3];
        for (int k = 0; k < 3; ++k) {
            out[k] = right[k] * side - g_forward[k] * back;
        }
        const float length = std::hypot(out[0], out[1], out[2]);
        for (int k = 0; k < 3; ++k) {
            out[k] /= length;
        }
        const float reach = g_boom.Follow(centre, out, length, seconds);
        for (int k = 0; k < 3; ++k) {
            g_eye[k] = centre[k] + out[k] * reach;
        }
        return {{g_eye[0], g_eye[1], g_eye[2]}, {look[0], 0.0f, look[2]}};
    }

    bool Place(uint8_t* camera, float seconds, ThirdPerson::ThirdCamera::Pose& pose) {
        if (camera != g_camera) {
            return false;
        }
        uint8_t* followed = g_view == View::Chase ? Pawn::Vehicle(g_pawn) : g_pawn;
        uint8_t* entity = followed != nullptr ? Pawn::Entity(followed) : nullptr;
        if (entity == nullptr) {
            return false;
        }
        pose = g_view == View::Chase ? Chase::Place(Pawn::Matrix(entity), seconds)
                                     : OverShoulder(entity, seconds);
        return true;
    }

    int32_t __fastcall UseCrosshairDetour(void* config, void* unused, int32_t difficulty) {
        return g_view == View::Shoulder ? 1 : g_originalUseCrosshair(config, unused, difficulty);
    }

    float __fastcall CurrentSpreadDetour(uint8_t* weapon) {
        const float spread = g_originalCurrentSpread(weapon);
        return g_view == View::Shoulder ? spread * std::lerp(1.0f, kAimCrosshair, Aimed()) : spread;
    }

    void OnSignal(uint32_t signal) {
        if (signal == kToggle) {
            g_pressed = true;
        } else if (signal == kAim || signal == kAimReleased) {
            g_aimHeld = signal == kAim;
        } else if (signal == kFire || signal == kFireReleased) {
            g_fireHeld = signal == kFire;
        }
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
    // the camera, spread as the engine spread it.
    void __fastcall Setup3rdDetour(uint8_t* strategy, void* unused, uint8_t* pawn, float* origin,
                                   float* direction, uint32_t fourth) {
        t_spread[0] = t_spread[1] = t_spread[2] = 0.0f;
        g_originalSetup3rd(strategy, unused, pawn, origin, direction, fourth);
        if (g_view != View::Shoulder || pawn != g_pawn) {
            return;
        }
        g_lastShot = GetTickCount();
        const float reach = ThirdPerson::Ray::Cast(g_eye, g_forward, kRange);
        float aim[3];
        for (int k = 0; k < 3; ++k) {
            aim[k] = g_eye[k] + g_forward[k] * reach - origin[k];
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

    // The gadgets carried at the side: the map, the phone and Inventory Overhaul's notebook.
    constexpr uint32_t kSideGadgets[] = {FCSE::Crc32("map"), FCSE::Crc32("phone"),
                                         FCSE::Crc32("unused1")};

    // Hides the side gadgets but the equipped one.
    void HideSideGadgets(uint8_t* pawn) {
        uint8_t* equipped = Pawn::EquippedGadget(pawn);
        for (const uint32_t category : kSideGadgets) {
            uint8_t* gadget = Pawn::Gadget(pawn, category);
            if (gadget != nullptr && gadget != equipped) {
                Pawn::HideGadget(gadget);
            }
        }
    }

    // Puts up the camera `to` wants; between the two third-person views only the placement changes.
    // Waits while a cutscene owns the camera.
    void Switch(uint8_t* pawn, View to) {
        const bool outside = to != View::First;
        if (outside != (g_camera != nullptr)) {
            uint8_t* camera = Camera::Activate(outside ? Camera::kThird : Camera::kFirst);
            if (camera == nullptr) {
                return;
            }
            Pawn::SetFirstPerson(pawn, !outside);
            if (outside) {
                HideSideGadgets(pawn);
            }
            g_camera = outside ? camera : nullptr;
        }
        if (to == View::Chase) {
            Chase::Reset();
        }
        Pawn::SetGunUp(pawn, true);
        g_aim = 0.0f;
        g_boom.Reset();
        g_view = to;
    }
}

bool ThirdPerson::Outside::Install() {
    Ray::Install();
    if (!Hook(g_useCrosshair.address(), &UseCrosshairDetour, &g_originalUseCrosshair) ||
        !Hook(g_spread.address(), &SpreadDetour, &g_originalSpread) ||
        !Hook(g_setup3rd.address(), &Setup3rdDetour, &g_originalSetup3rd)) {
        FCSE::ApiPointer()->Log("third person: the crosshair or the third-person shot was not found");
    }
    if (!Hook(g_currentSpread.address(), &CurrentSpreadDetour, &g_originalCurrentSpread)) {
        FCSE::ApiPointer()->Log("third person: the crosshair does not tighten while aiming");
    }
    // Without the terrain, the chase camera never comes up.
    if (Terrain::Install()) {
        Driver::Install();
    }
    return Input::Install(&OnSignal) && ThirdCamera::Install(&Place);
}

void ThirdPerson::Outside::Update(uint8_t* pawn, float* look) {
    if (pawn != g_pawn) {
        g_view = View::First;
        g_camera = nullptr;
        g_pawn = pawn;
        g_aimHeld = false;
        g_fireHeld = false;
    }
    if (!g_pressed && !g_wanted && !g_held && g_view == View::First) {
        return;
    }
    // A turret, a passenger seat, and getting in or out are seen in first person.
    const View where = Pawn::OnFoot(pawn) ? View::Shoulder : Driver::Driving() ? View::Chase : View::First;
    if (where != g_where) {
        g_where = where;
        g_held = false;
    }
    // Held, a press brings the chosen view back.
    if (g_pressed.exchange(false)) {
        if (!g_held) {
            g_wanted = !g_wanted;
        }
        g_held = false;
    }
    // A level load or a cutscene took the camera: let it go where it stands.
    if (g_camera != nullptr && Camera::Active() != g_camera) {
        FCSE::ApiPointer()->Log("third person: the camera was taken over, leaving it");
        Pawn::SetFirstPerson(pawn, true);
        Pawn::SetGunUp(pawn, true);
        g_view = View::First;
        g_camera = nullptr;
        g_held = true;
    }
    const View wanted = g_wanted && !g_held ? where : View::First;
    if (wanted != g_view) {
        Switch(pawn, wanted);
    }
    if (g_view == View::Chase) {
        Chase::Look(look[0], look[1]);
        look[0] = 0.0f;
        look[1] = 0.0f;
    } else if (g_view == View::Shoulder) {
        Pawn::DropSights(pawn);
        Pawn::SetGunUp(pawn, g_aimHeld || g_fireHeld || GetTickCount() - g_lastShot < kGunUpAfterShot);
    }
}
