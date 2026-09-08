// Flying: noclip, and the engine's own free camera.
//
// See docs/docs/engine-internals/free-camera-and-noclip.md.
#include "engine/debug_camera.h"

#include "engine/camera.h"
#include "engine/entity.h"
#include "engine/keys.h"
#include "engine/pawn_tick.h"
#include "engine/player.h"
#include "fcse_api.h"

#include <cmath>
#include <cstdint>
#include <windows.h>

namespace {
    using Mode = DevTools::DebugCamera::Mode;

    // Fixed bindings. Only the two mode keys are configurable.
    constexpr int kForward = 'W';
    constexpr int kBack = 'S';
    constexpr int kStrafeLeft = 'A';
    constexpr int kStrafeRight = 'D';
    constexpr int kUp = VK_SPACE;
    constexpr int kDown = VK_LCONTROL;
    constexpr int kSpeedCycle = VK_LSHIFT;
    constexpr int kSlowCycle = VK_LMENU;
    constexpr int kLookLeft = VK_LEFT;
    constexpr int kLookRight = VK_RIGHT;
    constexpr int kLookUp = VK_UP;
    constexpr int kLookDown = VK_DOWN;

    // A whole unit of look rate is a half turn a second, too fast for a key.
    constexpr float kLookRate = 0.35f;

    // The engine integrates look as angle += delta * frameTime * 180 degrees.
    constexpr float kLookRadiansPerUnit = 3.14159265f;

    constexpr float kBaseSpeed = 12.0f;
    constexpr float kSpeedSteps[] = {0.125f, 0.25f, 0.5f, 1.0f, 2.0f, 4.0f, 8.0f, 16.0f, 32.0f, 64.0f};
    constexpr size_t kBaseSpeedStep = 3;

    // Noclip's ceiling; the two steps above it are freecam only, where nothing has to keep up.
    constexpr size_t kNoclipTopStep = 7;

    // CPawnInputListener's look accumulators. Y is horizontal, X vertical.
    constexpr ptrdiff_t kListenerLookX = 0x10;
    constexpr ptrdiff_t kListenerLookY = 0x14;

    // Off pawn+0x10: requested and current state, each with a flags byte carrying the sprint bit.
    constexpr ptrdiff_t kPawnStateBlock = 0x10;
    constexpr ptrdiff_t kPawnRequestedState = 0x140;
    constexpr ptrdiff_t kPawnCurrentState = 0x2D0;
    constexpr ptrdiff_t kStateFlags = 0x04;
    constexpr uint8_t kSprintFlag = 0x40;

    // Falling, same block, and the tail of the fall update that reports not falling.
    constexpr ptrdiff_t kPawnFalling = 0x49B;
    constexpr ptrdiff_t kPawnFallFrames = 0x4A0;
    constexpr size_t kFallUpdateTail = 0x303;

    // The signals noclip refuses, by dispatcher CRC32.
    constexpr uint32_t kSignalPauseMenu = 0x04127107;
    constexpr uint32_t kSignalQuickSave = 0xEFEF8B90;
    constexpr uint32_t kSignalQuickLoad = 0x9F8F5553;

    using GameSignalFn = bool(__fastcall*)(void* dispatcher, void* unused, const uint32_t* signal,
                                           void* context);

    // Neither is an entry the address library knows, so both stay on patterns.
    FCSE::Relocation<GameSignalFn> g_gameSignal{
        FCSE::Pattern("A1 ?? ?? ?? ?? 83 EC 50 A8 01 53 55 56 57 8B F9")};
    FCSE::Relocation<uint8_t*> g_fallUpdate{FCSE::Pattern(
        "80 BE 9B 04 00 00 00 F3 0F 10 05 ?? ?? ?? ?? C6 44 24 12 00 F3 0F 11 44 24 18 0F 85 ?? ?? "
        "?? ?? F6 86 D4 02 00 00 20")};

