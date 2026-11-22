// The engine's linear depth stored in half floats rather than eight bits a channel, so it holds the
// same packing without the centimetre steps that rounding to 255ths leaves at fixed depths. See
// docs/docs/engine-internals/presentation-and-input.md#storing-it-in-half-floats.
#pragma once

namespace SkyOverhaul::LinearDepth {

// Changes the format the renderer asks for its two linear depth targets, which it takes on the next
// frame. Call once from FCSE_Load. False means a site was not found, which it logs, and the depth
// stays as it shipped.
bool Install();

}
