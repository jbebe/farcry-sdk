#include "foliage.h"

#include "engine/clock.h"
#include "engine/dome_draw.h"
#include "engine/shader.h"
#include "fcse_api.h"
#include "tuning.h"

#include "grass_cascaded_vs.h"
#include "grass_vs.h"

namespace {
    using SkyOverhaul::KnownShaders::Foliage;

    // Grass and GrassSheen in grass.fx, above every register the engine's lit grass reads.
    constexpr UINT kParameters = 90;
    constexpr UINT kParameterRegisters = 2;

    SkyOverhaul::VertexShader g_grass{"grass", g_grassVertexShader};
    SkyOverhaul::VertexShader g_grassCascaded{"grass cascaded", g_grassCascadedVertexShader};

    bool g_enabled = false;
    uint32_t g_lit = 0;

    SkyOverhaul::Stopwatch g_clock;
    SkyOverhaul::Heartbeat g_heartbeat{10.0f};

    HRESULT Draw(IDirect3DDevice9* device, Foliage kind,
                 const SkyOverhaul::DomeDraw::EngineDraw& draw) {
        if (!g_enabled || (kind != Foliage::LitGrass && kind != Foliage::LitGrassCascaded)) {
            return draw();
        }
        IDirect3DVertexShader9* ours =
            (kind == Foliage::LitGrass ? g_grass : g_grassCascaded).Get(device);
        IDirect3DVertexShader9* engine = nullptr;
        float saved[kParameterRegisters * 4] = {};
        if (ours == nullptr || FAILED(device->GetVertexShader(&engine)) ||
            FAILED(device->GetVertexShaderConstantF(kParameters, saved, kParameterRegisters))) {
            if (engine != nullptr) {
                engine->Release();
            }
            return draw();
        }

        const SkyOverhaul::Tuning::Values v = SkyOverhaul::Tuning::Current();
        const float parameters[kParameterRegisters * 4] = {
            v.grassRootShade, v.grassSideLight, v.grassSheen, v.grassGlow,
            v.grassSheenNarrowness, 0.0f, 0.0f, 0.0f,
        };
        device->SetVertexShader(ours);
        device->SetVertexShaderConstantF(kParameters, parameters, kParameterRegisters);
        const HRESULT drawn = draw();
        device->SetVertexShaderConstantF(kParameters, saved, kParameterRegisters);
        device->SetVertexShader(engine);
        engine->Release();
        g_lit++;
        return drawn;
    }
}

void SkyOverhaul::Foliage::Install() {
    DomeDraw::SetFoliage(&Draw);
}

void SkyOverhaul::Foliage::SetEnabled(bool enabled) {
    g_enabled = enabled;
}

void SkyOverhaul::Foliage::OnFinalPass(const Frame::Pass& pass) {
    if (!g_enabled || !pass.live || !g_heartbeat.Due(g_clock.Lap())) {
        return;
    }
    FCSE::Logf("foliage: %u grass draws lit by ours", g_lit);
    g_lit = 0;
}

void SkyOverhaul::Foliage::ReleaseDeviceObjects() {
    g_grass.Release();
    g_grassCascaded.Release();
}