    GameSignalFn g_originalGameSignal = nullptr;
    uintptr_t g_fallUpdateRejoin = 0;

    Mode g_mode = Mode::None;
    size_t g_speedStep = kBaseSpeedStep;

    // One entry per mode, indexed by the enumerator, so binding and polling are the same table.
    struct Binding {
        int key;
        bool latch;
    };

    Binding g_bindings[3]{};

    // Noclip.
    void* g_noclipRef = nullptr;
    void* g_noclipPhysics = nullptr;
    uintptr_t g_noclipStateBlock = 0;
    float g_noclipYaw = 0.0f;
    float g_noclipPitch = 0.0f;

    // Freecam, as one object: leaving the mode is assigning an empty one.
    struct Freecam {
        void* camera;
        void* manager;
        uint8_t savedLocked;
        bool fedMove;
        bool fedLook;
        float mouseLookX;
        float mouseLookY;
    };

    Freecam g_freecam{};

    struct MoveAxes {
        float forward;
        float strafe;
        float vertical;
    };

    float Axis(int positive, int negative) {
        return (DevTools::Keys::Down(positive) ? 1.0f : 0.0f) -
               (DevTools::Keys::Down(negative) ? 1.0f : 0.0f);
    }

    MoveAxes ReadMoveAxes() {
        return {Axis(kForward, kBack), Axis(kStrafeRight, kStrafeLeft), Axis(kUp, kDown)};
    }

    void LeaveNoclip() {
        if (g_noclipRef == nullptr) {
            return;
        }

        // Collision back before the reference goes, so the last thing touched is still alive.
        DevTools::Entity::SetPhysicsEnabled(g_noclipPhysics, true);

        g_noclipStateBlock = 0;
        g_noclipPhysics = nullptr;
        DevTools::Entity::ReleaseRef(g_noclipRef);
    }

    bool EnterNoclip(void* manager) {
        void* ref = DevTools::Entity::AcquireFocusRef(manager);
        void* entity = DevTools::Entity::Of(ref);

        if (entity == nullptr) {
            DevTools::Entity::ReleaseRef(ref);
            return false;
        }

        void* physics = DevTools::Entity::PhysicsComponent(entity);
        if (physics == nullptr) {
            DevTools::Entity::ReleaseRef(ref);
            return false;
        }

        DevTools::Entity::SetPhysicsEnabled(physics, false);

        // Seeded from the body's facing; local +Y is forward, hence the quarter turn.
        const float* matrix = DevTools::Entity::Matrix(entity);
        g_noclipYaw = std::atan2(matrix[5], matrix[4]) - 1.57079633f;

        // Pitch only exists on the camera.
        g_noclipPitch = 0.0f;
        DevTools::Camera::ViewPitch(manager, g_noclipPitch);

        g_noclipRef = ref;
        g_noclipPhysics = physics;
        return true;
    }

