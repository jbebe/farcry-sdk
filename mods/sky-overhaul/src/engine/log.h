// One formatted line into fcse.log, so no call site has to size a buffer of its own.
#pragma once

#include "fcse_api.h"

#include <cstdarg>
#include <cstdio>

namespace SkyOverhaul {

inline void Logf(const char* format, ...) {
    char line[256];
    va_list args;
    va_start(args, format);
    std::vsnprintf(line, sizeof(line), format, args);
    va_end(args);
    FCSE::ApiPointer()->Log(line);
}

}
