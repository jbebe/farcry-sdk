// The noise the clouds are shaped from, made once and kept for the life of the device.
//
// Generated on a worker thread because it is a few seconds of arithmetic, and uploaded into the
// managed pool, which survives a device reset and so needs nothing surrendered before one.
#pragma once

#include <d3d9.h>

#include <cstdint>

namespace SkyOverhaul::Noise {

// Begins generating. Call once from FCSE_Load.
void Start();

// Creates the textures the first time the bytes are finished, and does nothing after that. False
// while the worker is still running, or if the device refused a volume texture, which it logs.
bool Ensure(IDirect3DDevice9* device);

// The low frequencies a cloud's body is carved from, the high ones its edges are eroded by, and
// the map of where over the world clouds are at all.
IDirect3DVolumeTexture9* Shape();
IDirect3DVolumeTexture9* Detail();
IDirect3DTexture9* Weather();

// A number from 0 to 1 that depends only on its arguments, from the hash the noise is made with.
float Random(int x, int y, int z, uint32_t seed);

void ReleaseDeviceObjects();

}
