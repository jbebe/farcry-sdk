// The game's clock, set through DevTools' console, since this plugin reaches no console of its own.
#pragma once

namespace SkyOverhaul::TimeOfDay {

// Sets the clock to minutes past midnight, from which the day runs on. Does nothing without DevTools.
void Set(int minutes);

}
