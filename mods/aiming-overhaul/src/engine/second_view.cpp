#include "engine/second_view.h"

#include "engine/com.h"
#include "engine/frame.h"
#include "engine/render_target.h"
#include "fcse_api.h"

#include <cstddef>
#include <cstdint>

namespace {
    // All __thiscall, which a free function spells __fastcall with an unused EDX.
    using ViewCtorFn = void(__fastcall*)(uint8_t* view, void* unused);
    using CameraCopyFn = uint8_t*(__fastcall*)(uint8_t* to, void* unused, const uint8_t* from);
    using CameraUpdateFn = void(__fastcall*)(uint8_t* camera, void* unused);
    using QueryFn = void(__fastcall*)(uint8_t* renderer, void* unused, uint8_t* view,
                                      uint8_t* viewport, float killScale);
    using RenderFn = void(__fastcall*)(uint8_t* renderer, void* unused, uint8_t* view,
                                       uint32_t entityIndex, uint32_t entitySerial,
                                       uint8_t* viewport, uint8_t* component, float lodScale,
                                       float killScale, float detailScale, float effect,
                                       uint32_t states0, uint32_t states1, uint32_t states2,
                                       uint32_t states3, void* passState);
    using PopFn = void(__fastcall*)(uint8_t* renderer, void* unused);

    // CSceneRenderer::PrepareFrameGraph, from the call that draws the water's reflection to the
    // instruction after it.
    FCSE::Relocation<uint8_t*> g_reflectionDrawn{FCSE::Pattern(
        "52 E8 ?? ?? ?? ?? 8D 8C 24 E0 00 00 00 51 8B 8B 90 03 00 00 E8")};
    constexpr size_t kAfterCall = 6;

    FCSE::Relocation<ViewCtorFn> g_viewCtor{FCSE::Uplay(0x003A2C30)};
    FCSE::Relocation<CameraCopyFn> g_cameraAssign{FCSE::Uplay(0x0033AEA0)};
    FCSE::Relocation<CameraCopyFn> g_cameraCopy{FCSE::Uplay(0x0039FEA0)};
    FCSE::Relocation<CameraUpdateFn> g_cameraUpdate{FCSE::Uplay(0x0040C800)};
    FCSE::Relocation<QueryFn> g_query{FCSE::Uplay(0x003A8450)};
    FCSE::Relocation<RenderFn> g_render{FCSE::Uplay(0x003A3250)};
    FCSE::Relocation<PopFn> g_pop{FCSE::Uplay(0x003A1EF0)};

    // PrepareFrameGraph's frame at the hook: EBX the scene renderer, EBP its arguments, ESP its
    // locals.
    constexpr ptrdiff_t kEntityIndex = 0x08;
    constexpr ptrdiff_t kEntitySerial = 0x0C;
    constexpr ptrdiff_t kViewport = 0x10;
    constexpr ptrdiff_t kKillScale = 0x20;
    constexpr ptrdiff_t kDetailScale = 0x24;
    constexpr ptrdiff_t kCameraComponent = 0x48;
    constexpr ptrdiff_t kLodScale = 0x54;
    constexpr ptrdiff_t kEffect = 0x78;

    // CSceneRenderer: its config, the reflection renderer's holder, and the render-state handles
    // the reflection is drawn with.
    constexpr ptrdiff_t kSceneConfig = 0x2C;
    constexpr ptrdiff_t kReflectionHolder = 0x38;
    constexpr ptrdiff_t kRenderStates = 0x88;

    // The scene config's level-of-detail and kill-distance scales, and the viewport's.
    constexpr ptrdiff_t kConfigLodScale = 0x6B4;
    constexpr ptrdiff_t kConfigKillScale = 0x6B8;
    constexpr ptrdiff_t kViewportLodScale = 0x34;
    constexpr ptrdiff_t kViewportKillScale = 0x38;

    // CWaterReflectionRenderer: its config, the colour it last drew and its target pool scope.
    constexpr ptrdiff_t kReflectionConfig = 0x0C;
    constexpr ptrdiff_t kDrawnColour = 0x20;
    constexpr ptrdiff_t kPoolScope = 0x24;
    // Its config: the reflection's size and multisampling.
    constexpr ptrdiff_t kTargetWidth = 0x598;
    constexpr ptrdiff_t kTargetHeight = 0x59C;
    constexpr ptrdiff_t kTargetSamples = 0x5A4;
    // An engine texture wrapper's Direct3D texture.
    constexpr ptrdiff_t kWrappedTexture = 0x18;

    // CCamera: the field of view in radians, and whether the clip plane is a seventh frustum plane.
    constexpr ptrdiff_t kFieldOfView = 0x228;
    constexpr ptrdiff_t kClipPlaneOn = 0x348;
    // The camera component: its render camera, and the camera it is culled with.
    constexpr ptrdiff_t kRenderCamera = 0x20;
    constexpr ptrdiff_t kCullingCamera = 0x3F0;
    constexpr size_t kComponentSize = 0x830;

