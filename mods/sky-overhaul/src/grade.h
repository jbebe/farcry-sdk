// The final colour grade, drawn with values of our own instead of the weather preset's.
#pragma once

#include "engine/frame.h"

namespace SkyOverhaul::Grade {

// Logs what the engine asked for beside what was drawn. Runs on the composite.
void OnFinalPass(const Frame::Pass& pass);

void SetEnabled(bool enabled);

}
