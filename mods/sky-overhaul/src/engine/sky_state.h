// Where the sun is, taken from the engine rather than guessed.
#pragma once

struct IDirect3DDevice9;

namespace SkyOverhaul::SkyState {

// Hooks the sun-disc draw. Call once from FCSE_Load. False means the sun's direction is
// unavailable, which it logs, and nothing is left hooked.
bool Install();

// The sun's world direction, unit length, Z up. False until the first frame of a loaded world.
bool SunDirection(float out[3]);

// Whether the engine has drawn the sun since this was last asked. A menu, a loading screen and a
// cutscene draw no sky at all, so this is what keeps the glare out of them - the alternative,
// guessing at game state, would have to be kept in step with every screen the game can show.
bool ConsumeSunDrawn();

// The device, handed over once it is known, so the sky draw can sample shader constants while the
// engine still has the sky's own bound - by the end of the frame they belong to whatever drew last.
void SetDevice(IDirect3DDevice9* device);

// How much of the sun the engine could see, sampled during the sky draw rather than at the end of
// the frame. Negative when nothing plausible has been read.
float SunVisibility();

}
