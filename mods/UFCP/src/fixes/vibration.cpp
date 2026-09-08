// Controller vibration never happens.
//
// Ported from FC2JackalFix (MIT, (c) 2026 Joshhhuaaa, TGP482) - source/input/vibration.ixx.
//
// The Xbox 360 build rumbles; the PC build does not, on any pad, ever. Everything for it is still
// present - CCameraShakeAndPadRumbleComponent evaluates the archetype's rumble curves every frame,
// and the dispatcher below it calls XInputSetState - but the two motor arguments are pushed as
// constant zeroes, so the whole chain runs and asks for no vibration.
//
// The apply site is anchored on a prologue unique to this caller. Four other callers of
// SetVibrationAll share the FLDZ block that pushes the two floats, and they are shutdown, unload,
// pause and the destructor: those want their real zeroes, and catching them would leave a pad
// buzzing after the game stopped asking.
//
// Two hooks, because the evaluator's `this` does not survive the camera call between them. It is
// sampled at entry and consumed 0x37 bytes later, where ESP still points at the two motor slots;
// curve time only advances after the call returns, so both points see the same curve state.
#include "fcse_api.h"

#include "engine/input_device.h"

#include <cmath>
#include <cstdint>

namespace {
    // CCameraShakeAndPadRumbleComponent::EvalCurve, called back into rather than hooked.
    using EvalCurveFn = float(__fastcall*)(void* self, void* unused, int curveIndex);

    // Archetype curve indices for the two motors.
    constexpr int kCurveRumbleHighFrequency = 2;
    constexpr int kCurveRumbleLowFrequency = 3;

    // Entry to the MOV ECX,EAX before the dispatcher call, where ESP still holds the motor args.
    constexpr ptrdiff_t kMotorArgs = 0x37;

    FCSE::Relocation<EvalCurveFn> g_evalCurve{FCSE::Pattern(
        "83 EC 0C 0F 57 C0 53 55 8B E9 56 8D 45 58 57 F3 0F 11 44 24 14 33 DB")};

    FCSE::Relocation<uint8_t*> g_apply{FCSE::Pattern(
        "D9 44 24 04 51 D9 1C 24 E8 ?? ?? ?? ?? 8B 0D ?? ?? ?? ?? 83 79 08 00 76 25 6A 00")};

    float g_motorLowFrequency = 0.0f;
    float g_motorHighFrequency = 0.0f;

    // A NaN passes both clamp comparisons and reaches XInputSetState, where CVTTSS2SI turns it into
    // 0x80000000 and pins one motor to zero - so the finite test is the real guard, not the clamp.
    float Amplitude(void* self, int curveIndex) {
        if (self == nullptr || !g_evalCurve) {
            return 0.0f;
        }

        const float value = g_evalCurve(self, nullptr, curveIndex);
        if (!std::isfinite(value)) {
            return 0.0f;
        }

        return value < 0.0f ? 0.0f : (value > 1.0f ? 1.0f : value);
    }

    void ShakeEntryHandler(FCSE_MidHookContext* ctx) {
        // Falling through with zeroes is what makes a pad go quiet the moment it stops being the
        // device in use, rather than holding its last amplitude.
        if (!UFCP::IsPadActiveDevice()) {
            g_motorLowFrequency = 0.0f;
            g_motorHighFrequency = 0.0f;
            return;
        }

        void* self = reinterpret_cast<void*>(ctx->ecx);
        g_motorLowFrequency = Amplitude(self, kCurveRumbleLowFrequency);
        g_motorHighFrequency = Amplitude(self, kCurveRumbleHighFrequency);
    }

    // Both slots hold 0.0 on arrival. First argument is the left motor, second the right.
    void MotorArgsHandler(FCSE_MidHookContext* ctx) {
        *reinterpret_cast<float*>(ctx->esp) = g_motorLowFrequency;
        *reinterpret_cast<float*>(ctx->esp + 4) = g_motorHighFrequency;
    }
}

void ApplyVibrationFix() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!g_evalCurve || !g_apply) {
        api->Log("vibration: the rumble evaluator was not found in this build - controllers stay "
                 "silent");
        return;
    }

    if (!api->MidHook(reinterpret_cast<void*>(g_apply.address()), &ShakeEntryHandler)) {
        return;
    }

    if (api->MidHook(reinterpret_cast<void*>(g_apply.address() + kMotorArgs), &MotorArgsHandler)) {
        api->Log("controller vibration restored");
    }
}
