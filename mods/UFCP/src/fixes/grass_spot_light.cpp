// Grass stays dark under a spot light - a car's headlights, a flashlight - however close it is.
//
// The engine does light grass under a spot light: a second, additive pass over each clump the light
// reaches, through two vertex shaders it builds for itself at runtime, one for upright clumps and one
// for clumps a vehicle or a character is bending (bytecode CRC-32 D6110861 and DC1C293C, measured on
// GOG; the Steam build not yet). Its pixel shader draws the grass texture times the vertex's colour
// times its alpha. Two instructions in those vertex shaders leave the grass black:
//
// - The light is scaled by the fog. The shader writes the fog's amount to o1.w, the same sum the lit
//   grass pass takes one minus of, where an additive light wants what the fog leaves. Grass short of
//   the fog gets none of the light; only grass already lost in fog does.
// - A clump is lit only as far as the ground under it faces the light, a saturated dot of the
//   ground's normal with the light vector. A light a metre up meets the ground almost edge on: a
//   tenth of its strength ten metres out.
//
// Both shaders are drawn through copies with those two instructions changed and nothing else, so
// every clump lands exactly where the engine's own pass puts it. `mul o1.w, a, b` becomes
// `mad o1.w, -a, b, 1`, and the dot loses its saturate and gains `mad_sat r, r, 0.5, 0.5`, which
// wraps the light round the blades. Both constants are the shader's own c0, (0.5, _, _, -1).
#include "fcse_api.h"

#include <d3d9.h>
#include <windows.h>

#include <algorithm>
#include <bit>
#include <iterator>
#include <unordered_map>
#include <unordered_set>
#include <vector>

namespace {
    constexpr size_t kDrawIndexedPrimitiveSlot = 82;

    struct Pass {
        uint32_t crc;
        const char* clumps;
    };

    constexpr Pass kPasses[] = {{0xD6110861, "upright"}, {0xDC1C293C, "bent"}};

    // Shader tokens as d3d9types.h lays them out.
    constexpr DWORD kEnd = 0x0000FFFF;
    constexpr DWORD kComment = 0xFFFE;
    constexpr DWORD kMad = 0x04;
    constexpr DWORD kMul = 0x05;
    constexpr DWORD kDp3 = 0x08;
    constexpr DWORD kDef = 0x51;
    constexpr DWORD kParameter = 0x80000000;
    constexpr DWORD kSaturate = 1u << 20;
    constexpr DWORD kNegate = 1u << 24;
    // A parameter's register number and type, and the two this reads by: c0, and o1 the output.
    constexpr DWORD kRegisterBits = 0x70001FFF;
    constexpr DWORD kC0 = 2u << 28;
    constexpr DWORD kO1 = (6u << 28) | 1;
    constexpr DWORD kWriteW = 8;
    constexpr DWORD kSwizzleXXXX = 0x00 << 16;
    constexpr DWORD kSwizzleWWWW = 0xFF << 16;

    DWORD Opcode(DWORD token) { return token & 0xFFFF; }
    size_t Length(DWORD token) { return (token >> 24) & 0xF; }
    DWORD Register(DWORD parameter) { return parameter & kRegisterBits; }
    DWORD Mask(DWORD destination) { return (destination >> 16) & 0xF; }

    // The bytecode with both instructions changed, or empty when the shader is not that shape.
    std::vector<DWORD> Rewritten(const std::vector<DWORD>& tokens) {
        size_t diffuse = 0;
        size_t fog = 0;
        int diffuses = 0;
        int fogs = 0;
        bool constantsFit = false;
        size_t i = 1;
        while (i < tokens.size() && tokens[i] != kEnd) {
            const DWORD token = tokens[i];
            if (Opcode(token) == kComment) {
                i += 1 + ((token >> 16) & 0x7FFF);
                continue;
            }
            const size_t length = Length(token);
            if (i + length >= tokens.size()) {
                return {};
            }
            if (Opcode(token) == kDef && Register(tokens[i + 1]) == kC0) {
                constantsFit = std::bit_cast<float>(tokens[i + 2]) == 0.5f &&
                               std::bit_cast<float>(tokens[i + 5]) == -1.0f;
            }
            if (Opcode(token) == kDp3 && length == 3 && (tokens[i + 1] & kSaturate) != 0 &&
                (tokens[i + 3] & kNegate) != 0) {
                diffuse = i;
                diffuses++;
            }
            if (Opcode(token) == kMul && length == 3 && Register(tokens[i + 1]) == kO1 &&
                Mask(tokens[i + 1]) == kWriteW) {
                fog = i;
                fogs++;
            }
            i += 1 + length;
        }
        const DWORD lit = diffuses == 1 ? tokens[diffuse + 1] : 0;
        if (diffuses != 1 || fogs != 1 || !constantsFit || std::popcount(Mask(lit)) != 1) {
            return {};
        }

        const DWORD replicated = (std::countr_zero(Mask(lit)) * 0x55) << 16;
        const DWORD half = kParameter | kC0 | kSwizzleXXXX;
        const DWORD wrap[] = {kMad | (4u << 24), lit, kParameter | Register(lit) | replicated, half, half};
        const DWORD clear[] = {kMad | (4u << 24), tokens[fog + 1], tokens[fog + 2] | kNegate, tokens[fog + 3],
                               kParameter | kC0 | kSwizzleWWWW | kNegate};

        std::vector<DWORD> out;
        out.reserve(tokens.size() + std::size(wrap) + 1);
        for (size_t t = 0; t < tokens.size(); t++) {
            if (t == fog) {
                out.insert(out.end(), std::begin(clear), std::end(clear));
                t += 3;
                continue;
            }
            out.push_back(t == diffuse + 1 ? tokens[t] & ~kSaturate : tokens[t]);
            if (t == diffuse + 3) {
                out.insert(out.end(), std::begin(wrap), std::end(wrap));
            }
        }
        return out;
    }

