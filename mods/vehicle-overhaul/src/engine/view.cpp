#include "engine/view.h"

#include <algorithm>
#include <cmath>
#include <cstring>

namespace {
    // c4 through c11: the view-projection and then the projection.
    constexpr UINT kTransformsRegister = 4;
    constexpr UINT kTransformsCount = 8;
    constexpr UINT kCameraPositionRegister = 45;
    constexpr UINT kFogValuesRegister = 51;
    constexpr UINT kFogHeightValuesRegister = 52;

    float Saturate(float value) { return std::clamp(value, 0.0f, 1.0f); }
}

bool VehicleOverhaul::View::Read(IDirect3DDevice9* device, Camera& out) {
    float transforms[kTransformsCount * 4] = {};
    float eye[4] = {};
    float fogValues[4] = {};
    if (FAILED(device->GetVertexShaderConstantF(kTransformsRegister, transforms, kTransformsCount)) ||
        FAILED(device->GetVertexShaderConstantF(kCameraPositionRegister, eye, 1)) ||
        FAILED(device->GetVertexShaderConstantF(kFogValuesRegister, fogValues, 1)) ||
        FAILED(device->GetVertexShaderConstantF(kFogHeightValuesRegister, out.fogHeightValues, 1))) {
        return false;
    }
    std::memcpy(out.viewProjection, transforms, sizeof(out.viewProjection));
    std::memcpy(out.eye, eye, sizeof(out.eye));
    std::memcpy(out.fogValues, fogValues, sizeof(out.fogValues));
    out.verticalScale = transforms[16 + 5];
    return true;
}

float VehicleOverhaul::View::Clear(const Camera& camera, const float* point) {
    const float dx = point[0] - camera.eye[0];
    const float dy = point[1] - camera.eye[1];
    const float dz = point[2] - camera.eye[2];
    const float distance = std::sqrt(dx * dx + dy * dy + dz * dz);
    const float* f = camera.fogValues;
    const float* h = camera.fogHeightValues;
    const float fog = Saturate(distance * f[0] + f[1]) * f[2] *
                      (Saturate(point[2] * h[0] + h[1]) * h[2] + h[3]);
    return Saturate(1.0f - fog);
}
