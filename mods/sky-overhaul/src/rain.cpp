#include "rain.h"

#include "blend.h"
#include "hash.h"
#include "engine/camera.h"
#include "engine/clock.h"
#include "engine/com.h"
#include "engine/dome_draw.h"
#include "engine/rain_component.h"
#include "engine/rain_cover.h"
#include "engine/screen_draw.h"
#include "engine/shader.h"
#include "fcse_api.h"
#include "ripples.h"
#include "weather/weather.h"
#include "wet.h"

#include "rain_drops_ps.h"
#include "rain_drops_vs.h"

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <iterator>

namespace {
    // rain.fx's registers.
    constexpr UINT kFirstVertexConstant = 230;
    constexpr UINT kVertexConstants = 12;
    constexpr UINT kViewProjection = 230;
    constexpr UINT kCoverTransform = 234;
    constexpr UINT kBox = 238;
    constexpr UINT kStreak = 239;
    constexpr UINT kScreen = 240;
    constexpr UINT kLook = 241;
    constexpr UINT kDropColour = 91;
    constexpr DWORD kCoverSampler = 0;

    constexpr UINT kDrops = 16384;

    // Nested boxes round the eye, each with its own drops, so they thin out with distance: metres
    // across and how many fall in it at the rain's full strength.
    struct Layer {
        float size;
        UINT drops;
    };
    constexpr Layer kLayers[] = {{8.0f, 5000}, {20.0f, 14000}, {44.0f, 16384}};

    // Drizzle to downpour: how fast the drops fall in metres a second, and how wide they are.
    constexpr float kSlowest = 5.0f;
    constexpr float kFastest = 9.0f;
    constexpr float kThinnest = 0.0008f;
    constexpr float kWidest = 0.003f;

    // How long each streak is exposed for, in seconds.
    constexpr float kExposure = 1.0f / 30.0f;
    // The share of a box edge the drops fade over, the narrowest a streak is drawn in pixels, how
    // opaque a full-width drop is, and how far drops are drawn.
    constexpr float kEdgeFade = 0.25f;
    constexpr float kNarrowest = 1.0f;
    constexpr float kOpacity = 0.5f;
    constexpr float kFarthest = 30.0f;

    // Below this the rain is gone.
    constexpr float kFaintest = 0.01f;
    // Without the weather: how fast the game's rain wets the world and fills the puddles, and the air
    // dries them, a second.
    constexpr float kWetting = 1.0f / 120.0f;
    constexpr float kDrying = 1.0f / 900.0f;
    constexpr float kFilling = 1.0f / 300.0f;
    constexpr float kDraining = 1.0f / 1800.0f;
    // The player's speed is ignored past this, which is a teleport or a load, not a run.
    constexpr float kFastestEye = 30.0f;
    // The rings' clock wraps every this many seconds, which keeps it precise in a float.
    constexpr double kRingClockWrap = 4096.0;

    struct DropVertex {
        float seed[4];
        float corner;
    };

    bool g_enabled = false;
    SkyOverhaul::VertexShader g_vertexShader{"rain drops", g_rainDropsVertexShader};
    SkyOverhaul::PixelShader g_pixelShader{"rain drops", g_rainDropsPixelShader};
    IDirect3DDevice9* g_owner = nullptr;
    IDirect3DVertexBuffer9* g_vertices = nullptr;
    IDirect3DIndexBuffer9* g_indices = nullptr;
    IDirect3DVertexDeclaration9* g_declaration = nullptr;
    bool g_refused = false;

    SkyOverhaul::Stopwatch g_stopwatch;
    double g_seconds = 0.0;
    double g_ringClock = 0.0;
    float g_wetness = 0.0f;
    float g_puddles = 0.0f;
    float g_lastEye[3] = {};
    float g_eyeVelocity[3] = {};
    bool g_haveEye = false;

    using SkyOverhaul::Mix;
    using SkyOverhaul::NextUnit;

