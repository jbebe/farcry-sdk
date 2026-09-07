#include "engine/camera.h"

#include <cstdint>
#include <cstring>

namespace {
    // Where the engine binds these for every shader in the frame.
    constexpr uint32_t kViewProjectionRegister = 4;
    constexpr uint32_t kCameraBlockRegister = 45;

    // c4 through c11: the view-projection and then the projection.
    constexpr UINT kViewProjectionCount = 8;
    // c45 through c58: camera, fog and the bloom factor, in one read.
    constexpr UINT kCameraBlockCount = 14;

    // Offsets into that block, in floats.
    constexpr size_t kPosition = 0;
    constexpr size_t kDirection = 4;
    constexpr size_t kFogColourVector = 12;
    constexpr size_t kFogColour = 16;
    constexpr size_t kFogColourRange = 20;
    constexpr size_t kFogValues = 24;
    constexpr size_t kFogHeightValues = 28;
    constexpr size_t kRight = 32;
    constexpr size_t kUp = 36;
    constexpr size_t kBloom = 52;

    // The viewport's corners in normalised device coordinates, in the order the quad wants them.
    constexpr float kCorners[4][2] = {{-1.0f, 1.0f}, {1.0f, 1.0f}, {-1.0f, -1.0f}, {1.0f, -1.0f}};

    void Copy3(const float* from, float* out) {
        out[0] = from[0];
        out[1] = from[1];
        out[2] = from[2];
    }
}

bool SkyOverhaul::Camera::Read(IDirect3DDevice9* device, View& out) {
    float transforms[kViewProjectionCount * 4] = {};
    float block[kCameraBlockCount * 4] = {};
    if (FAILED(device->GetVertexShaderConstantF(kViewProjectionRegister, transforms,
                                                kViewProjectionCount)) ||
        FAILED(device->GetVertexShaderConstantF(kCameraBlockRegister, block, kCameraBlockCount))) {
        return false;
    }

    std::memcpy(out.viewProjection, transforms, sizeof(out.viewProjection));
    out.verticalScale = transforms[16 + 5];

    Copy3(block + kPosition, out.eye);
    Copy3(block + kDirection, out.direction);
    Copy3(block + kFogColourVector, out.fogColourVector);
    Copy3(block + kFogColour, out.fogColour);
    Copy3(block + kFogColourRange, out.fogColourRange);
    Copy3(block + kFogValues, out.fogValues);
    std::memcpy(out.fogHeightValues, block + kFogHeightValues, sizeof(out.fogHeightValues));
    out.bloom = block[kBloom];

    // How far off centre a corner sits is the projection's own scale, and the camera's axes carry
    // that into the world. Inverting the view-projection instead recovers the position to a
    // millimetre but the directions only to within half a degree, which is metres of wander by the
    // time a ray has travelled a kilometre.
    const float horizontalScale = transforms[16 + 0];
    const float across = horizontalScale != 0.0f ? 1.0f / horizontalScale : 0.0f;
    const float down = out.verticalScale != 0.0f ? 1.0f / out.verticalScale : 0.0f;
    const float* right = block + kRight;
    const float* up = block + kUp;
    for (int corner = 0; corner < 4; corner++) {
        const float x = kCorners[corner][0] * across;
        const float y = kCorners[corner][1] * down;
        for (int axis = 0; axis < 3; axis++) {
            out.corners[corner][axis] = out.direction[axis] + right[axis] * x + up[axis] * y;
        }
    }
    return true;
}
