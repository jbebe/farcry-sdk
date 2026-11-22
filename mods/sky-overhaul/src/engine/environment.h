// The environment manager's frame, where the weather is read and handed to the game's own storm,
// wind, rain and fog. See docs/docs/engine-internals/time-of-day-and-lighting.md.
#pragma once

namespace SkyOverhaul::Environment {

// Hooks the manager's weather update, wind push, rain update and fog. Call once from FCSE_Load. A
// site not found is logged, and what it carries stays the game's; without the weather update
// nothing is read at all.
void Install();

// The sky's fog by height in the shader's form, as the game's own fog sets it before the weather
// reshapes the land's. From any thread; false before the first frame.
bool SkyFogHeight(float out[4]);

}