    void ApplyNoclip(const DevTools::PawnTick::Frame& frame, void* manager) {
        void* entity = DevTools::Entity::Of(g_noclipRef);
        if (entity == nullptr) {
            return;
        }

        // Sprint goes nowhere but still reaches the animation layer: clear request and current.
        const uintptr_t state =
            frame.pawn != 0 ? *reinterpret_cast<uintptr_t*>(frame.pawn + kPawnStateBlock) : 0;
        g_noclipStateBlock = state;

        if (state != 0) {
            *reinterpret_cast<uint8_t*>(state + kPawnRequestedState + kStateFlags) &=
                static_cast<uint8_t>(~kSprintFlag);
            *reinterpret_cast<uint8_t*>(state + kPawnCurrentState + kStateFlags) &=
                static_cast<uint8_t>(~kSprintFlag);
        }

        // Physics off takes the character controller with it, so the body cannot turn and the head
        // bone cannot look; yaw is integrated here off the same accumulator.
        if (frame.listener != 0 && frame.delta > 0.0f) {
            g_noclipYaw -= *reinterpret_cast<const float*>(frame.listener + kListenerLookY) *
                           frame.delta * kLookRadiansPerUnit;
        }

        // Pitch steers flight only, so it is read off the camera rather than integrated: this
        // module's wall clock drifts against engine frame time.
        DevTools::Camera::ViewPitch(manager, g_noclipPitch);

        // The body takes the yaw and stays upright; pitching it pitches the head bone and the arms.
        DevTools::Entity::SetEuler(entity, 0.0f, 0.0f, g_noclipYaw);

        if (frame.delta <= 0.0f || !frame.focused) {
            return;
        }

        const MoveAxes move = ReadMoveAxes();
        if (move.strafe == 0.0f && move.forward == 0.0f && move.vertical == 0.0f) {
            return;
        }

        const float step = kBaseSpeed * kSpeedSteps[g_speedStep] * frame.delta;

        // The entity's basis: X strafe, Y forward, Z up. SetEuler above already rebuilt it.
        const float* matrix = DevTools::Entity::Matrix(entity);
        const float* right = matrix;
        const float* ahead = matrix + 4;
        const float* up = matrix + 8;

        // The body is upright, so flight pitch is applied here rather than read out of the basis.
        const float cos = std::cos(g_noclipPitch);
        const float sin = std::sin(g_noclipPitch);

        float moved[3]{};
        for (int i = 0; i < 3; ++i) {
            const float aheadTilted = ahead[i] * cos + up[i] * sin;
            moved[i] = (move.strafe * right[i] + move.forward * aheadTilted + move.vertical * up[i]) *
                       step;
        }

        DevTools::Entity::SetPosition(entity, matrix[12] + moved[0], matrix[13] + moved[1],
                                      matrix[14] + moved[2]);
    }

    // A level load destroys the manager and a cutscene may take the camera: ask the engine first.
    bool CameraStillOurs(void* manager) {
        return g_freecam.camera != nullptr && manager != nullptr && manager == g_freecam.manager &&
               DevTools::Camera::Active(manager) == g_freecam.camera;
    }

    // Locked byte back if its manager is still live, then forget. Every exit path ends here.
    void ForgetFreecam(void* manager) {
        if (g_freecam.manager != nullptr && g_freecam.manager == manager) {
            *DevTools::Camera::Locked(g_freecam.manager) = g_freecam.savedLocked;
        }
        g_freecam = {};
    }

    void LeaveFreecam(void* manager) {
        if (g_freecam.camera == nullptr) {
            return;
        }

        if (CameraStillOurs(manager)) {
            DevTools::Camera::ClearAxes(g_freecam.camera);
            DevTools::Camera::ActivateByName(g_freecam.manager, DevTools::Camera::kGameplay);

            // The gameplay camera did not take: keep it, or the key no longer exits the mode.
            if (DevTools::Camera::Active(g_freecam.manager) == g_freecam.camera) {
                return;
            }
        }

        ForgetFreecam(manager);
    }

    bool EnterFreecam(void* manager) {
        if (manager == nullptr) {
            return false;
        }

        void* previous = DevTools::Camera::Active(manager);

        // Locked makes the switch a silent no-op. Clear in ordinary gameplay, set by a cutscene.
        uint8_t* locked = DevTools::Camera::Locked(manager);
        const uint8_t wasLocked = *locked;
        *locked = 0;

        DevTools::Camera::ActivateByName(manager, DevTools::Camera::kFree);
        void* camera = DevTools::Camera::Active(manager);

        // The switch reports nothing, and an archetype the data does not carry leaves the old
        // camera up rather than failing.
        if (camera == nullptr || camera == previous) {
            *locked = wasLocked;
            return false;
        }

        // Axes and position persist between activations.
        DevTools::Camera::ClearAxes(camera);
        DevTools::Camera::SnapToFocus(manager, camera);

        g_freecam = {camera, manager, wasLocked, false, false, 0.0f, 0.0f};
        return true;
    }