    // SWaterReflectionView: its camera, scissor, clip plane, tint, quality and the smallest object
    // radius drawn.
    constexpr size_t kViewSize = 0x870;
    constexpr ptrdiff_t kViewCamera = 0x3D0;
    constexpr ptrdiff_t kViewScissor = 0x7A0;
    constexpr ptrdiff_t kViewClipPlane = 0x830;
    constexpr ptrdiff_t kViewTint = 0x850;
    constexpr ptrdiff_t kViewQuality = 0x868;
    constexpr ptrdiff_t kViewSmallest = 0x86C;

    // The second view's size as a share of the screen's, its detail on the reflection's scale of
    // 2 to 7, and the radius in metres below which an object is left out.
    constexpr UINT kShare = 2;
    constexpr int kQuality = 5;
    constexpr float kSmallestObject = 0.5f;

    AimingOverhaul::SecondView::WantFn g_want = nullptr;
    alignas(16) uint8_t g_view[kViewSize];
    alignas(16) uint8_t g_component[kComponentSize];

    IDirect3DDevice9* g_owner = nullptr;
    AimingOverhaul::Target g_copy;
    D3DSURFACE_DESC g_copyDesc = {};
    uint32_t g_drawnFrame = AimingOverhaul::Frame::kNever;
    // Set once the device refuses the copy's target; cleared on reset.
    bool g_refused = false;
    bool g_reported = false;

    template <typename T>
    T& Field(uint8_t* object, ptrdiff_t offset) {
        return *reinterpret_cast<T*>(object + offset);
    }

    // The reflection renderer's last colour, copied into a texture of our own.
    bool Copy(uint8_t* renderer) {
        uint8_t* wrapper = Field<uint8_t*>(renderer, kDrawnColour);
        IDirect3DTexture9* texture =
            wrapper != nullptr ? Field<IDirect3DTexture9*>(wrapper, kWrappedTexture) : nullptr;
        IDirect3DSurface9* surface = nullptr;
        if (texture == nullptr || FAILED(texture->GetSurfaceLevel(0, &surface))) {
            return false;
        }
        D3DSURFACE_DESC desc = {};
        surface->GetDesc(&desc);
        IDirect3DDevice9* device = nullptr;
        texture->GetDevice(&device);
        if (!g_refused && (g_owner != device || g_copy.texture == nullptr ||
                           g_copyDesc.Width != desc.Width || g_copyDesc.Height != desc.Height ||
                           g_copyDesc.Format != desc.Format)) {
            AimingOverhaul::Release(g_copy);
            g_owner = device;
            g_copyDesc = desc;
            if (FAILED(AimingOverhaul::CreateTarget(device, desc.Width, desc.Height, desc.Format,
                                                    g_copy))) {
                AimingOverhaul::Release(g_copy);
                g_refused = true;
                FCSE::Logf("second view: the device refused a %ux%u target of format %d",
                           desc.Width, desc.Height, desc.Format);
            }
        }
        const bool copied =
            g_copy.surface != nullptr &&
            SUCCEEDED(device->StretchRect(surface, nullptr, g_copy.surface, nullptr, D3DTEXF_NONE));
        AimingOverhaul::Release(device);
        AimingOverhaul::Release(surface);
        return copied;
    }