    // Every IDirect3DDevice9 shares one vtable, so a throwaway device of our own names its slots.
    void* DeviceSlot(size_t slot) {
        IDirect3D9* d3d = Direct3DCreate9(D3D_SDK_VERSION);
        if (d3d == nullptr) {
            return nullptr;
        }
        D3DPRESENT_PARAMETERS present = {};
        present.Windowed = TRUE;
        present.SwapEffect = D3DSWAPEFFECT_DISCARD;
        present.hDeviceWindow = GetDesktopWindow();
        IDirect3DDevice9* device = nullptr;
        void* function = nullptr;
        if (SUCCEEDED(d3d->CreateDevice(D3DADAPTER_DEFAULT, D3DDEVTYPE_HAL, present.hDeviceWindow,
                                        D3DCREATE_SOFTWARE_VERTEXPROCESSING, &present, &device))) {
            function = (*reinterpret_cast<void***>(device))[slot];
            device->Release();
        }
        d3d->Release();
        return function;
    }

    using DrawIndexedPrimitiveFn = HRESULT(__stdcall*)(IDirect3DDevice9*, D3DPRIMITIVETYPE, INT, UINT, UINT,
                                                       UINT, UINT);
    DrawIndexedPrimitiveFn g_original = nullptr;

    // Render thread only, as every draw is. The engine's grass passes are held, so their addresses
    // cannot come back as another shader while ours stands in for them; the rest are only remembered.
    IDirect3DDevice9* g_device = nullptr;
    std::unordered_map<IDirect3DVertexShader9*, IDirect3DVertexShader9*> g_ours;
    std::unordered_set<const void*> g_others;

    void Forget() {
        for (auto& [engine, ours] : g_ours) {
            engine->Release();
            if (ours != nullptr) {
                ours->Release();
            }
        }
        g_ours.clear();
        g_others.clear();
    }

    // Ours for a grass pass, or null for every other shader and for a pass that would not rewrite.
    IDirect3DVertexShader9* Ours(IDirect3DDevice9* device, IDirect3DVertexShader9* engine) {
        if (device != g_device) {
            Forget();
            g_device = device;
        }
        if (g_others.contains(engine)) {
            return nullptr;
        }
        const auto known = g_ours.find(engine);
        if (known != g_ours.end()) {
            return known->second;
        }

        UINT size = 0;
        std::vector<DWORD> tokens;
        if (SUCCEEDED(engine->GetFunction(nullptr, &size)) && size != 0) {
            tokens.resize(size / sizeof(DWORD));
            engine->GetFunction(tokens.data(), &size);
        }
        const uint32_t crc = tokens.empty() ? 0 : FCSE::Crc32(tokens.data(), tokens.size() * sizeof(DWORD));
        const Pass* pass = std::find_if(std::begin(kPasses), std::end(kPasses),
                                        [crc](const Pass& p) { return p.crc == crc; });
        if (pass == std::end(kPasses)) {
            g_others.insert(engine);
            return nullptr;
        }

        const std::vector<DWORD> rewritten = Rewritten(tokens);
        IDirect3DVertexShader9* ours = nullptr;
        if (rewritten.empty() || FAILED(device->CreateVertexShader(rewritten.data(), &ours))) {
            FCSE::Logf("grass under spot lights: the engine's pass for %s grass could not be rewritten - "
                       "it stays dark",
                       pass->clumps);
            ours = nullptr;
        } else {
            FCSE::Logf("grass under spot lights: the engine's pass for %s grass rewritten", pass->clumps);
        }
        engine->AddRef();
        g_ours.emplace(engine, ours);
        return ours;
    }

    HRESULT __stdcall DrawIndexedPrimitiveDetour(IDirect3DDevice9* device, D3DPRIMITIVETYPE type,
                                                 INT baseVertexIndex, UINT minVertexIndex, UINT vertices,
                                                 UINT startIndex, UINT primitives) {
        IDirect3DVertexShader9* engine = nullptr;
        device->GetVertexShader(&engine);
        IDirect3DVertexShader9* ours = engine != nullptr ? Ours(device, engine) : nullptr;
        if (ours != nullptr) {
            device->SetVertexShader(ours);
        }
        const HRESULT drawn =
            g_original(device, type, baseVertexIndex, minVertexIndex, vertices, startIndex, primitives);
        if (ours != nullptr) {
            device->SetVertexShader(engine);
        }
        if (engine != nullptr) {
            engine->Release();
        }
        return drawn;
    }
}

void ApplyGrassSpotLightFix() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    void* draw = DeviceSlot(kDrawIndexedPrimitiveSlot);
    if (draw == nullptr) {
        api->Log("grass under spot lights: no Direct3D 9 device could be made to find the draw call - "
                 "grass stays dark");
        return;
    }

    if (api->Hook(draw, reinterpret_cast<void*>(&DrawIndexedPrimitiveDetour),
                  reinterpret_cast<void**>(&g_original))) {
        api->Log("grass under spot lights: watching for the engine's grass light pass");
    }
}
