// The moment before the engine resets the device.
//
// A resolution change resets the device rather than rebuilding it, and a live D3DPOOL_DEFAULT
// resource makes that reset fail. See docs/docs/engine-internals/presentation-and-input.md.
#pragma once

namespace SkyOverhaul::DeviceReset {

// Hooks the renderer's device teardown. `onRelease` runs before the device is reset, which is the
// only point at which anything held in D3DPOOL_DEFAULT can still be freed. Call once from
// FCSE_Load. False means the teardown was not found, which it logs, and nothing is left hooked -
// a caller that holds device resources must then hold none at all.
bool Install(void (*onRelease)());

}
