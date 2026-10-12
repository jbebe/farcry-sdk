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

    // The bytecode with two terms changed, through the shader's own c0 of (0.5, _, _, -1), or empty
    // when the shader is not that shape:
    // - its one saturated dot of the ground's normal with the negated light vector, wrapped as the
    //   dot halved plus a half;
    // - the fog amount it writes to o1.w, by which the light is scaled, turned into what the fog
    //   leaves, one minus it, so grass short of the fog is lit at all.
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
        out.reserve(tokens.size() + 6);
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
        const std::vector<DWORD> rewritten = Rewritten(tokens);
        IDirect3DVertexShader9* ours = nullptr;
        if (rewritten.empty() || FAILED(device->CreateVertexShader(rewritten.data(), &ours))) {
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
