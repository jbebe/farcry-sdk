// Sky Overhaul's weather on the Weather tab: a button to hold each weather, a storm put down by hand,
// and what is overhead.
#pragma once

#include <string>

namespace DevTools::SkyWeather {

// Says Sky Overhaul is needed when SkyOverhaul.dll is not loaded.
void Draw();

// Holds the weather with this id ("C5"), or lets the timeline run with "timeline". Empty, or why it
// could not be done.
std::string Hold(const std::string& id);

// Puts a storm down: "over", "ahead <metres>", "upwind <metres>", "<degrees> <metres>", or takes it
// away with "off". Empty, or why it could not be done.
std::string Plant(const std::string& where);

}
