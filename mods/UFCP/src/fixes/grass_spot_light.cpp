// Grass stays dark under a spot light - a car's headlights, a flashlight - however close it is.
//
// The engine does light grass under a spot light: a second, additive pass over each clump the light
// reaches. shadersobj ships 80 vertex shaders for it, for each way a clump is drawn: upright or bent by
// a vehicle or a character, receiving the light's shadow or not, at each graphics setting. The pixel
// shaders draw the grass texture times the vertex's colour times its alpha, and the shadow where there
// is one. Two instructions in every one of those vertex shaders leave the grass black:
//
// - The light is scaled by the fog. The shader writes the fog's amount to the colour's w, the same sum
//   the lit grass pass takes one minus of, where an additive light wants what the fog leaves. Grass
//   short of the fog gets none of the light; only grass already lost in fog does.
// - A clump is lit only as far as the ground under it faces the light, a saturated dot of the
//   ground's normal with the light vector. A light a metre up meets the ground almost edge on: a
//   tenth of its strength ten metres out.
//
// Each is created with those two instructions changed and nothing else, so every clump lands exactly
// where the engine's own pass puts it. `mul o.w, a, b` becomes `mad o.w, -a, b, 1`, and the dot loses
// its saturate and gains `mad_sat r, r, 0.5, 0.5`, which wraps the light round the blades. Both
// constants are a `def` of ours. The passes are known by what they read: only grass turns by the
// billboard at c32 to c34, of grass only a spot light's passes have a plain dot with a constant, the
// light's direction for its cone, and of those only the ones that draw the clump first read the fog's
// colour at c48. An omni light's pass has both bugs too and stays as it shipped: the only one measured
// is drawn for a few seconds after a load at the player, reaching tens of metres or more, and what it
// would do to the grass unscaled has not been seen.
#include "fcse_api.h"

#include <d3d9.h>
#include <windows.h>

#include <bit>
#include <iterator>
#include <vector>

namespace {
    constexpr size_t kCreateVertexShaderSlot = 91;

    // Shader tokens as d3d9types.h lays them out.
    constexpr DWORD kVsVersion3 = 0xFFFE0300;
    constexpr DWORD kEnd = 0x0000FFFF;
    constexpr DWORD kComment = 0xFFFE;
    constexpr DWORD kMad = 0x04;
    constexpr DWORD kMul = 0x05;
    constexpr DWORD kDp3 = 0x08;
    constexpr DWORD kDef = 0x51;
    constexpr DWORD kParameter = 0x80000000;
    constexpr DWORD kSaturate = 1u << 20;
    constexpr DWORD kNegate = 1u << 24;
    // A parameter's register type and number, and the two types this reads by.
    constexpr DWORD kTypeBits = 0x70001800;
    constexpr DWORD kNumberBits = 0x000007FF;
    constexpr DWORD kConstant = 2u << 28;
    constexpr DWORD kOutput = 6u << 28;
    constexpr DWORD kWriteAll = 0xFu << 16;
    constexpr DWORD kWriteW = 8u << 16;
    constexpr DWORD kSwizzleXXXX = 0x00 << 16;
    constexpr DWORD kSwizzleYYYY = 0x55 << 16;

    // The billboard's first register, the fog's colour, and a constant no shipped shader reaches.
    constexpr DWORD kBillboard = 32;
    constexpr DWORD kFogColour = kConstant | 48;
    constexpr DWORD kOurConstant = kConstant | 255;

    DWORD Opcode(DWORD token) { return token & 0xFFFF; }
    size_t Length(DWORD token) { return (token >> 24) & 0xF; }
    DWORD Register(DWORD parameter) { return parameter & (kTypeBits | kNumberBits); }
    DWORD Mask(DWORD destination) { return (destination >> 16) & 0xF; }

    // What a shader's instructions say it is, how long it is, and where the two to change sit.
    struct Shape {
        size_t size = 0;
        size_t diffuse = 0;
        size_t fog = 0;
        int diffuses = 0;
        int fogs = 0;
        unsigned billboard = 0;
        bool cone = false;
        bool fogColour = false;

        bool IsSpotLightOverGrass() const {
            return billboard == 0b111 && cone && !fogColour && diffuses == 1 && fogs == 1;
        }
    };

