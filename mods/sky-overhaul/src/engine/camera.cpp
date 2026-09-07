#include "engine/camera.h"

#include <cmath>
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
    constexpr size_t kViewPoint = 8;
    constexpr size_t kFogColourVector = 12;
    constexpr size_t kFogColour = 16;
    constexpr size_t kFogColourRange = 20;
    constexpr size_t kFogValues = 24;
    constexpr size_t kFogHeightValues = 28;
    constexpr size_t kRight = 32;
    constexpr size_t kUp = 36;
    constexpr size_t kBloom = 52;

    void Copy3(const float* from, float* out) {
        out[0] = from[0];
        out[1] = from[1];
        out[2] = from[2];
    }

    // The adjugate over the determinant, for a matrix laid out row by row.
    bool Invert(const float* m, float* out) {
        const float s0 = m[0] * m[5] - m[4] * m[1];
        const float s1 = m[0] * m[6] - m[4] * m[2];
        const float s2 = m[0] * m[7] - m[4] * m[3];
        const float s3 = m[1] * m[6] - m[5] * m[2];
        const float s4 = m[1] * m[7] - m[5] * m[3];
        const float s5 = m[2] * m[7] - m[6] * m[3];

        const float c5 = m[10] * m[15] - m[14] * m[11];
        const float c4 = m[9] * m[15] - m[13] * m[11];
        const float c3 = m[9] * m[14] - m[13] * m[10];
        const float c2 = m[8] * m[15] - m[12] * m[11];
        const float c1 = m[8] * m[14] - m[12] * m[10];
        const float c0 = m[8] * m[13] - m[12] * m[9];

        const float determinant =
            s0 * c5 - s1 * c4 + s2 * c3 + s3 * c2 - s4 * c1 + s5 * c0;
        if (std::fabs(determinant) < 1e-20f) {
            return false;
        }
        const float scale = 1.0f / determinant;

        out[0] = (m[5] * c5 - m[6] * c4 + m[7] * c3) * scale;
        out[1] = (-m[1] * c5 + m[2] * c4 - m[3] * c3) * scale;
        out[2] = (m[13] * s5 - m[14] * s4 + m[15] * s3) * scale;
        out[3] = (-m[9] * s5 + m[10] * s4 - m[11] * s3) * scale;

        out[4] = (-m[4] * c5 + m[6] * c2 - m[7] * c1) * scale;
        out[5] = (m[0] * c5 - m[2] * c2 + m[3] * c1) * scale;
        out[6] = (-m[12] * s5 + m[14] * s2 - m[15] * s1) * scale;
        out[7] = (m[8] * s5 - m[10] * s2 + m[11] * s1) * scale;

        out[8] = (m[4] * c4 - m[5] * c2 + m[7] * c0) * scale;
        out[9] = (-m[0] * c4 + m[1] * c2 - m[3] * c0) * scale;
        out[10] = (m[12] * s4 - m[13] * s2 + m[15] * s0) * scale;
        out[11] = (-m[8] * s4 + m[9] * s2 - m[11] * s0) * scale;

        out[12] = (-m[4] * c3 + m[5] * c1 - m[6] * c0) * scale;
        out[13] = (m[0] * c3 - m[1] * c1 + m[2] * c0) * scale;
        out[14] = (-m[12] * s3 + m[13] * s1 - m[14] * s0) * scale;
        out[15] = (m[8] * s3 - m[9] * s1 + m[10] * s0) * scale;
        return true;
    }

    // Where a clip-space point lands in the world, which needs the perspective divide the
    // rasteriser would otherwise have done.
    bool Unproject(const float* inverse, float x, float y, float z, float* out) {
        const float clip[4] = {x, y, z, 1.0f};
        float world[4];
        for (int row = 0; row < 4; row++) {
            world[row] = inverse[row * 4 + 0] * clip[0] + inverse[row * 4 + 1] * clip[1] +
                         inverse[row * 4 + 2] * clip[2] + inverse[row * 4 + 3] * clip[3];
        }
        if (std::fabs(world[3]) < 1e-12f) {
            return false;
        }
        out[0] = world[0] / world[3];
        out[1] = world[1] / world[3];
        out[2] = world[2] / world[3];
        return true;
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

    Copy3(block + kPosition, out.position);
    Copy3(block + kDirection, out.direction);
    Copy3(block + kViewPoint, out.viewPoint);
    Copy3(block + kRight, out.right);
    Copy3(block + kUp, out.up);
    Copy3(block + kFogColourVector, out.fogColourVector);
    Copy3(block + kFogColour, out.fogColour);
    Copy3(block + kFogColourRange, out.fogColourRange);
    Copy3(block + kFogValues, out.fogValues);
    std::memcpy(out.fogHeightValues, block + kFogHeightValues, sizeof(out.fogHeightValues));
    out.bloom = block[kBloom];

    float inverse[16];
    if (!Invert(out.viewProjection, inverse)) {
        return false;
    }

    // The camera is the one point the transform sends to the origin with no w at all, so it is
    // that column of the inverse, taken back out of homogeneous form.
    const float w = inverse[3 * 4 + 2];
    if (std::fabs(w) < 1e-12f) {
        return false;
    }
    out.eye[0] = inverse[0 * 4 + 2] / w;
    out.eye[1] = inverse[1 * 4 + 2] / w;
    out.eye[2] = inverse[2 * 4 + 2] / w;

    // Two points on each corner's ray, near plane and far, which subtract to the direction along
    // it. Reading the direction off the camera basis instead would depend on registers no shader
    // in the sky pass declares.
    static constexpr float kCorners[4][2] = {{-1.0f, 1.0f}, {1.0f, 1.0f}, {-1.0f, -1.0f},
                                             {1.0f, -1.0f}};
    for (int corner = 0; corner < 4; corner++) {
        float atNear[3];
        float atFar[3];
        if (!Unproject(inverse, kCorners[corner][0], kCorners[corner][1], 0.0f, atNear) ||
            !Unproject(inverse, kCorners[corner][0], kCorners[corner][1], 1.0f, atFar)) {
            return false;
        }
        out.corners[corner][0] = atFar[0] - atNear[0];
        out.corners[corner][1] = atFar[1] - atNear[1];
        out.corners[corner][2] = atFar[2] - atNear[2];
    }

    // The same corners from the basis: how far off centre a corner sits is the projection's own
    // scale, and the camera's axes carry it into the world without touching a world coordinate.
    const float horizontalScale = transforms[16 + 0];
    const float across = horizontalScale != 0.0f ? 1.0f / horizontalScale : 0.0f;
    const float down = out.verticalScale != 0.0f ? 1.0f / out.verticalScale : 0.0f;
    for (int corner = 0; corner < 4; corner++) {
        const float x = kCorners[corner][0] * across;
        const float y = kCorners[corner][1] * down;
        for (int axis = 0; axis < 3; axis++) {
            out.basisCorners[corner][axis] =
                out.direction[axis] + out.right[axis] * x + out.up[axis] * y;
        }
    }
    return true;
}
