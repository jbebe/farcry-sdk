// Every line any plugin sends on FCSE's `combat` channel, this plugin's hits and deaths among them,
// written to this session's file in bin\devtools-logs.
#pragma once

namespace DevTools::CombatLog {

// Starts logging if the game was launched with -combatlog.
void Start();

}