    Shape ShapeOf(const DWORD* tokens) {
        Shape shape;
        if (tokens == nullptr || tokens[0] != kVsVersion3) {
            return shape;
        }
        size_t i = 1;
        while (tokens[i] != kEnd) {
            const DWORD token = tokens[i];
            if (Opcode(token) == kComment) {
                i += 1 + ((token >> 16) & 0x7FFF);
                continue;
            }
            const size_t length = Length(token);
            if (Opcode(token) != kDef) {
                for (size_t t = i + 2; t <= i + length; t++) {
                    shape.fogColour |= Register(tokens[t]) == kFogColour;
                }
            }
            if (Opcode(token) == kDp3 && length == 3) {
                const bool saturated = (tokens[i + 1] & kSaturate) != 0;
                const DWORD source = tokens[i + 3];
                if (saturated && (source & kNegate) != 0 && std::popcount(Mask(tokens[i + 1])) == 1) {
                    shape.diffuse = i;
                    shape.diffuses++;
                }
                const bool constant = (source & kTypeBits) == kConstant;
                const DWORD number = source & kNumberBits;
                if (constant && number >= kBillboard && number < kBillboard + 3) {
                    shape.billboard |= 1u << (number - kBillboard);
                } else if (constant && !saturated) {
                    shape.cone = true;
                }
            }
            if (Opcode(token) == kMul && length == 3 && (tokens[i + 1] & kTypeBits) == kOutput &&
                (tokens[i + 1] & kWriteAll) == kWriteW) {
                shape.fog = i;
                shape.fogs++;
            }
            i += 1 + length;
        }
        shape.size = i + 1;
        return shape;
    }

    // The bytecode of a spot light's pass with both instructions changed.
    std::vector<DWORD> Rewritten(const DWORD* tokens, const Shape& shape) {
        const DWORD lit = tokens[shape.diffuse + 1];
        const DWORD values[] = {kDef | (5u << 24),          kParameter | kOurConstant | kWriteAll,
                                std::bit_cast<DWORD>(0.5f), std::bit_cast<DWORD>(1.0f),
                                0,                          0};
        const DWORD replicated = (std::countr_zero(Mask(lit)) * 0x55) << 16;
        const DWORD half = kParameter | kOurConstant | kSwizzleXXXX;
        const DWORD one = kParameter | kOurConstant | kSwizzleYYYY;
        const DWORD wrap[] = {kMad | (4u << 24), lit, kParameter | Register(lit) | replicated, half, half};
        const DWORD clear[] = {kMad | (4u << 24), tokens[shape.fog + 1], tokens[shape.fog + 2] | kNegate,
                               tokens[shape.fog + 3], one};

        std::vector<DWORD> out = {tokens[0]};
        out.insert(out.end(), std::begin(values), std::end(values));
        for (size_t t = 1; t < shape.size; t++) {
            if (t == shape.fog) {
                out.insert(out.end(), std::begin(clear), std::end(clear));
                t += 3;
                continue;
            }
            out.push_back(t == shape.diffuse + 1 ? tokens[t] & ~kSaturate : tokens[t]);
            if (t == shape.diffuse + 3) {
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

    using CreateVertexShaderFn = HRESULT(__stdcall*)(IDirect3DDevice9*, const DWORD*, IDirect3DVertexShader9**);
    CreateVertexShaderFn g_original = nullptr;

    HRESULT __stdcall CreateVertexShaderDetour(IDirect3DDevice9* device, const DWORD* engine,
                                               IDirect3DVertexShader9** shader) {
        const Shape shape = ShapeOf(engine);
        if (shape.IsSpotLightOverGrass()) {
            const uint32_t crc = FCSE::Crc32(engine, shape.size * sizeof(DWORD));
            const HRESULT created = g_original(device, Rewritten(engine, shape).data(), shader);
            if (SUCCEEDED(created)) {
                FCSE::Logf("grass under spot lights: the engine's pass %08X rewritten", crc);
                return created;
            }
            FCSE::Logf("grass under spot lights: the device refused the engine's pass %08X rewritten - that grass "
                       "stays dark",
                       crc);
        }
        return g_original(device, engine, shader);
    }
}

void ApplyGrassSpotLightFix() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    void* create = DeviceSlot(kCreateVertexShaderSlot);
    if (create == nullptr) {
        api->Log("grass under spot lights: no Direct3D 9 device could be made to find the shader call - "
                 "grass stays dark");
        return;
    }

    if (api->Hook(create, reinterpret_cast<void*>(&CreateVertexShaderDetour),
                  reinterpret_cast<void**>(&g_original))) {
        api->Log("grass under spot lights: watching for the engine's grass light pass");
    }
}
