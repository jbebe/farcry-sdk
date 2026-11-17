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
#include <iterator>
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

    // CGOStateMachineDriver::Signal(id), __thiscall, RET 4: a signal to a pawn's state machine.
    using SignalFn = void(__fastcall*)(uint8_t* driver, void* unused, uint32_t signal);
    FCSE::Relocation<SignalFn> g_signal{FCSE::Uplay(0x0029DA10)};
    SignalFn g_originalSignal = nullptr;

    // The signals that start the player's interactions in interactions.gosm. Their states wait for
    // animations only the first-person view plays to the end.
    constexpr uint32_t kInteractions[] = {
        FCSE::Crc32("pickup_weapon"), FCSE::Crc32("pickup_and_reload"),
        FCSE::Crc32("pickup_weapon_holster"), FCSE::Crc32("pickup_syringe"),
        FCSE::Crc32("open_briefcase"), FCSE::Crc32("open_door"),
        FCSE::Crc32("open_door_to_weaponsafe"), FCSE::Crc32("open_door_out_of_weaponsafe"),
        FCSE::Crc32("open_computer")};
    // How long the pawn stays in first person before an interaction held back for it starts, in
    // milliseconds; started at the switch, the state still waits on the third-person animations.
    constexpr DWORD kSettle = 150;
    // How long a sent interaction may take to leave idle before it counts as refused.
    constexpr DWORD kStartTimeout = 1000;

    // CGOStateMachineDriver::BelongsToGroup(&group), __thiscall, RET 4: whether a state the machine
    // is in belongs to the group, the Crc32 of its full name.
    using BelongsFn = bool(__fastcall*)(uint8_t* driver, void* unused, const uint32_t* group);
    FCSE::Relocation<BelongsFn> g_belongs{FCSE::Uplay(0x0029DBA0)};
    // The states an interaction starts from and returns to.
    constexpr uint32_t kIdleGroup = FCSE::Crc32("::Interactions/InteractionsIdleGroup");

    // An interaction over the shoulder: held back while the pawn settles in first person, sent and
    // not yet out of idle, then running until idle again.
    enum class Interaction { None, HeldBack, Started, Running };

    // How long the gun stays up after a shot, in milliseconds.
    constexpr DWORD kGunUpAfterShot = 1000;

    // CGameConfig::UseHelpCrosshair(difficulty), __thiscall, which the HUD asks every frame whether
    // to draw the game's own crosshair.
    using UseCrosshairFn = int32_t(__fastcall*)(void* config, void* unused, int32_t difficulty);
    FCSE::Relocation<UseCrosshairFn> g_useCrosshair{FCSE::Uplay(0x00048860)};
    UseCrosshairFn g_originalUseCrosshair = nullptr;

    // CFCXMainHudUI::UpdateCrosshair(dt), __thiscall, RET 4: the HUD's crosshair, drawn while the
    // HUD's own show flag is set.
    using UpdateCrosshairFn = void(__fastcall*)(uint8_t* hud, void* unused, float dt);
    FCSE::Relocation<UpdateCrosshairFn> g_updateCrosshair{FCSE::Uplay(0x0080AF10)};
    UpdateCrosshairFn g_originalUpdateCrosshair = nullptr;
    constexpr ptrdiff_t kHudShowCrosshair = 0x321;

    // A gun's select and deselect, __thiscall, plain RET: they show and hide the HUD's crosshair
    // when the wielder is in first person. CEquipmentBase::GetWielder, on the gun's equipment at
    // +0x40, gives its pawn or null.
    using WeaponFn = void(__fastcall*)(uint8_t* weapon);
    using WielderFn = uint8_t*(__fastcall*)(uint8_t* equipment);
    FCSE::Relocation<WeaponFn> g_weaponSelect{FCSE::Uplay(0x00110A70)};
    FCSE::Relocation<WeaponFn> g_weaponDeselect{FCSE::Uplay(0x00110B10)};
    FCSE::Relocation<WielderFn> g_wielder{FCSE::Uplay(0x00156BA0)};
    WeaponFn g_originalWeaponSelect = nullptr;
    WeaponFn g_originalWeaponDeselect = nullptr;
    constexpr ptrdiff_t kWeaponEquipment = 0x40;

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
    // Game thread: the pawn's state machine, and an interaction seen in first person: its stage,
    // when it was held back, and its signal.
    uint8_t* g_driver = nullptr;
    Interaction g_interaction = Interaction::None;
    DWORD g_interactionAt = 0;
    uint32_t g_heldBack = 0;
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

    // The player's crosshair shown or hidden as a gun raised or put away sets it, which the game
    // only does while the wielder is in first person; -1 for nothing pending.
    std::atomic<int> g_crosshairShown{-1};

    void PlayerWeaponChanged(uint8_t* weapon, bool shown) {
        uint8_t* wielder = g_wielder(ThirdPerson::Field<uint8_t*>(weapon, kWeaponEquipment));
        if (wielder != nullptr && wielder == g_pawn && !Pawn::FirstPerson(wielder)) {
            g_crosshairShown = shown;
        }
    }

    void __fastcall WeaponSelectDetour(uint8_t* weapon) {
        PlayerWeaponChanged(weapon, true);
        g_originalWeaponSelect(weapon);
    }

    void __fastcall WeaponDeselectDetour(uint8_t* weapon) {
        PlayerWeaponChanged(weapon, false);
        g_originalWeaponDeselect(weapon);
    }

    void __fastcall UpdateCrosshairDetour(uint8_t* hud, void* unused, float dt) {
        const int shown = g_crosshairShown.exchange(-1);
        if (shown >= 0) {
            ThirdPerson::Field<uint8_t>(hud, kHudShowCrosshair) = static_cast<uint8_t>(shown);
        }
        g_originalUpdateCrosshair(hud, unused, dt);
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

    // Over the shoulder, holds an interaction back and drops to first person; Update starts it.
    void __fastcall SignalDetour(uint8_t* driver, void* unused, uint32_t signal) {
        if (driver == g_driver && g_view == View::Shoulder &&
            std::find(std::begin(kInteractions), std::end(kInteractions), signal) !=
                std::end(kInteractions)) {
            Switch(g_pawn, View::First);
            if (g_view == View::First) {
                g_interaction = Interaction::HeldBack;
                g_interactionAt = GetTickCount();
                g_heldBack = signal;
                return;
            }
        }
        g_originalSignal(driver, unused, signal);
    }

    // Sends a held-back interaction once the pawn has settled in first person, and lets the view go
    // back when the state machine is idle again.
    void UpdateInteraction() {
        if (g_interaction == Interaction::None) {
            return;
        }
        const DWORD since = GetTickCount() - g_interactionAt;
        const bool idle = g_belongs(g_driver, nullptr, &kIdleGroup);
        if (g_interaction == Interaction::HeldBack) {
            if (since >= kSettle) {
                g_originalSignal(g_driver, nullptr, g_heldBack);
                g_interaction = Interaction::Started;
            }
        } else if (g_interaction == Interaction::Started) {
            if (!idle) {
                g_interaction = Interaction::Running;
            } else if (since >= kSettle + kStartTimeout) {
                g_interaction = Interaction::None;
            }
        } else if (idle) {
            g_interaction = Interaction::None;
        }
    }
}

