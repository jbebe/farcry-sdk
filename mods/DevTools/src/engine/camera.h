// The camera manager, and the free camera prototype it can be asked for.
#pragma once

#include <cstdint>

namespace DevTools::Camera {

// The archetype names the manager is asked for. These are entity-library names from game data, not
// strings in Dunia.dll, which is why a data set can simply not carry the free camera.
constexpr const char* kGameplay = "Cameras.Camera.First";
constexpr const char* kFree = "Cameras.Camera.Free";

void Install();

// The camera the manager currently has up, or null.
void* Active(void* manager);

// Asks for a camera by archetype name. Reports nothing: whether it took is Active() changing.
void ActivateByName(void* manager, const char* name);

// The manager's Locked byte. A cutscene sets it, and while it is set a switch is a silent no-op.
uint8_t* Locked(void* manager);

// Points a camera at whatever the manager is focused on. The manager only does this itself on the
// activation that instantiates the camera.
void SnapToFocus(void* manager, void* camera);

// Where the pooled render camera is, its pitch, roll and yaw, and its field of view, angles in radians.
struct View {
    float position[3];
    float pitch;
    float roll;
    float yaw;
    float fov;
};

// The view the frame is drawn from, or false when there is no render camera to ask.
bool CurrentView(void* manager, View& view);

// Move axes are unit vectors in the camera's local frame and look values are rates; speed is metres
// per second and is not folded into the axes.
void ClearAxes(void* camera);
void SetMove(void* camera, float forward, float strafe, float vertical);
void SetLook(void* camera, float yaw, float pitch);
void SetSpeed(void* camera, float metresPerSecond);

// Called from the free camera's own update, with the component. Hooking it is what lets input be
// written in the frame the engine is about to read it.
using UpdateFn = void (*)(void* camera);
void OnUpdate(UpdateFn fn);

}
