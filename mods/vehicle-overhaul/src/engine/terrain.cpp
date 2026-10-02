// The terrain's height under a point.
#include "engine/terrain.h"

#include "fcse_api.h"

#include <windows.h>

namespace {
    // The editor export over CTerrain::GetZApr, which reads the loaded heightfield.
    using HeightFn = float(__cdecl*)(float x, float y);

    HeightFn g_height = nullptr;
}

namespace VehicleOverhaul::Terrain {

bool Install() {
    g_height = reinterpret_cast<HeightFn>(
        GetProcAddress(static_cast<HMODULE>(FCSE::ApiPointer()->duniaModule), "FCE_TerrainManager_GetHeightAt"));
    if (g_height == nullptr) {
        FCSE::ApiPointer()->Log("terrain: Dunia.dll does not export FCE_TerrainManager_GetHeightAt");
        return false;
    }
    return true;
}

float Height(float x, float y) { return g_height(x, y); }

}