    bool FillVertices() {
        void* data = nullptr;
        if (FAILED(g_vertices->Lock(0, 0, &data, 0))) {
            return false;
        }
        auto* vertices = static_cast<DropVertex*>(data);
        uint32_t state = 0x9E3779B9u;
        for (UINT drop = 0; drop < kDrops; drop++) {
            const float seed[4] = {NextUnit(state), NextUnit(state), NextUnit(state),
                                   NextUnit(state)};
            for (UINT corner = 0; corner < 4; corner++) {
                DropVertex& vertex = vertices[drop * 4 + corner];
                std::memcpy(vertex.seed, seed, sizeof(seed));
                vertex.corner = static_cast<float>(corner);
            }
        }
        g_vertices->Unlock();
        return true;
    }

    bool FillIndices() {
        void* data = nullptr;
        if (FAILED(g_indices->Lock(0, 0, &data, 0))) {
            return false;
        }
        auto* indices = static_cast<uint16_t*>(data);
        for (UINT drop = 0; drop < kDrops; drop++) {
            const uint16_t first = static_cast<uint16_t>(drop * 4);
            const uint16_t quad[6] = {first,
                                      static_cast<uint16_t>(first + 1),
                                      static_cast<uint16_t>(first + 2),
                                      static_cast<uint16_t>(first + 2),
                                      static_cast<uint16_t>(first + 1),
                                      static_cast<uint16_t>(first + 3)};
            std::memcpy(indices + drop * 6, quad, sizeof(quad));
        }
        g_indices->Unlock();
        return true;
    }

    bool Ensure(IDirect3DDevice9* device) {
        if (g_owner != device) {
            SkyOverhaul::Rain::ReleaseDeviceObjects();
            g_owner = device;
            g_refused = false;
        }
        if (g_refused) {
            return false;
        }
        if (g_vertices != nullptr) {
            return true;
        }
        const D3DVERTEXELEMENT9 elements[] = {
            {0, 0, D3DDECLTYPE_FLOAT4, D3DDECLMETHOD_DEFAULT, D3DDECLUSAGE_POSITION, 0},
            {0, 16, D3DDECLTYPE_FLOAT1, D3DDECLMETHOD_DEFAULT, D3DDECLUSAGE_TEXCOORD, 1},
            D3DDECL_END()};
        const bool made =
            SUCCEEDED(device->CreateVertexDeclaration(elements, &g_declaration)) &&
            SUCCEEDED(device->CreateVertexBuffer(kDrops * 4 * sizeof(DropVertex), D3DUSAGE_WRITEONLY,
                                                 0, D3DPOOL_MANAGED, &g_vertices, nullptr)) &&
            SUCCEEDED(device->CreateIndexBuffer(kDrops * 6 * sizeof(uint16_t), D3DUSAGE_WRITEONLY,
                                                D3DFMT_INDEX16, D3DPOOL_MANAGED, &g_indices,
                                                nullptr)) &&
            FillVertices() && FillIndices();
        if (!made) {
            SkyOverhaul::Rain::ReleaseDeviceObjects();
            g_owner = device;
            g_refused = true;
            FCSE::Logf("rain: this device would not take the drops, so the game's own fall");
        }
        return made;
    }

    // The player's own movement, which streaks the drops the other way.
    void TrackEye(const float eye[3], float seconds) {
        if (g_haveEye && seconds > 0.0f) {
            float velocity[3];
            float speed = 0.0f;
            for (int i = 0; i < 3; i++) {
                velocity[i] = (eye[i] - g_lastEye[i]) / seconds;
                speed += velocity[i] * velocity[i];
            }
            const bool jumped = std::sqrt(speed) > kFastestEye;
            for (int i = 0; i < 3; i++) {
                g_eyeVelocity[i] = jumped ? 0.0f : Mix(g_eyeVelocity[i], velocity[i], 0.5f);
            }
        }
        std::memcpy(g_lastEye, eye, sizeof(g_lastEye));
        g_haveEye = true;
    }

    double Wrap(double value) {
        return value - std::floor(value);
    }

    // What the drops change that the engine's own draws after them read: the vertex shader registers
    // they write, and the instancing the engine may have left on its first two streams, under which
    // a plain draw reads past its buffers.
    class VertexState {
    public:
        explicit VertexState(IDirect3DDevice9* device) : m_device(device) {
            m_device->GetVertexShaderConstantF(kFirstVertexConstant, m_constants, kVertexConstants);
            for (UINT stream = 0; stream < kStreams; stream++) {
                m_device->GetStreamSourceFreq(stream, &m_frequencies[stream]);
                m_device->SetStreamSourceFreq(stream, 1);
            }
        }
        ~VertexState() {
            m_device->SetVertexShaderConstantF(kFirstVertexConstant, m_constants, kVertexConstants);
            for (UINT stream = 0; stream < kStreams; stream++) {
                m_device->SetStreamSourceFreq(stream, m_frequencies[stream]);
            }
        }