    // Draws the second view through the reflection renderer, with the player's camera at `fov`,
    // putting back everything the renderer keeps for the water's own reflection.
    void Draw(FCSE_MidHookContext* ctx, uint8_t* component, float fov) {
        uint8_t* scene = reinterpret_cast<uint8_t*>(ctx->ebx);
        uint8_t* frame = reinterpret_cast<uint8_t*>(ctx->ebp);
        uint8_t* stack = reinterpret_cast<uint8_t*>(ctx->esp);
        uint8_t* holder = Field<uint8_t*>(scene, kReflectionHolder);
        uint8_t* renderer = holder != nullptr ? Field<uint8_t*>(holder, kReflectionHolder) : nullptr;
        if (renderer == nullptr) {
            return;
        }
        uint8_t* config = Field<uint8_t*>(renderer, kReflectionConfig);
        uint8_t* sceneConfig = Field<uint8_t*>(scene, kSceneConfig);
        uint8_t* viewport = Field<uint8_t*>(frame, kViewport);
        // The scene's own scales without the zoom, which would otherwise thin the view out.
        const float lodScale = Field<float>(sceneConfig, kConfigLodScale) *
                               Field<float>(viewport, kViewportLodScale);
        const float killScale = Field<float>(sceneConfig, kConfigKillScale) *
                                Field<float>(viewport, kViewportKillScale);
        const int width = static_cast<int>(AimingOverhaul::Frame::Width() / kShare);
        const int height = static_cast<int>(AimingOverhaul::Frame::Height() / kShare);

        g_viewCtor(g_view, nullptr);
        uint8_t* camera = g_view + kViewCamera;
        g_cameraAssign(camera, nullptr, component + kRenderCamera);
        Field<float>(camera, kFieldOfView) = fov;
        Field<uint8_t>(camera, kClipPlaneOn) = 0;
        g_cameraUpdate(camera, nullptr);
        int* scissor = &Field<int>(g_view, kViewScissor);
        scissor[0] = 0;
        scissor[1] = 0;
        scissor[2] = width;
        scissor[3] = height;
        float* plane = &Field<float>(g_view, kViewClipPlane);
        plane[0] = plane[1] = plane[2] = 0.0f;
        plane[3] = 1.0f;
        float* tint = &Field<float>(g_view, kViewTint);
        tint[0] = tint[1] = tint[2] = 1.0f;
        Field<int>(g_view, kViewQuality) = kQuality;
        Field<float>(g_view, kViewSmallest) = kSmallestObject;
        g_cameraCopy(g_component + kRenderCamera, nullptr, camera);
        g_cameraCopy(g_component + kCullingCamera, nullptr, camera);

        const int savedWidth = Field<int>(config, kTargetWidth);
        const int savedHeight = Field<int>(config, kTargetHeight);
        const int savedSamples = Field<int>(config, kTargetSamples);
        uint8_t* const savedColour = Field<uint8_t*>(renderer, kDrawnColour);
        const int savedScope = Field<int>(renderer, kPoolScope);
        Field<int>(config, kTargetWidth) = width;
        Field<int>(config, kTargetHeight) = height;
        Field<int>(config, kTargetSamples) = 0;

        g_query(renderer, nullptr, g_view, viewport, killScale);
        const uint32_t* states = &Field<uint32_t>(scene, kRenderStates);
        g_render(renderer, nullptr, g_view, Field<uint32_t>(frame, kEntityIndex),
                 Field<uint32_t>(frame, kEntitySerial), viewport, g_component, lodScale, killScale,
                 Field<float>(frame, kDetailScale), Field<float>(stack, kEffect), states[0],
                 states[1], states[2], states[3], reinterpret_cast<void*>(ctx->esi));
        if (Copy(renderer)) {
            g_drawnFrame = AimingOverhaul::Frame::Number();
        }
        g_pop(renderer, nullptr);

        Field<uint8_t*>(renderer, kDrawnColour) = savedColour;
        Field<int>(renderer, kPoolScope) = savedScope;
        Field<int>(config, kTargetWidth) = savedWidth;
        Field<int>(config, kTargetHeight) = savedHeight;
        Field<int>(config, kTargetSamples) = savedSamples;

        if (!g_reported) {
            g_reported = true;
            FCSE::Logf("second view: %dx%d at a field of view of %.3f, the camera's %.3f; level of "
                       "detail %.3f and kill scale %.3f, the reflection's %.3f and %.3f; %s",
                       width, height, fov, Field<float>(component + kRenderCamera, kFieldOfView),
                       lodScale, killScale, Field<float>(stack, kLodScale),
                       Field<float>(frame, kKillScale),
                       g_drawnFrame == AimingOverhaul::Frame::Number() ? "copied" : "not copied");
        }
    }

    void OnReflectionDrawn(FCSE_MidHookContext* ctx) {
        uint8_t* component = Field<uint8_t*>(reinterpret_cast<uint8_t*>(ctx->esp), kCameraComponent);
        if (component == nullptr) {
            return;
        }
        const float fov = g_want(Field<float>(component + kRenderCamera, kFieldOfView));
        if (fov > 0.0f) {
            Draw(ctx, component, fov);
        }
    }
}

bool AimingOverhaul::SecondView::Install(WantFn want) {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();
    if (!g_reflectionDrawn || !g_viewCtor || !g_cameraAssign || !g_cameraCopy || !g_cameraUpdate ||
        !g_query || !g_render || !g_pop) {
        api->Log("second view: the reflection renderer was not found in this build");
        return false;
    }
    g_want = want;
    return api->MidHook(g_reflectionDrawn.get() + kAfterCall, &OnReflectionDrawn);
}

IDirect3DTexture9* AimingOverhaul::SecondView::Latest() {
    return g_drawnFrame == Frame::Number() ? g_copy.texture : nullptr;
}

void AimingOverhaul::SecondView::ReleaseDeviceObjects() {
    Release(g_copy);
    g_owner = nullptr;
    g_refused = false;
    g_drawnFrame = Frame::kNever;
}
