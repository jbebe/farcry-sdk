#include "lights.h"

#include "lamp_glow.h"

#include "engine/device_reset.h"
#include "engine/entity.h"
#include "engine/frame.h"
#include "engine/handoff.h"
#include "engine/headlights.h"

namespace VehicleOverhaul::Lights {

bool Install() {
    // Without the device's reset nothing may be held on the device, so nothing is drawn.
    return Entity::Install() && DeviceReset::Install(&LampGlow::ReleaseDeviceObjects) && Headlights::Install() &&
           Handoff::Install(&Headlights::Post) && Frame::Install(&LampGlow::OnSkyPass, &LampGlow::OnComposite);
}

}
