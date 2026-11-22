// The weather's cells drawn over the map for the cloud layer: where they fill the layer, how tall and
// how dark they stand, and where their anvils spread the high sheet. clouds.fx's CELL_MAP_* defines
// read it.
#pragma once

#include <d3d9.h>

namespace SkyOverhaul::CellMap {

// The map, redrawn from the newest cells about once a second. Null if the device refused it.
IDirect3DTexture9* Ensure(IDirect3DDevice9* device);

void ReleaseDeviceObjects();

}