bool ThirdPerson::Outside::Install() {
    Ray::Install();
    if (!g_wielder || !Hook(g_updateCrosshair.address(), &UpdateCrosshairDetour, &g_originalUpdateCrosshair) ||
        !Hook(g_weaponSelect.address(), &WeaponSelectDetour, &g_originalWeaponSelect) ||
        !Hook(g_weaponDeselect.address(), &WeaponDeselectDetour, &g_originalWeaponDeselect)) {
        FCSE::ApiPointer()->Log("third person: a gun raised over the shoulder may keep the crosshair hidden");
    }
    if (!Hook(g_useCrosshair.address(), &UseCrosshairDetour, &g_originalUseCrosshair) ||
        !Hook(g_spread.address(), &SpreadDetour, &g_originalSpread) ||
        !Hook(g_setup3rd.address(), &Setup3rdDetour, &g_originalSetup3rd)) {
        FCSE::ApiPointer()->Log("third person: the crosshair or the third-person shot was not found");
    }
    if (!g_belongs || !Hook(g_signal.address(), &SignalDetour, &g_originalSignal)) {
        FCSE::ApiPointer()->Log("third person: a pickup or a door in third person may never finish");
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
        g_interaction = Interaction::None;
    }
    g_driver = Pawn::StateDriver(pawn);
    UpdateInteraction();
    if (!g_pressed && !g_wanted && !g_held && g_view == View::First) {
        return;
    }
    // A turret, a passenger seat, and getting in or out are seen in first person.
    const View where = Pawn::OnFoot(pawn) ? View::Shoulder : Driver::Driving() ? View::Chase : View::First;
    if (where != g_where) {
        g_where = where;
        g_held = false;
        g_interaction = Interaction::None;
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
        Pawn::SetFirstPerson(pawn, true);
        Pawn::SetGunUp(pawn, true);
        g_view = View::First;
        g_camera = nullptr;
        g_held = true;
    }
    const View wanted = g_wanted && !g_held && g_interaction == Interaction::None ? where : View::First;
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
