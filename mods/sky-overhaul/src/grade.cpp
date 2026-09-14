#include "grade.h"

#include "engine/clock.h"
#include "engine/dome_draw.h"
#include "fcse_api.h"
#include "tuning.h"

#include <algorithm>

namespace {
    // Saturation, ColorRemapData and ContrastData, as the engine last set them and as they were
    // drawn instead.
    float g_engine[12] = {};
    float g_ours[12] = {};
    bool g_enabled = false;

    SkyOverhaul::Stopwatch g_clock;
    SkyOverhaul::Heartbeat g_heartbeat{2.0f};

    // The final pass raises each channel c to its own power, bends it by c(Z + c(Y + cX)) from
    // ContrastData, then blends toward grey by the saturation. Ours is the cubic
    // lerp(c, smoothstep(c), contrast), which keeps black and white where they are.
    void Override(const float engine[12], float out[12]) {
        const SkyOverhaul::Tuning::Values v = SkyOverhaul::Tuning::Current();
        std::copy_n(engine, 12, g_engine);
        std::copy_n(engine, 12, out);
        out[0] = v.gradeSaturation;
        out[4] = v.gradeRed;
        out[5] = v.gradeGreen;
        out[6] = v.gradeBlue;
        out[8] = -2.0f * v.gradeContrast;
        out[9] = 3.0f * v.gradeContrast;
        out[10] = 1.0f - v.gradeContrast;
        std::copy_n(out, 12, g_ours);
    }
}

void SkyOverhaul::Grade::OnFinalPass(const Frame::Pass& pass) {
    if (!g_enabled || !g_heartbeat.Due(g_clock.Lap())) {
        return;
    }
    FCSE::Logf("grade f%u: engine sat %.3f remap (%.3f %.3f %.3f) contrast (%.3f %.3f %.3f) | "
               "ours sat %.3f remap (%.3f %.3f %.3f) contrast (%.3f %.3f %.3f)",
               pass.frame, g_engine[0], g_engine[4], g_engine[5], g_engine[6], g_engine[8],
               g_engine[9], g_engine[10], g_ours[0], g_ours[4], g_ours[5], g_ours[6], g_ours[8],
               g_ours[9], g_ours[10]);
}

void SkyOverhaul::Grade::SetEnabled(bool enabled) {
    g_enabled = enabled;
    DomeDraw::SetGrade(enabled ? &Override : nullptr);
}
