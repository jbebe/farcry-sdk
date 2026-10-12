#include "engine/handoff.h"

#include "fcse_api.h"

#include <atomic>

namespace {
    using ThisFn = void(__fastcall*)(void* self);

    // SceneRendererFacade's RunFrame, which waits for the frame in flight and posts the next, and
    // RenderLoop, which the render thread runs each posted frame from.
    FCSE::Relocation<ThisFn> g_runFrame{FCSE::Retail(0x00330960)};
    FCSE::Relocation<ThisFn> g_renderLoop{FCSE::Retail(0x003307C0)};

    ThisFn g_originalRunFrame = nullptr;
    ThisFn g_originalRenderLoop = nullptr;
    VehicleOverhaul::Handoff::PostFn g_onPost = nullptr;

    std::atomic<uint32_t> g_posted{0};
    uint32_t g_drawing = 0;

    void __fastcall RunFrameDetour(void* self) {
        const uint32_t frame = g_posted.load() + 1;
        g_onPost(frame);
        g_posted.store(frame);
        g_originalRunFrame(self);
    }

    void __fastcall RenderLoopDetour(void* self) {
        g_drawing = g_posted.load();
        g_originalRenderLoop(self);
    }
}

bool VehicleOverhaul::Handoff::Install(PostFn onPost) {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();
    if (!g_runFrame || !g_renderLoop) {
        api->Log("handoff: RunFrame or RenderLoop was not found in this build");
        return false;
    }
    g_onPost = onPost;
    return api->Hook(reinterpret_cast<void*>(g_runFrame.address()),
                     reinterpret_cast<void*>(&RunFrameDetour),
                     reinterpret_cast<void**>(&g_originalRunFrame)) &&
           api->Hook(reinterpret_cast<void*>(g_renderLoop.address()),
                     reinterpret_cast<void*>(&RenderLoopDetour),
                     reinterpret_cast<void**>(&g_originalRenderLoop));
}

uint32_t VehicleOverhaul::Handoff::Drawing() {
    return g_drawing;
}
