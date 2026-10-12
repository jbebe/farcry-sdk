#include "grass_light.h"

#include "engine/com.h"
#include "engine/grass_draw.h"
#include "engine/handoff.h"
#include "engine/headlights.h"
#include "engine/shader.h"
#include "fcse_api.h"

#include "grass_light_ps.h"
#include "grass_light_vs.h"

#include <bit>
#include <cmath>
#include <iterator>
#include <numbers>

namespace {
    using VehicleOverhaul::Headlights::Light;

    // HeadlightPosition through HeadlightCone in grass_light.fx, above every register the engine's
    // grass and Sky Overhaul's read.
    constexpr UINT kFirstRegister = 112;
    constexpr UINT kRegisters = 4;

    // A hair towards the eye, so the pass lands on the engine's draw however the two shaders round
    // its depth; at the headlight's range that is millimetres.
    constexpr float kDepthBias = -2.0e-6f;

    struct State {
        D3DRENDERSTATETYPE type;
        DWORD value;
    };

    const State kStates[] = {
        {D3DRS_ALPHABLENDENABLE, TRUE},
        {D3DRS_SEPARATEALPHABLENDENABLE, FALSE},
        {D3DRS_SRCBLEND, D3DBLEND_ONE},
        {D3DRS_DESTBLEND, D3DBLEND_ONE},
        {D3DRS_BLENDOP, D3DBLENDOP_ADD},
        {D3DRS_ALPHATESTENABLE, FALSE},
        {D3DRS_ZWRITEENABLE, FALSE},
        {D3DRS_ZFUNC, D3DCMP_LESSEQUAL},
        {D3DRS_DEPTHBIAS, std::bit_cast<DWORD>(kDepthBias)},
        {D3DRS_SLOPESCALEDEPTHBIAS, 0},
    };

    VehicleOverhaul::VertexShader g_vertex{"grass light", g_grassLightVertexShader};
    VehicleOverhaul::PixelShader g_pixel{"grass light", g_grassLightPixelShader};
    bool g_announced = false;

    const Light& Current() {
        using namespace VehicleOverhaul;
        return Headlights::Of(Handoff::Drawing()).light;
    }

    bool Wanted() { return Current().on; }

    float HalfAngleCosine(float degrees) {
        return std::cos(degrees * std::numbers::pi_v<float> / 360.0f);
    }

    void Draw(IDirect3DDevice9* device, VehicleOverhaul::GrassDraw::RedrawFn redraw) {
        const Light& light = Current();
        IDirect3DVertexShader9* vertex = g_vertex.Get(device);
        IDirect3DPixelShader9* pixel = g_pixel.Get(device);
        const float* d = light.direction;
        const float length = std::sqrt(d[0] * d[0] + d[1] * d[1] + d[2] * d[2]);
        if (vertex == nullptr || pixel == nullptr || length < 0.001f) {
            return;
        }
        const float constants[kRegisters * 4] = {
            light.position[0], light.position[1], light.position[2], light.range,
            d[0] / length,     d[1] / length,     d[2] / length,     0.0f,
            light.colour[0],   light.colour[1],   light.colour[2],   0.0f,
            HalfAngleCosine(light.innerAngle), HalfAngleCosine(light.outerAngle), 0.0f, 0.0f,
        };

        IDirect3DVertexShader9* engineVertex = nullptr;
        IDirect3DPixelShader9* enginePixel = nullptr;
        float engineConstants[kRegisters * 4] = {};
        DWORD engineStates[std::size(kStates)] = {};
        device->GetVertexShader(&engineVertex);
        device->GetPixelShader(&enginePixel);
        device->GetVertexShaderConstantF(kFirstRegister, engineConstants, kRegisters);
        for (size_t i = 0; i < std::size(kStates); i++) {
            device->GetRenderState(kStates[i].type, &engineStates[i]);
            device->SetRenderState(kStates[i].type, kStates[i].value);
        }
        device->SetVertexShader(vertex);
        device->SetPixelShader(pixel);
        device->SetVertexShaderConstantF(kFirstRegister, constants, kRegisters);

        redraw(device);

        device->SetVertexShaderConstantF(kFirstRegister, engineConstants, kRegisters);
        device->SetVertexShader(engineVertex);
        device->SetPixelShader(enginePixel);
        for (size_t i = 0; i < std::size(kStates); i++) {
            device->SetRenderState(kStates[i].type, engineStates[i]);
        }
        VehicleOverhaul::Release(engineVertex);
        VehicleOverhaul::Release(enginePixel);

        if (!g_announced) {
            g_announced = true;
            FCSE::ApiPointer()->Log("grass light: the headlight is lighting grass");
        }
    }
}

namespace VehicleOverhaul::GrassLight {

bool Install() {
    return GrassDraw::Install(&Wanted, &Draw);
}

void ReleaseDeviceObjects() {
    g_vertex.Release();
    g_pixel.Release();
    GrassDraw::Forget();
}

}