    void LeaveAll(void* manager) {
        LeaveNoclip();
        LeaveFreecam(manager);
        g_mode = Mode::None;
    }

    // Called from the free camera's own update, so input lands in the frame that reads it.
    void FeedCameraInput(void* camera) {
        if (g_mode != Mode::Freecam || camera != g_freecam.camera) {
            return;
        }

        const bool focused = DevTools::Keys::Focused();

        // Downstream of the axes, which stay unit vectors.
        DevTools::Camera::SetSpeed(camera, kBaseSpeed * kSpeedSteps[g_speedStep]);

        const MoveAxes move = focused ? ReadMoveAxes() : MoveAxes{};

        if (move.forward != 0.0f || move.strafe != 0.0f || move.vertical != 0.0f) {
            DevTools::Camera::SetMove(camera, move.forward, move.strafe, move.vertical);
            g_freecam.fedMove = true;
        } else if (g_freecam.fedMove) {
            // The shipped free_camera mapping also writes these, and only when an action fires, so
            // a released key would coast. One pass of zeros, then hands off.
            DevTools::Camera::SetMove(camera, 0.0f, 0.0f, 0.0f);
            g_freecam.fedMove = false;
        }

        // The mouse arrives in the keys' own units, and is consumed rather than held.
        const float yaw = (focused ? Axis(kLookRight, kLookLeft) * kLookRate : 0.0f) +
                          g_freecam.mouseLookX;

        // Pitch integrates with a negated scale, so up is negative; the sample already has that sign.
        const float pitch = (focused ? -Axis(kLookUp, kLookDown) * kLookRate : 0.0f) +
                            g_freecam.mouseLookY;

        g_freecam.mouseLookX = 0.0f;
        g_freecam.mouseLookY = 0.0f;

        if (yaw != 0.0f || pitch != 0.0f) {
            DevTools::Camera::SetLook(camera, yaw, pitch);
            g_freecam.fedLook = true;
        } else if (g_freecam.fedLook) {
            DevTools::Camera::SetLook(camera, 0.0f, 0.0f);
            g_freecam.fedLook = false;
        }
    }

    // The dispatcher all three signals are acted on in. True is the original's own refusal:
    // handled, nothing done.
    bool __fastcall GameSignalDetour(void* dispatcher, void* unused, const uint32_t* signal,
                                     void* context) {
        if (g_mode == Mode::Noclip && signal != nullptr &&
            (*signal == kSignalPauseMenu || *signal == kSignalQuickSave ||
             *signal == kSignalQuickLoad)) {
            return true;
        }
        return g_originalGameSignal(dispatcher, unused, signal, context);
    }

    // Physics off removes the ground, so the fall update is sent straight to its not-falling tail -
    // for the player's own state block only.
    void FallUpdateHandler(FCSE_MidHookContext* ctx) {
        if (g_mode != Mode::Noclip || ctx->esi == 0 || ctx->esi != g_noclipStateBlock) {
            return;
        }

        *reinterpret_cast<uint8_t*>(ctx->esi + kPawnFalling) = 0;
        *reinterpret_cast<int32_t*>(ctx->esi + kPawnFallFrames) = 0;
        ctx->eip = g_fallUpdateRejoin;
    }

    void StepSpeed(int direction) {
        const size_t top =
            g_mode == Mode::Noclip ? kNoclipTopStep : (sizeof(kSpeedSteps) / sizeof(kSpeedSteps[0])) - 1;

        if (direction > 0) {
            g_speedStep = (g_speedStep >= top) ? kBaseSpeedStep : g_speedStep + 1;
        } else {
            g_speedStep = (g_speedStep == 0) ? kBaseSpeedStep : g_speedStep - 1;
        }
    }

