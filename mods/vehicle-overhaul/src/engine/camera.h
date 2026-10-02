// The local player's camera manager, and the cameras it can be asked for by archetype name.
#pragma once

namespace VehicleOverhaul::Camera {

// Entity-library archetype names from game data, not strings in Dunia.dll.
inline constexpr const char* kFirst = "Cameras.Camera.First";
inline constexpr const char* kThird = "Cameras.Camera.Third";

// False, and logged, when this build lacks any of the manager's entry points.
bool Install();

// Null outside a session.
void* Manager();

// The camera the manager has up, or null.
void* Active(void* manager);

// Reports nothing: whether it took is Active() changing.
void ActivateByName(void* manager, const char* name);

// Set while a cutscene owns the camera, which makes a switch a silent no-op.
bool Locked(void* manager);

}
