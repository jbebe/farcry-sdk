// A knockdown stim floors the player: knockout.lua's Faint when they survive it, Die when not.
#pragma once

#include <string>

namespace WildlifeOverhaul::Knockdown {

// `script` is knockout.lua's text. False, and logged, when a hook cannot be installed.
bool Install(std::string script);

}
