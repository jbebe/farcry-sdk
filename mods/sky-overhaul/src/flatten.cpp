#include "flatten.h"

#include "engine/com.h"
#include "engine/dome_draw.h"
#include "engine/shader.h"
#include "engine/vertex_patch.h"
#include "fcse_api.h"
#include "tuning.h"

#include <algorithm>
#include <unordered_map>

namespace {
    // Sky passes between two reports of what has been flattened.
    constexpr uint32_t kReportEvery = 1800;

    // How many shaders that could not be flattened are named in the log, at most.
    constexpr int kNamedLeft = 24;

    bool g_enabled = false;
    SkyOverhaul::VertexPatch::Flatten g_flatten = {};

    // Every engine shader seen, held so its address cannot be reused, and the flattened one drawn in
    // its place, or null where it could not be.
    std::unordered_map<IDirect3DVertexShader9*, IDirect3DVertexShader9*> g_shaders;
    int g_flattened = 0;
    int g_left = 0;
    int g_named = 0;
    int g_reported = -1;
    uint32_t g_passes = 0;

    void Forget() {
        for (auto& [engine, ours] : g_shaders) {
            SkyOverhaul::Release(ours);
            engine->Release();
        }
        g_shaders.clear();
        g_flattened = 0;
        g_left = 0;
        g_reported = -1;
    }

    IDirect3DVertexShader9* Make(IDirect3DDevice9* device, IDirect3DVertexShader9* engine) {
        const std::vector<DWORD> tokens = SkyOverhaul::Bytecode(engine);
        const std::vector<DWORD> flat =
            SkyOverhaul::VertexPatch::Flattened(tokens.data(), tokens.size(), g_flatten);
        IDirect3DVertexShader9* ours = nullptr;
        if (flat.empty() || FAILED(device->CreateVertexShader(flat.data(), &ours))) {
            g_left++;
            if (g_named < kNamedLeft) {
                g_named++;
                FCSE::Logf("flatten: vertex shader %08X is drawn as it is, not projected as the "
                           "world's are%s",
                           FCSE::Crc32(tokens.data(), tokens.size() * sizeof(DWORD)),
                           flat.empty() ? "" : " (the device refused the flattened one)");
            }
            return nullptr;
        }
        g_flattened++;
        return ours;
    }

    // At least one times as far, so the curve never divides by nought.
    SkyOverhaul::VertexPatch::Flatten Current() {
        const SkyOverhaul::Tuning::Values v = SkyOverhaul::Tuning::Current();
        return {v.flattenStart, v.flattenEnd, (std::max)(v.flattenStretch, 1.0f)};
    }

    IDirect3DVertexShader9* Pick(IDirect3DDevice9* device, IDirect3DVertexShader9* bound) {
        const auto found = g_shaders.find(bound);
        if (found != g_shaders.end()) {
            return found->second;
        }
        bound->AddRef();
        return g_shaders[bound] = Make(device, bound);
    }
}

void SkyOverhaul::Flatten::OnScenePass(const Frame::Pass& pass) {
    if (!pass.sky) {
        return;
    }
    // Here rather than when switched off, since only this thread draws.
    if (!g_enabled) {
        if (!g_shaders.empty()) {
            Forget();
        }
        return;
    }
    const VertexPatch::Flatten now = Current();
    if (now.start != g_flatten.start || now.end != g_flatten.end ||
        now.stretch != g_flatten.stretch) {
        Forget();
        g_flatten = now;
    }
    if (++g_passes % kReportEvery == 0 && g_flattened + g_left != g_reported) {
        g_reported = g_flattened + g_left;
        FCSE::Logf("flatten: %d vertex shaders drawn flatter, %d as they are", g_flattened, g_left);
    }
}

void SkyOverhaul::Flatten::ReleaseDeviceObjects() {
    Forget();
}

void SkyOverhaul::Flatten::SetEnabled(bool enabled) {
    if (enabled && !g_enabled) {
        g_flatten = Current();
    }
    g_enabled = enabled;
    DomeDraw::SetVertex(enabled ? &Pick : nullptr);
}
