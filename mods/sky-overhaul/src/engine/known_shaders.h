// The engine's own pixel shaders that a draw is recognised by, named by their bytecode.
//
// The device hands back the exact bytecode shipped in shadersobj, so a CRC-32 of it names the
// object. See docs/docs/file-formats/shader-objects.md.
#pragma once

#include <d3d9.h>

namespace SkyOverhaul::KnownShaders {

enum class Kind {
    Other,
    // Reads the linear depth texture at s0.
    DepthReader,
    // The fogged CelestialBody sprite the moon is drawn with.
    Moon,
    // The final pass, with Saturation, ColorRemapData and ContrastData a register each.
    Grade,
};

struct Known {
    Kind kind;
    // Where a grade's three registers start.
    UINT firstRegister;
};

// What the pixel shader bound right now is. Remembered per shader, so only the first draw through
// each one pays for reading its bytecode.
Known Bound(IDirect3DDevice9* device);

// Forgets every shader seen, whose addresses a new device may reuse.
void Forget();

}
