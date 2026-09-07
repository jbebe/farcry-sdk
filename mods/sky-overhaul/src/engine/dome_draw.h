// The engine's own sky dome, replaced at the one draw call that is it.
//
// Our clouds are drawn when a pass ends, which is after everything in it - but a sky belongs under
// the sun, the moon and the stars, so it has to be drawn where the dome is drawn. There is no seam
// at the start of a pass, so the seam is the dome's own draw call: it is recognised as it is
// submitted, dropped, and a sky of ours drawn in its place. Everything the engine draws after it
// then lands on our sky exactly as it landed on the engine's.
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

// Draws a sky in the dome's place, with the device set exactly as the dome would have found it.
// True means it drew, and the dome's own draw is dropped; false leaves the engine to draw it, so a
// substitute that cannot draw yet costs nothing but the sky it did not replace.
using SubstituteFn = bool (*)(IDirect3DDevice9* device);

// Takes over the device's draw entry point. Call once from FCSE_Load. False means it could not be
// taken, which it logs, and nothing is left hooked. Starts in Engine mode, so nothing changes
// until something asks for it.
bool Install(SubstituteFn substitute);

// Takes effect on the next draw.
void SetMode(Mode mode);

// How many domes have been replaced. A count that stops climbing while the mode is Overhaul is a
// dome that stopped being recognised, which is the one failure that would otherwise be silent.
uint32_t SubstituteCount();

}
