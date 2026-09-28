#include "grade.h"

#include "engine/dome_draw.h"
#include "tuning.h"

#include <algorithm>
#include <cmath>

namespace {
    using SkyOverhaul::Tuning::Values;

    // The weights the final pass takes its grey by.
    constexpr float kGreyWeights[3] = {0.3086f, 0.6094f, 0.0820f};

    // The power each channel is raised to, whose geometric mean is the brightness.
    void Powers(const Values& v, float out[3]) {
        const float tint = v.gradeTint / 3.0f;
        out[0] = v.gradeBrightness * std::exp(-v.gradeWarmth - tint);
        out[1] = v.gradeBrightness * std::exp(2.0f * tint);
        out[2] = v.gradeBrightness * std::exp(v.gradeWarmth - tint);
    }

    // ContrastData for lerp(c, smoothstep(c), contrast), which keeps black and white.
    void Contrast(const Values& v, float out[3]) {
        out[0] = -2.0f * v.gradeContrast;
        out[1] = 3.0f * v.gradeContrast;
        out[2] = 1.0f - v.gradeContrast;
    }

    void Override(const float engine[12], float out[12]) {
        const Values v = SkyOverhaul::Tuning::Current();
        std::copy_n(engine, 12, out);
        out[0] = v.gradeSaturation;
        Powers(v, out + 4);
        Contrast(v, out + 8);
    }
}

void SkyOverhaul::Grade::Apply(const Values& v, const float in[3], float out[3]) {
    float powers[3];
    float contrast[3];
    Powers(v, powers);
    Contrast(v, contrast);
    float grey = 0.0f;
    for (int i = 0; i < 3; i++) {
        const float c = std::pow(std::clamp(in[i], 0.0f, 1.0f), powers[i]);
        out[i] = c * (contrast[2] + c * (contrast[1] + c * contrast[0]));
        grey += kGreyWeights[i] * out[i];
    }
    for (int i = 0; i < 3; i++) {
        out[i] = grey + (out[i] - grey) * v.gradeSaturation;
    }
}

void SkyOverhaul::Grade::SetEnabled(bool enabled) {
    DomeDraw::SetGrade(enabled ? &Override : nullptr);
}

