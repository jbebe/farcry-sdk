// Every frame presented, written to a CSV in bin\, for comparing a session with a mod against one
// without. mods/DevTools/README.md lists the columns.
#pragma once

namespace DevTools::SessionLog {

// Starts logging if the game was launched with -perftest. Off otherwise, because it writes
// a few megabytes every few minutes.
void Start();

}
