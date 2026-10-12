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
    constexpr DWORD kDp3 = 0x08;
    constexpr DWORD kDef = 0x51;
    constexpr DWORD kParameter = 0x80000000;
    constexpr DWORD kSaturate = 1u << 20;
    constexpr DWORD kNegate = 1u << 24;
    // A parameter's register number and type.
    constexpr DWORD kRegisterBits = 0x70001FFF;
    constexpr DWORD kConstantRegister = 2u << 28;

    DWORD Opcode(DWORD token) { return token & 0xFFFF; }
    size_t Length(DWORD token) { return (token >> 24) & 0xF; }

    // c0, written as a parameter token.
    bool IsC0(DWORD parameter) { return (parameter & kRegisterBits) == kConstantRegister; }

    // The bytecode with its one saturated dot of a normal and the negated light vector rewritten as
    // the dot halved plus a half, through the shader's own c0.x; empty when the shader is not that
    // shape.
    std::vector<DWORD> Wrapped(const std::vector<DWORD>& tokens) {
        size_t diffuse = 0;
        int found = 0;
        bool halfInC0 = false;
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
            if (Opcode(token) == kDef && IsC0(tokens[i + 1])) {
                halfInC0 = std::bit_cast<float>(tokens[i + 2]) == 0.5f;
            }
            if (Opcode(token) == kDp3 && length == 3 && (tokens[i + 1] & kSaturate) != 0 &&
                (tokens[i + 3] & kNegate) != 0) {
                diffuse = i;
                found++;
            }
            i += 1 + length;
        }
        const DWORD destination = diffuse != 0 ? tokens[diffuse + 1] : 0;
        const DWORD mask = (destination >> 16) & 0xF;
        if (found != 1 || !halfInC0 || std::popcount(mask) != 1) {
            return {};
        }

        const DWORD component = std::countr_zero(mask);
        const DWORD replicated = (component * 0x55) << 16;
        const DWORD half = kParameter | kConstantRegister;
        const DWORD wrap[] = {kMad | (4u << 24), destination,
                              kParameter | (destination & kRegisterBits) | replicated, half, half};

        std::vector<DWORD> out(tokens.begin(), tokens.begin() + diffuse + 4);
        out[diffuse + 1] &= ~kSaturate;
        out.insert(out.end(), std::begin(wrap), std::end(wrap));
        out.insert(out.end(), tokens.begin() + diffuse + 4, tokens.end());
        return out;
    }

    // Ours per engine shader, null where the engine's could not be rewritten. Render thread only.
    std::unordered_map<const void*, IDirect3DVertexShader9*> g_wrapped;

    IDirect3DVertexShader9* Substitute(IDirect3DDevice9* device, IDirect3DVertexShader9* engine) {
        const auto found = g_wrapped.find(engine);
        if (found != g_wrapped.end()) {
            return found->second;
        }

        UINT size = 0;
        std::vector<DWORD> tokens;
        if (SUCCEEDED(engine->GetFunction(nullptr, &size)) && size != 0) {
            tokens.resize(size / sizeof(DWORD));
            engine->GetFunction(tokens.data(), &size);
        }
        const std::vector<DWORD> wrapped = Wrapped(tokens);
        IDirect3DVertexShader9* ours = nullptr;
        if (wrapped.empty() || FAILED(device->CreateVertexShader(wrapped.data(), &ours))) {
            FCSE::ApiPointer()->Log("grass light: a spot light's pass over grass could not be rewritten; "
                                    "it keeps the engine's light");
            ours = nullptr;
        }
        g_wrapped.emplace(engine, ours);
        return ours;
    }
}

namespace VehicleOverhaul::GrassLight {

bool Install() {
    return GrassDraw::Install(&Substitute);
}

void ReleaseDeviceObjects() {
    for (auto& [engine, ours] : g_wrapped) {
        Release(ours);
    }
    g_wrapped.clear();
    GrassDraw::Forget();
}

}
