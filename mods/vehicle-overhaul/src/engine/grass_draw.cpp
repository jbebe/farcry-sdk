#include "engine/grass_draw.h"

#include "engine/com.h"
#include "engine/vtable.h"
#include "fcse_api.h"

#include <algorithm>
#include <iterator>
#include <unordered_map>
#include <vector>

namespace {
    constexpr size_t kDrawIndexedPrimitiveSlot = 82;

    // The spot light's pass over upright clumps, and over bent ones.
    constexpr uint32_t kSpotOnGrass[] = {0xD6110861, 0xDC1C293C};

    using DrawIndexedPrimitiveFn = HRESULT(__stdcall*)(IDirect3DDevice9*, D3DPRIMITIVETYPE, INT, UINT,
                                                       UINT, UINT, UINT);

    DrawIndexedPrimitiveFn g_original = nullptr;
    VehicleOverhaul::GrassDraw::SubstituteFn g_substitute = nullptr;

    // Whether each vertex shader seen is a spot light's grass pass, remembered so only the first
    // draw through each one pays for reading its bytecode. Render thread only, as every draw is.
    std::unordered_map<const void*, bool> g_known;

    bool IsSpotOnGrass(IDirect3DVertexShader9* shader) {
        const auto found = g_known.find(shader);
        if (found != g_known.end()) {
            return found->second;
        }
        UINT size = 0;
        std::vector<BYTE> bytes;
        if (SUCCEEDED(shader->GetFunction(nullptr, &size)) && size != 0) {
            bytes.resize(size);
            shader->GetFunction(bytes.data(), &size);
        }
        const uint32_t crc = bytes.empty() ? 0 : FCSE::Crc32(bytes.data(), bytes.size());
        const bool spot = std::find(std::begin(kSpotOnGrass), std::end(kSpotOnGrass), crc) != std::end(kSpotOnGrass);
        g_known.emplace(shader, spot);
        return spot;
    }

    HRESULT __stdcall DrawIndexedPrimitiveDetour(IDirect3DDevice9* device, D3DPRIMITIVETYPE type,
                                                 INT baseVertexIndex, UINT minVertexIndex, UINT vertices,
                                                 UINT startIndex, UINT primitives) {
        IDirect3DVertexShader9* engine = nullptr;
        device->GetVertexShader(&engine);
        IDirect3DVertexShader9* ours =
            engine != nullptr && IsSpotOnGrass(engine) ? g_substitute(device, engine) : nullptr;
        if (ours != nullptr) {
            device->SetVertexShader(ours);
        }
        const HRESULT drawn =
            g_original(device, type, baseVertexIndex, minVertexIndex, vertices, startIndex, primitives);
        if (ours != nullptr) {
            device->SetVertexShader(engine);
        }
        VehicleOverhaul::Release(engine);
        return drawn;
    }
}

bool VehicleOverhaul::GrassDraw::Install(SubstituteFn substitute) {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();
    void* draw = Vtable::Slot(kDrawIndexedPrimitiveSlot);
    if (draw == nullptr) {
        api->Log("grass draw: no Direct3D 9 device could be created to read the vtable from");
        return false;
    }
    g_substitute = substitute;
    return api->Hook(draw, reinterpret_cast<void*>(&DrawIndexedPrimitiveDetour),
                     reinterpret_cast<void**>(&g_original));
}

void VehicleOverhaul::GrassDraw::Forget() {
    g_known.clear();
}
