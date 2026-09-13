// The engine's own sky dome and god-ray mask clouds, each replaced at the one draw call that is it.
//
// Our clouds are drawn when a pass ends, which is after everything in it - but a sky belongs under
// the sun, the moon and the stars, so it has to be drawn where the dome is drawn. There is no seam
// at the start of a pass, so the seam is the dome's own draw call: it is recognised as it is
// submitted, dropped, and a sky of ours drawn in its place. Everything the engine draws after it
// then lands on our sky exactly as it landed on the engine's.
//
// The same hook replaces the god-ray mask's cloud draw, the one draw that multiplies its target by
// what it lets through, so that the mask follows our clouds rather than the engine's, and draws the
// moon without the fog the engine hides a low moon in.
//
// See docs/docs/engine-internals/presentation-and-input.md for what named that call.
#pragma once

#include <cstdint>
#include <d3d9.h>

namespace SkyOverhaul::DomeDraw {

// Whether the engine draws its own dome or ours is drawn instead.
enum class Mode {
    Engine,
    Overhaul,
};

// Draws in place of one of the engine's draws, with the device set exactly as that draw found it.
// True means it drew, and the engine's own draw is dropped; false leaves the engine to draw it, so
// a substitute that cannot draw yet costs nothing but what it did not replace.
using SubstituteFn = bool (*)(IDirect3DDevice9* device);

// Takes over the device's draw entry point. Call once from FCSE_Load. False means it could not be
// taken, which it logs, and nothing is left hooked. Starts in Engine mode, so nothing changes
// until something asks for it.
bool Install(SubstituteFn substitute);

// Takes effect on the next draw.
void SetMode(Mode mode);

// Draws in place of the god-ray mask's cloud draw, with the device set as that draw found it. True
// means it drew, and the engine's draw is dropped.
void SetMaskSubstitute(SubstituteFn mask);

// How many domes have been replaced. A count that stops climbing while the mode is Overhaul is a
// dome that stopped being recognised, which is the one failure that would otherwise be silent.
uint32_t SubstituteCount();

// How many moons have been drawn without the engine's fog.
uint32_t UnfoggedMoonCount();

// The visibility and HDR multiplier the engine handed the last moon drawn, before the cap.
void MoonParameters(float& visibility, float& multiplier);

}
