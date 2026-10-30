// The overlay's counterpart for programs: a local named pipe taking one command a line and answering
// each with one line of JSON. mods/DevTools/README.md lists the commands.
#pragma once

namespace DevTools::Remote {

// Starts serving if the game was launched with -devtoolsremote. Off otherwise, because whatever
// connects can run any Lua in the game.
void Start();

}