    private:
        static constexpr UINT kStreams = 2;
        IDirect3DDevice9* m_device;
        float m_constants[kVertexConstants * 4];
        UINT m_frequencies[kStreams] = {};
    };

    // How wet the open world is and how full its puddles: the weather's own reckoning while it runs,
    // else the game's rain wetting the ground and filling the puddles over a few minutes, and the
    // dry air taking both back over a quarter and half an hour. The rings' clock runs on with it.
    void UpdateWet(float strength, float seconds) {
        g_ringClock = std::fmod(g_ringClock + seconds, kRingClockWrap);
        SkyOverhaul::Weather::Reading reading;
        if (SkyOverhaul::Weather::Latest(reading)) {
            g_wetness = reading.wet;
            g_puddles = reading.puddles;
        } else {
            const bool raining = strength >= kFaintest;
            g_wetness = std::clamp(g_wetness + (raining ? kWetting : -kDrying) * seconds, 0.0f, 1.0f);
            g_puddles = std::clamp(
                g_puddles + (raining ? kFilling * strength : -kDraining) * seconds, 0.0f, 1.0f);
        }
        SkyOverhaul::Wet::Set(g_wetness, strength, g_puddles, static_cast<float>(g_ringClock));
    }

    // What the drops draw from.
    struct Shower {
        const SkyOverhaul::Frame::Pass& pass;
        const SkyOverhaul::Camera::View& view;
        const SkyOverhaul::RainCover::Found& cover;
        float direction[3];
        float strength;
        // Metres a second.
        float speed;
    };

    // The cover map's transform, eye-relative, and the sampler state its compare reads through.
    void BindCover(SkyOverhaul::ScreenDraw& draw, const Shower& shower, DWORD sampler,
                   float out[16]) {
        SkyOverhaul::Camera::FromEye(shower.cover.transform, shower.view.eye, out);
        // Filtered, a shadow texture's projected read is a soft compare.
        shower.pass.device->SetTexture(sampler, shower.cover.texture);
        draw.Linear(sampler);
    }

    // A frame of the rain clock, wrapped to a share of `size`: `shift` plus how far a point moving
    // at `velocity` has gone by now.
    float Moved(double shift, double velocity, double size) {
        return static_cast<float>(Wrap((shift + velocity * g_seconds) / size));
    }

