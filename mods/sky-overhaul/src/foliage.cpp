#include "foliage.h"

#include "engine/clock.h"
#include "engine/dome_draw.h"
#include "engine/shader.h"
#include "fcse_api.h"
#include "tuning.h"

#include "grass_cascaded_vs.h"
#include "grass_vs.h"
#include "leaves_cascaded_copies_vs.h"
#include "leaves_cascaded_vs.h"
#include "leaves_shadowed_copies_vs.h"
#include "leaves_shadowed_vs.h"
#include "leaves_unshadowed_copies_vs.h"
#include "leaves_unshadowed_vs.h"

#include <algorithm>
#include <iterator>

namespace {
    using SkyOverhaul::KnownShaders::VertexKind;

    // One of the engine's lit foliage shaders, and ours in its place.
    struct Replaced {
        VertexKind kind;
        bool leaves;
        SkyOverhaul::VertexShader ours;
    };

    Replaced g_replaced[] = {
        {VertexKind::LitGrass, false, {"grass", g_grassVertexShader}},
        {VertexKind::LitGrassCascaded, false, {"grass cascaded", g_grassCascadedVertexShader}},
        {VertexKind::ShadowedLeaves, true, {"leaves shadowed", g_leavesShadowedVertexShader}},
        {VertexKind::ShadowedLeafCopies, true,
         {"leaves shadowed copies", g_leavesShadowedCopiesVertexShader}},
        {VertexKind::CascadedLeaves, true, {"leaves cascaded", g_leavesCascadedVertexShader}},
        {VertexKind::CascadedLeafCopies, true,
         {"leaves cascaded copies", g_leavesCascadedCopiesVertexShader}},
        {VertexKind::UnshadowedLeaves, true,
         {"leaves unshadowed", g_leavesUnshadowedVertexShader}},
        {VertexKind::UnshadowedLeafCopies, true,
         {"leaves unshadowed copies", g_leavesUnshadowedCopiesVertexShader}},
    };

    bool g_grass = false;
    bool g_leaves = false;
    uint32_t g_litGrass = 0;
    uint32_t g_litLeaves = 0;

    SkyOverhaul::Stopwatch g_clock;
    SkyOverhaul::Heartbeat g_heartbeat{10.0f};

    // Grass and GrassSheen in grass.fx, or Leaves and LeavesGlow in leaves.fx.
    IDirect3DVertexShader9* Light(
        IDirect3DDevice9* device, VertexKind kind,
        float parameters[SkyOverhaul::DomeDraw::kFoliageParameterRegisters * 4]) {
        const auto found =
            std::find_if(std::begin(g_replaced), std::end(g_replaced),
                         [kind](const Replaced& replaced) { return replaced.kind == kind; });
        if (found == std::end(g_replaced) || !(found->leaves ? g_leaves : g_grass)) {
            return nullptr;
        }
        IDirect3DVertexShader9* shader = found->ours.Get(device);
        if (shader == nullptr) {
            return nullptr;
        }
        const SkyOverhaul::Tuning::Values v = SkyOverhaul::Tuning::Current();
        const float grass[] = {v.grassRootShade, v.grassSideLight, v.grassSheen, v.grassGlow,
                               v.grassSheenNarrowness};
        const float leaves[] = {v.leafCrownShade, v.leafCrownThickness, v.leafTilt, v.leafGlint,
                                v.leafGlow};
        std::copy_n(found->leaves ? leaves : grass, std::size(grass), parameters);
        (found->leaves ? g_litLeaves : g_litGrass)++;
        return shader;
    }

    void Install() {
        SkyOverhaul::DomeDraw::SetFoliage(g_grass || g_leaves ? &Light : nullptr);
    }
}

void SkyOverhaul::Foliage::SetGrass(bool enabled) {
    g_grass = enabled;
    Install();
}

void SkyOverhaul::Foliage::SetLeaves(bool enabled) {
    g_leaves = enabled;
    Install();
}

void SkyOverhaul::Foliage::OnFinalPass(const Frame::Pass& pass) {
    if (!(g_grass || g_leaves) || !pass.live || !g_heartbeat.Due(g_clock.Lap())) {
        return;
    }
    FCSE::Logf("foliage: %u grass and %u leaf draws lit by ours", g_litGrass, g_litLeaves);
    g_litGrass = 0;
    g_litLeaves = 0;
}

void SkyOverhaul::Foliage::ReleaseDeviceObjects() {
    for (Replaced& replaced : g_replaced) {
        replaced.ours.Release();
    }
}
