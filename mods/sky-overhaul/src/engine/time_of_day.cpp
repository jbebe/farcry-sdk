#include "engine/time_of_day.h"

#include "devtools_api.h"

#include <cstdio>

void SkyOverhaul::TimeOfDay::Set(int minutes) {
    char line[96];
    std::snprintf(line, sizeof(line),
                  "#CDynamicEnvironmentManager_GetInstance():SetScriptedTimeOfDay(%d, %d)",
                  minutes / 60, minutes % 60);
    DevTools::Overlay::PostLine(line);
}
