// The combat log: every line also goes to fcse.log, and to this session's file in
// bin\difficulty-overhaul-logs, which keeps the last sessions that fcse.log loses at each launch.
#pragma once

namespace DifficultyOverhaul {

// Writes one line printf-style, stamped with the local time in the session's file.
void CombatLogf(const char* format, ...);

}
