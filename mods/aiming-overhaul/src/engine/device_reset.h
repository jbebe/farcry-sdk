// The moment before the device is reset, the only point at which anything held in D3DPOOL_DEFAULT
// can still be freed.
#pragma once

namespace AimingOverhaul::DeviceReset {

// Hooks the device's Reset so `onRelease` runs first. False, and logged, when it cannot be hooked;
// a caller that holds device resources must then hold none at all.
bool Install(void (*onRelease)());

}
