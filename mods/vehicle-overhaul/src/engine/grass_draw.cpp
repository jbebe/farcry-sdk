#include "engine/grass_draw.h"

#include "engine/com.h"
#include "engine/vtable.h"
#include "fcse_api.h"

#include <windows.h>

#include <algorithm>
#include <cstdio>
#include <iterator>
#include <string>
#include <unordered_map>
#include <unordered_set>
#include <vector>

extern "C" IMAGE_DOS_HEADER __ImageBase;

namespace {
    constexpr size_t kDrawIndexedPrimitiveSlot = 82;

    // The lit grass vertex shaders whose placement grass_light.fx repeats: one shadow map slice,
    // and cascades.
    constexpr uint32_t kFollowedVertex[] = {0x736F4429, 0x50B69F13};
    // Their pixel shaders, both reading the grass's texture at s1.
    constexpr uint32_t kFollowedPixel[] = {0xD26F57AE, 0xFA083E8F};
    // Every pixel shader the engine draws grass with in the lit scene, the unshadowed and the
    // additive light pass's among them.
    constexpr uint32_t kGrassPixel[] = {0xD26F57AE, 0xFA083E8F, 0xABB7093C, 0xB65F81B2, 0x5D532300};

    using DrawIndexedPrimitiveFn = HRESULT(__stdcall*)(IDirect3DDevice9*, D3DPRIMITIVETYPE, INT, UINT,
                                                       UINT, UINT, UINT);

    DrawIndexedPrimitiveFn g_original = nullptr;
    VehicleOverhaul::GrassDraw::WantedFn g_wanted = nullptr;
    VehicleOverhaul::GrassDraw::AfterFn g_after = nullptr;

    // Render thread only, as every draw is.
    std::unordered_map<const void*, uint32_t> g_crcs;
    std::unordered_set<uint32_t> g_written;

    struct Draw {
        D3DPRIMITIVETYPE type;
        INT baseVertexIndex;
        UINT minVertexIndex;
        UINT vertices;
        UINT startIndex;
        UINT primitives;
    };

    Draw g_draw = {};

    template <class Interface>
    std::vector<BYTE> Bytecode(Interface* shader) {
        UINT size = 0;
        if (shader == nullptr || FAILED(shader->GetFunction(nullptr, &size)) || size == 0) {
            return {};
        }
        std::vector<BYTE> bytes(size);
        if (FAILED(shader->GetFunction(bytes.data(), &size))) {
            return {};
        }
        return bytes;
    }

    // Remembered per shader, so only the first draw through each one pays for reading its bytecode.
    template <class Interface>
    uint32_t Crc(Interface* shader) {
        if (shader == nullptr) {
            return 0;
        }
        const auto found = g_crcs.find(shader);
        if (found != g_crcs.end()) {
            return found->second;
        }
        const std::vector<BYTE> bytes = Bytecode(shader);
        const uint32_t crc = bytes.empty() ? 0 : FCSE::Crc32(bytes.data(), bytes.size());
        g_crcs.emplace(shader, crc);
        return crc;
    }

    bool Contains(const uint32_t* begin, const uint32_t* end, uint32_t crc) {
        return std::find(begin, end, crc) != end;
    }

    std::wstring ShaderFolder() {
        wchar_t module[MAX_PATH] = {};
        GetModuleFileNameW(reinterpret_cast<HMODULE>(&__ImageBase), module, MAX_PATH);
        std::wstring folder = module;
        folder.resize(folder.find_last_of(L"\\/") + 1);
        return folder + L"VehicleOverhaul-shaders";
    }

    void Write(IDirect3DVertexShader9* vertex, uint32_t vertexCrc, uint32_t pixelCrc) {
        if (!g_written.insert(vertexCrc).second) {
            return;
        }
        const std::vector<BYTE> bytes = Bytecode(vertex);
        const std::wstring folder = ShaderFolder();
        CreateDirectoryW(folder.c_str(), nullptr);
        wchar_t name[32];
        swprintf_s(name, L"\\%08X.vs.bin", vertexCrc);
        FILE* file = nullptr;
        if (!bytes.empty() && _wfopen_s(&file, (folder + name).c_str(), L"wb") == 0 && file != nullptr) {
            fwrite(bytes.data(), 1, bytes.size(), file);
            fclose(file);
        }
        FCSE::Logf("grass draw: grass through vertex shader %08X and pixel shader %08X is not lit by the "
                   "headlight; its vertex shader is in VehicleOverhaul-shaders beside the plugin",
                   vertexCrc, pixelCrc);
    }

    HRESULT Redraw(IDirect3DDevice9* device) {
        return g_original(device, g_draw.type, g_draw.baseVertexIndex, g_draw.minVertexIndex, g_draw.vertices,
                          g_draw.startIndex, g_draw.primitives);
    }

    HRESULT __stdcall DrawIndexedPrimitiveDetour(IDirect3DDevice9* device, D3DPRIMITIVETYPE type,
                                                 INT baseVertexIndex, UINT minVertexIndex, UINT vertices,
                                                 UINT startIndex, UINT primitives) {
        const HRESULT drawn =
            g_original(device, type, baseVertexIndex, minVertexIndex, vertices, startIndex, primitives);
        if (FAILED(drawn) || !g_wanted()) {
            return drawn;
        }

        IDirect3DVertexShader9* vertex = nullptr;
        IDirect3DPixelShader9* pixel = nullptr;
        device->GetVertexShader(&vertex);
        device->GetPixelShader(&pixel);
        const uint32_t vertexCrc = Crc(vertex);
        const uint32_t pixelCrc = Crc(pixel);

        const bool followed = Contains(std::begin(kFollowedVertex), std::end(kFollowedVertex), vertexCrc) &&
                              Contains(std::begin(kFollowedPixel), std::end(kFollowedPixel), pixelCrc);
        if (followed) {
            g_draw = {type, baseVertexIndex, minVertexIndex, vertices, startIndex, primitives};
            g_after(device, &Redraw);
        } else if (Contains(std::begin(kGrassPixel), std::end(kGrassPixel), pixelCrc)) {
            Write(vertex, vertexCrc, pixelCrc);
        }
        VehicleOverhaul::Release(vertex);
        VehicleOverhaul::Release(pixel);
        return drawn;
    }
}

bool VehicleOverhaul::GrassDraw::Install(WantedFn wanted, AfterFn after) {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();
    void* draw = Vtable::Slot(kDrawIndexedPrimitiveSlot);
    if (draw == nullptr) {
        api->Log("grass draw: no Direct3D 9 device could be created to read the vtable from");
        return false;
    }
    g_wanted = wanted;
    g_after = after;
    return api->Hook(draw, reinterpret_cast<void*>(&DrawIndexedPrimitiveDetour),
                     reinterpret_cast<void**>(&g_original));
}

void VehicleOverhaul::GrassDraw::Forget() {
    g_crcs.clear();
}