    // False when the device would not take what the drops need.
    bool DrawDrops(const Shower& shower) {
        const SkyOverhaul::Frame::Pass& pass = shower.pass;
        const SkyOverhaul::Camera::View& view = shower.view;
        IDirect3DDevice9* device = pass.device;
        // First, since a new device lets go of the shaders along with the buffers.
        if (!Ensure(device)) {
            return false;
        }
        IDirect3DVertexShader9* vertexShader = g_vertexShader.Get(device);
        IDirect3DPixelShader9* pixelShader = g_pixelShader.Get(device);
        if (vertexShader == nullptr || pixelShader == nullptr) {
            return false;
        }

        SkyOverhaul::ScreenDraw draw(device, kDropColour, 1);
        VertexState saved(device);

        device->SetRenderState(D3DRS_ZENABLE, D3DZB_TRUE);
        device->SetRenderState(D3DRS_ALPHABLENDENABLE, TRUE);
        device->SetRenderState(D3DRS_SRCBLEND, D3DBLEND_SRCALPHA);
        device->SetRenderState(D3DRS_DESTBLEND, D3DBLEND_INVSRCALPHA);
        draw.KeepAlpha();

        float transforms[32];
        SkyOverhaul::Camera::FromEye(view.viewProjection, view.eye, transforms);
        BindCover(draw, shower, kCoverSampler, transforms + 16);
        device->SetVertexShaderConstantF(kViewProjection, transforms, 4);
        device->SetVertexShaderConstantF(kCoverTransform, transforms + 16, 4);

        const float strength = shower.strength;
        const float speed = shower.speed;
        const float screen[4] = {0.5f * static_cast<float>(pass.viewport.Width),
                                 0.5f * static_cast<float>(pass.viewport.Height), kNarrowest,
                                 Mix(kThinnest, kWidest, strength)};
        const float look[4] = {kOpacity, kFarthest, view.verticalScale, 0.0f};
        device->SetVertexShaderConstantF(kScreen, screen, 1);
        device->SetVertexShaderConstantF(kLook, look, 1);
        // The drops take the fog's colour, which is what the sky round them gives.
        const float colour[4] = {view.fogColour[0], view.fogColour[1], view.fogColour[2],
                                 0.5f / static_cast<float>(shower.cover.size)};
        device->SetPixelShaderConstantF(kDropColour, colour, 1);

        float streak[4] = {0.0f, 0.0f, 0.0f, kEdgeFade};
        for (int i = 0; i < 3; i++) {
            streak[i] = (shower.direction[i] * speed - g_eyeVelocity[i]) * kExposure;
        }
        device->SetVertexShaderConstantF(kStreak, streak, 1);

        device->SetVertexDeclaration(g_declaration);
        device->SetStreamSource(0, g_vertices, 0, sizeof(DropVertex));
        device->SetIndices(g_indices);
        device->SetVertexShader(vertexShader);
        device->SetPixelShader(pixelShader);

        const float share = std::pow(strength, 0.75f);
        for (size_t i = 0; i < std::size(kLayers); i++) {
            const Layer& layer = kLayers[i];
            const UINT drops = (std::min)(kDrops, static_cast<UINT>(layer.drops * share));
            if (drops == 0) {
                continue;
            }
            // Where the lattice of drops has fallen to, as a share of the box, with the box kept
            // round the eye; each layer starts somewhere else so their drops do not line up.
            float box[4] = {0.0f, 0.0f, 0.0f, layer.size};
            for (int axis = 0; axis < 3; axis++) {
                box[axis] = Moved((0.5 + 0.37 * static_cast<double>(i)) * layer.size - view.eye[axis],
                                  static_cast<double>(shower.direction[axis]) * speed, layer.size);
            }
            device->SetVertexShaderConstantF(kBox, box, 1);
            device->DrawIndexedPrimitive(D3DPT_TRIANGLELIST, 0, 0, drops * 4, 0, drops * 2);
        }
        return true;
    }
}

void SkyOverhaul::Rain::SetEnabled(bool enabled) {
    g_enabled = enabled;
    DomeDraw::SetShine(enabled ? &Wet::Shine : nullptr);
    if (!enabled) {
        RainComponent::SetDropsHidden(false);
        Wet::Set(0.0f, 0.0f, 0.0f, 0.0f);
    }
}

void SkyOverhaul::Rain::OnScenePass(const Frame::Pass& pass) {
    if (!pass.sky || !pass.live) {
        return;
    }
    const float seconds = g_stopwatch.Lap();
    Camera::View view;
    if (!Camera::Read(pass.device, view)) {
        return;
    }
    TrackEye(view.eye, seconds);
    if (!g_enabled) {
        return;
    }

    RainComponent::State rain;
    const bool raining = RainComponent::Latest(rain) && rain.started;
    const float strength = raining ? std::clamp(rain.strength, 0.0f, 1.0f) : 0.0f;
    UpdateWet(strength, seconds);
    Ripples::OnScenePass(pass, strength, Wet::Clock());

    // The game's drops come back for any frame ours cannot be drawn in.
    RainCover::Found cover;
    if (!raining || !RainCover::Latest(pass.frame, cover) || view.verticalScale == 0.0f) {
        RainComponent::SetDropsHidden(false);
        return;
    }
    if (strength < kFaintest) {
        RainComponent::SetDropsHidden(true);
        return;
    }
    g_seconds += seconds;
    const Shower shower = {pass, view, cover,
                           {rain.direction[0], rain.direction[1], rain.direction[2]},
                           strength, Mix(kSlowest, kFastest, strength)};
    RainComponent::SetDropsHidden(DrawDrops(shower));
}

void SkyOverhaul::Rain::ReleaseDeviceObjects() {
    g_vertexShader.Release();
    g_pixelShader.Release();
    Release(g_vertices);
    Release(g_indices);
    Release(g_declaration);
    g_owner = nullptr;
    RainCover::ReleaseDeviceObjects();
}
