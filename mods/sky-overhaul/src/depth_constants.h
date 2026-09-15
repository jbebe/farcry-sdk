// The registers and sampler shaders/depth.inc.fx reads: the linear depth, the lens and the camera.
#pragma once

#include "engine/camera.h"
#include "engine/depth_texture.h"

#include <d3d9.h>

namespace SkyOverhaul::DepthConstants {

// Above the clouds' registers. A pass's own registers follow these.
constexpr UINT kFirst = 91;
constexpr UINT kCount = 5;
constexpr DWORD kSampler = 3;

// The share of the view distance past which the depth is the engine's clear, not geometry.
constexpr float kFarthestShare = 0.995f;

// Uploads the registers and binds the depth. `width` and `height` are the full-resolution target's.
inline void Set(IDirect3DDevice9* device, const Camera::View& view, const DepthTexture::Found& depth,
                UINT width, UINT height) {
    const float values[kCount * 4] = {
        depth.metreWeights[0], depth.metreWeights[1], depth.metreWeights[2],
        view.viewDistance * kFarthestShare,
        1.0f / view.horizontalScale, 1.0f / view.verticalScale,
        1.0f / static_cast<float>(width), 1.0f / static_cast<float>(height),
        view.right[0], view.right[1], view.right[2], 0.0f,
        view.up[0], view.up[1], view.up[2], 0.0f,
        view.direction[0], view.direction[1], view.direction[2], 0.0f};
    device->SetPixelShaderConstantF(kFirst, values, kCount);
    device->SetTexture(kSampler, depth.texture);
}

}
