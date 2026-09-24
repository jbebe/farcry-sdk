#include "grass.h"

#include "engine/clock.h"
#include "engine/dome_draw.h"
#include "engine/shader.h"
#include "fcse_api.h"
#include "tuning.h"

#include "grass_cascaded_vs.h"
#include "grass_vs.h"

namespace {
    SkyOverhaul::VertexShader g_shader{"grass", g_grassVertexShader};
    SkyOverhaul::VertexShader g_cascadedShader{"grass cascaded", g_grassCascadedVertexShader};

    bool g_enabled = false;
    uint32_t g_lit = 0;

    SkyOverhaul::Stopwatch g_clock;
    SkyOverhaul::Heartbeat g_heartbeat{10.0f};

    // Grass and GrassSheen in grass.fx.
    IDirect3DVertexShader9* Light(
        IDirect3DDevice9* device, SkyOverhaul::KnownShaders::VertexKind kind,
        float parameters[SkyOverhaul::DomeDraw::kFoliageParameterRegisters * 4]) {
        const bool cascaded = kind == SkyOverhaul::KnownShaders::VertexKind::LitGrassCascaded;
        IDirect3DVertexShader9* shader = (cascaded ? g_cascadedShader : g_shader).Get(device);
        if (shader == nullptr) {
            return nullptr;
        }
        const SkyOverhaul::Tuning::Values v = SkyOverhaul::Tuning::Current();
        parameters[0] = v.grassRootShade;
        parameters[1] = v.grassSideLight;
        parameters[2] = v.grassSheen;
        parameters[3] = v.grassGlow;
        parameters[4] = v.grassSheenNarrowness;
        g_lit++;
        return shader;
    }
}

void SkyOverhaul::Grass::SetEnabled(bool enabled) {
    g_enabled = enabled;
    DomeDraw::SetGrass(enabled ? &Light : nullptr);
}

void SkyOverhaul::Grass::OnFinalPass(const Frame::Pass& pass) {
    if (!g_enabled || !pass.live || !g_heartbeat.Due(g_clock.Lap())) {
        return;
    }
    FCSE::Logf("grass: %u draws lit by ours", g_lit);
    g_lit = 0;
}

void SkyOverhaul::Grass::ReleaseDeviceObjects() {
    g_shader.Release();
    g_cascadedShader.Release();
}
