// The final colour grade, drawn with values of our own instead of the weather preset's.
#pragma once

#include "engine/frame.h"

namespace SkyOverhaul::Tuning {
struct Values;
}

namespace SkyOverhaul::Grade {

// Logs what the engine asked for beside what was drawn. Runs on the composite.
void OnFinalPass(const Frame::Pass& pass);

// A colour on screen as the final pass would grade it with `v`.
void Apply(const Tuning::Values& v, const float in[3], float out[3]);

void SetEnabled(bool enabled);

}
