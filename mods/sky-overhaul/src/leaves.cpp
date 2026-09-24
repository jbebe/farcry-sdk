#include "leaves.h"

#include "engine/clock.h"
#include "engine/dome_draw.h"
#include "engine/shader.h"
#include "fcse_api.h"
#include "tuning.h"

#include "leaves_cascaded_copies_vs.h"
#include "leaves_cascaded_vs.h"
#include "leaves_shadowed_copies_vs.h"
#include "leaves_shadowed_vs.h"
#include "leaves_unshadowed_copies_vs.h"
#include "leaves_unshadowed_vs.h"

namespace {
    using SkyOverhaul::KnownShaders::VertexKind;

    SkyOverhaul::VertexShader g_shadowed{"leaves shadowed", g_leavesShadowedVertexShader};
    SkyOverhaul::VertexShader g_shadowedCopies{"leaves shadowed copies",
                                               g_leavesShadowedCopiesVertexShader};
    SkyOverhaul::VertexShader g_cascaded{"leaves cascaded", g_leavesCascadedVertexShader};
    SkyOverhaul::VertexShader g_cascadedCopies{"leaves cascaded copies",
                                               g_leavesCascadedCopiesVertexShader};
    SkyOverhaul::VertexShader g_unshadowed{"leaves unshadowed", g_leavesUnshadowedVertexShader};
    SkyOverhaul::VertexShader g_unshadowedCopies{"leaves unshadowed copies",
                                                 g_leavesUnshadowedCopiesVertexShader};

    bool g_enabled = false;
    uint32_t g_lit = 0;

    SkyOverhaul::Stopwatch g_clock;
    SkyOverhaul::Heartbeat g_heartbeat{10.0f};

    SkyOverhaul::VertexShader& ShaderFor(VertexKind kind) {
        switch (kind) {
        case VertexKind::ShadowedLeafCopies:
            return g_shadowedCopies;
        case VertexKind::CascadedLeaves:
            return g_cascaded;
        case VertexKind::CascadedLeafCopies:
            return g_cascadedCopies;
        case VertexKind::UnshadowedLeaves:
            return g_unshadowed;
        case VertexKind::UnshadowedLeafCopies:
            return g_unshadowedCopies;
        default:
            return g_shadowed;
        }
    }

    // Leaves and LeavesGlow in leaves.fx.
    IDirect3DVertexShader9* Light(
        IDirect3DDevice9* device, VertexKind kind,
        float parameters[SkyOverhaul::DomeDraw::kFoliageParameterRegisters * 4]) {
        IDirect3DVertexShader9* shader = ShaderFor(kind).Get(device);
        if (shader == nullptr) {
            return nullptr;
        }
        const SkyOverhaul::Tuning::Values v = SkyOverhaul::Tuning::Current();
        parameters[0] = v.leafCrownShade;
        parameters[1] = v.leafCrownThickness;
        parameters[2] = v.leafTilt;
        parameters[3] = v.leafGlint;
        parameters[4] = v.leafGlow;
        g_lit++;
        return shader;
    }
}

void SkyOverhaul::Leaves::SetEnabled(bool enabled) {
    g_enabled = enabled;
    DomeDraw::SetLeaves(enabled ? &Light : nullptr);
}

void SkyOverhaul::Leaves::OnFinalPass(const Frame::Pass& pass) {
    if (!g_enabled || !pass.live || !g_heartbeat.Due(g_clock.Lap())) {
        return;
    }
    FCSE::Logf("leaves: %u draws lit by ours", g_lit);
    g_lit = 0;
}

void SkyOverhaul::Leaves::ReleaseDeviceObjects() {
    g_shadowed.Release();
    g_shadowedCopies.Release();
    g_cascaded.Release();
    g_cascadedCopies.Release();
    g_unshadowed.Release();
    g_unshadowedCopies.Release();
}
