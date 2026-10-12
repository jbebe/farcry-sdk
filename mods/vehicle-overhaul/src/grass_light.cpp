#include "grass_light.h"

#include "engine/com.h"
#include "engine/grass_draw.h"
#include "fcse_api.h"

#include <bit>
#include <iterator>
#include <unordered_map>
#include <vector>

namespace {
    // Shader tokens as d3d9types.h lays them out.
    constexpr DWORD kEnd = 0x0000FFFF;
    constexpr DWORD kComment = 0xFFFE;
    constexpr DWORD kMad = 0x04;
    constexpr DWORD kMul = 0x05;
    constexpr DWORD kDef = 0x51;
    constexpr DWORD kParameter = 0x80000000;
    constexpr DWORD kNegate = 1u << 24;
    // A parameter's register number and type, and the two this reads by: c0, and o1 the output.
    constexpr DWORD kRegisterBits = 0x70001FFF;
    constexpr DWORD kC0 = 2u << 28;
    constexpr DWORD kO1 = (6u << 28) | 1;
    constexpr DWORD kWriteW = 8;
    constexpr DWORD kSwizzleWWWW = 0xFF << 16;

    DWORD Opcode(DWORD token) { return token & 0xFFFF; }
    size_t Length(DWORD token) { return (token >> 24) & 0xF; }
    DWORD Register(DWORD parameter) { return parameter & kRegisterBits; }
    DWORD Mask(DWORD destination) { return (destination >> 16) & 0xF; }

    // The bytecode with the fog amount it writes to o1.w, by which the light is scaled, turned into
    // what the fog leaves, one minus it, through the shader's own c0.w of -1; empty when the shader is
    // not that shape.
    std::vector<DWORD> Rewritten(const std::vector<DWORD>& tokens) {
        size_t fog = 0;
        int fogs = 0;
        bool minusOne = false;
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
                minusOne = std::bit_cast<float>(tokens[i + 5]) == -1.0f;
            }
            if (Opcode(token) == kMul && length == 3 && Register(tokens[i + 1]) == kO1 &&
                Mask(tokens[i + 1]) == kWriteW) {
                fog = i;
                fogs++;
            }
            i += 1 + length;
        }
        if (fogs != 1 || !minusOne) {
            return {};
        }

        const DWORD clear[] = {kMad | (4u << 24), tokens[fog + 1], tokens[fog + 2] | kNegate, tokens[fog + 3],
                               kParameter | kC0 | kSwizzleWWWW | kNegate};
        std::vector<DWORD> out(tokens.begin(), tokens.begin() + fog);
        out.insert(out.end(), std::begin(clear), std::end(clear));
        out.insert(out.end(), tokens.begin() + fog + 4, tokens.end());
        return out;
    }

    // Ours per engine shader, null where the engine's could not be rewritten. Render thread only.
    std::unordered_map<const void*, IDirect3DVertexShader9*> g_rewritten;

    IDirect3DVertexShader9* Substitute(IDirect3DDevice9* device, IDirect3DVertexShader9* engine) {
        const auto found = g_rewritten.find(engine);
        if (found != g_rewritten.end()) {
            return found->second;
        }

        UINT size = 0;
        std::vector<DWORD> tokens;
        if (SUCCEEDED(engine->GetFunction(nullptr, &size)) && size != 0) {
            tokens.resize(size / sizeof(DWORD));
            engine->GetFunction(tokens.data(), &size);
        }
        const std::vector<DWORD> rewritten = Rewritten(tokens);
        IDirect3DVertexShader9* ours = nullptr;
        if (rewritten.empty() || FAILED(device->CreateVertexShader(rewritten.data(), &ours))) {
            FCSE::ApiPointer()->Log("grass light: a spot light's pass over grass could not be rewritten; "
                                    "it keeps the engine's light");
            ours = nullptr;
        }
        g_rewritten.emplace(engine, ours);
        return ours;
    }
}

namespace VehicleOverhaul::GrassLight {

bool Install() {
    return GrassDraw::Install(&Substitute);
}

void ReleaseDeviceObjects() {
    for (auto& [engine, ours] : g_rewritten) {
        Release(ours);
    }
    g_rewritten.clear();
    GrassDraw::Forget();
}

}
