#include "wet.h"

#include "ripples.h"
#include "sky_model.h"

#include "engine/cloud_layer.h"

#include <algorithm>
#include <initializer_list>

namespace {
    using SkyOverhaul::KnownShaders::kNoRegister;
    namespace Field = SkyOverhaul::KnownShaders;

    // Fully wet by day: how many times stronger a specular colour gets, what it gains where it had
    // none, the most wetting raises it to, and how many times sharper its gloss. At night the
    // colour stays the material's own: the engine's night ambient is no light to shine in.
    constexpr float kSpecularGain = 6.0f;
    constexpr float kSpecularFloor = 0.3f;
    constexpr float kSpecularMost = 1.0f;
    constexpr float kGlossGain = 6.0f;
    // In a downpour: how many times more the water's waves bend its look, and the share of its
    // straight-down mirror lost. Light rain hardly roughens it, by the square.
    constexpr float kWaterRoughness = 4.0f;
    constexpr float kMirrorLost = 0.9f;

    // Below this nothing changes.
    constexpr float kFaintest = 0.01f;

    float g_wetness = 0.0f;
    float g_rain = 0.0f;
    float g_puddles = 0.0f;
    float g_clock = 0.0f;
    float g_daylight = 1.0f;

    class Changes {
    public:
        Changes(IDirect3DDevice9* device, const uint8_t* shine, SkyOverhaul::DomeDraw::ShineSaved& saved)
            : m_device(device), m_shine(shine), m_saved(saved) {}

        // Hands `change` the engine's value of the field's register, `registers` of them from there.
        template <class Change>
        void Apply(int field, Change change, int registers = 1) {
            const uint8_t first = m_shine[field];
            if (first == kNoRegister) {
                return;
            }
            for (int i = 0; i < registers && m_saved.count < SkyOverhaul::DomeDraw::kShineSaved; i++) {
                const UINT reg = first + static_cast<UINT>(i);
                float* engine = m_saved.values[m_saved.count];
                if (FAILED(m_device->GetPixelShaderConstantF(reg, engine, 1))) {
                    continue;
                }
                float value[4] = {engine[0], engine[1], engine[2], engine[3]};
                change(value);
                m_device->SetPixelShaderConstantF(reg, value, 1);
                m_saved.registers[m_saved.count++] = reg;
            }
        }

    private:
        IDirect3DDevice9* m_device;
        const uint8_t* m_shine;
        SkyOverhaul::DomeDraw::ShineSaved& m_saved;
    };
}

void SkyOverhaul::Wet::Set(float wetness, float rain, float puddles, float clock) {
    g_wetness = wetness;
    g_rain = rain;
    g_puddles = puddles;
    g_clock = clock;
    CloudLayer::Lighting lighting;
    if (CloudLayer::Latest(lighting)) {
        g_daylight = 1.0f - SkyModel::Night(lighting.sunDirection[2]);
    }
}

float SkyOverhaul::Wet::Puddles() {
    return g_puddles;
}

float SkyOverhaul::Wet::Wetness() {
    return g_wetness;
}

float SkyOverhaul::Wet::Rain() {
    return g_rain;
}

float SkyOverhaul::Wet::Clock() {
    return g_clock;
}

void SkyOverhaul::Wet::Shine(IDirect3DDevice9* device, const uint8_t* shine,
                             DomeDraw::ShineSaved& saved) {
    const float w = g_wetness;
    const float r = g_rain * g_rain;
    if (w < kFaintest && r < kFaintest) {
        return;
    }
    Changes changes(device, shine, saved);
    if (w >= kFaintest) {
        const float lit = w * g_daylight;
        const auto colour = [lit](float* v) {
            for (int c = 0; c < 3; c++) {
                const float wet = v[c] * (1.0f + kSpecularGain * lit) + kSpecularFloor * lit;
                v[c] = (std::max)(v[c], (std::min)(wet, kSpecularMost));
            }
        };
        const auto gloss = [w](float* v) {
            for (int c = 0; c < 4; c++) {
                v[c] *= 1.0f + kGlossGain * w;
            }
        };
        for (int field : {Field::kSpecularColorBase, Field::kSpecularColor1,
                          Field::kSpecularColorBaseClean, Field::kSpecularColor1Clean,
                          Field::kSpecularColorBaseBroken, Field::kSpecularColor1Broken}) {
            changes.Apply(field, colour);
        }
        changes.Apply(Field::kDetailSpecularColor, colour, shine[Field::kDetailSpecularCount]);
        changes.Apply(Field::kSpecularPower, gloss);
        changes.Apply(Field::kSpecularShininess, gloss);
    }
    // Water while it rains reads the drops' rings in place of its own waves.
    IDirect3DTexture9* rings = Ripples::Texture();
    const uint8_t sampler = shine[Field::kWaterNormalSampler];
    if (g_rain >= kFaintest && rings != nullptr && sampler != kNoRegister &&
        SUCCEEDED(device->GetTexture(sampler, &saved.texture)) && saved.texture != nullptr) {
        saved.sampler = sampler;
        device->SetTexture(sampler, rings);
    }
    if (r >= kFaintest) {
        changes.Apply(Field::kNormalOffsetScale, [r](float* v) { v[0] *= 1.0f + kWaterRoughness * r; });
        changes.Apply(Field::kFresnelBias, [r](float* v) { v[0] *= 1.0f - kMirrorLost * r; });
    }
}