    void Toggle(Mode mode, void* manager) {
        if (g_mode == mode) {
            LeaveAll(manager);
            return;
        }

        LeaveAll(manager);

        const bool entered = mode == Mode::Noclip ? EnterNoclip(manager) : EnterFreecam(manager);
        if (!entered) {
            if (mode == Mode::Freecam) {
                FCSE::ApiPointer()->Log("freecam: the camera manager would not activate "
                                        "Cameras.Camera.Free - this data set does not carry the "
                                        "free camera archetype");
            }
            return;
        }

        g_mode = mode;

        // A step chosen in freecam can sit past noclip's lower ceiling.
        if (mode == Mode::Noclip && g_speedStep > kNoclipTopStep) {
            g_speedStep = kNoclipTopStep;
        }
    }

    void OnTick(const DevTools::PawnTick::Frame& frame) {
        void* manager = DevTools::Player::CameraManager(frame.local);

        if (g_mode == Mode::Freecam) {
            // Mouse look for the free camera, off the listener's accumulators.
            if (frame.listener != 0) {
                g_freecam.mouseLookX = *reinterpret_cast<float*>(frame.listener + kListenerLookX);
                g_freecam.mouseLookY = *reinterpret_cast<float*>(frame.listener + kListenerLookY);
            }

            // A level load or a cutscene taking the camera ends the mode where it stands.
            if (!CameraStillOurs(manager)) {
                ForgetFreecam(manager);
                g_mode = Mode::None;
            }
        } else if (g_mode == Mode::Noclip) {
            // The entity going away is a level load. Drop the reference rather than follow it.
            if (DevTools::Entity::Of(g_noclipRef) == nullptr) {
                LeaveNoclip();
                g_mode = Mode::None;
            } else {
                ApplyNoclip(frame, manager);
            }
        }

        if (!frame.focused) {
            return;
        }

        for (size_t i = 0; i < sizeof(g_bindings) / sizeof(g_bindings[0]); ++i) {
            Binding& binding = g_bindings[i];
            if (binding.key != 0 && DevTools::Keys::Pressed(binding.key, binding.latch)) {
                Toggle(static_cast<Mode>(i), manager);
            }
        }

        // Outside a mode, Shift and Alt are the game's own sprint and lean bindings.
        if (g_mode == Mode::None) {
            return;
        }

        static bool speedLatch = false;
        static bool slowLatch = false;

        if (DevTools::Keys::Pressed(kSpeedCycle, speedLatch)) {
            StepSpeed(1);
        }
        if (DevTools::Keys::Pressed(kSlowCycle, slowLatch)) {
            StepSpeed(-1);
        }
    }
}

namespace DevTools::DebugCamera {

void Install() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    Camera::OnUpdate(&FeedCameraInput);
    PawnTick::Subscribe(&OnTick);

    if (!g_gameSignal || !api->Hook(reinterpret_cast<void*>(g_gameSignal.address()),
                                    reinterpret_cast<void*>(&GameSignalDetour),
                                    reinterpret_cast<void**>(&g_originalGameSignal))) {
        api->Log("debug camera: the signal dispatcher was not hooked - the pause menu and quicksave "
                 "stay reachable while noclip is up");
    }

    if (g_fallUpdate) {
        g_fallUpdateRejoin = g_fallUpdate.address() + kFallUpdateTail;
        api->MidHook(g_fallUpdate.get(), &FallUpdateHandler);
    } else {
        api->Log("debug camera: the fall update was not found in this build - noclip will read as "
                 "falling the whole time it is up");
    }
}

int Bind(Mode mode, int key) {
    const size_t index = static_cast<size_t>(mode);

    for (size_t i = 0; i < sizeof(g_bindings) / sizeof(g_bindings[0]); ++i) {
        if (i != index && key != 0 && g_bindings[i].key == key) {
            key = 0;
        }
    }

    g_bindings[index] = {key, false};

    // Unbound while up: put the player back rather than leave them with no way out.
    if (key == 0 && g_mode == mode) {
        LeaveAll(Player::CameraManager());
    }

    return key;
}

}
