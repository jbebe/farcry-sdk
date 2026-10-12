#include "lights.h"

#include "grass_light.h"
#include "lamp_glow.h"

#include "engine/device_reset.h"
#include "engine/entity.h"
#include "engine/frame.h"
#include "engine/handoff.h"
#include "engine/headlights.h"

namespace {
    void ReleaseDeviceObjects() {
        VehicleOverhaul::LampGlow::ReleaseDeviceObjects();
        VehicleOverhaul::GrassLight::ReleaseDeviceObjects();
    }
}

namespace VehicleOverhaul::Lights {

bool Install() {
    // Without the device teardown nothing may be held on the device, so nothing is drawn.
    return Entity::Install() && DeviceReset::Install(&ReleaseDeviceObjects) && Headlights::Install() &&
           Handoff::Install(&Headlights::Post) && Frame::Install(&LampGlow::OnSkyPass, &LampGlow::OnComposite) &&
           GrassLight::Install();
}

}
