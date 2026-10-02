// Which device is actually in the player's hands.
//
// The action map has lost that by the time it reaches gameplay, so the raw drivers are watched
// instead. Vibration, the aim and sprint toggles, aim assist and look sensitivity all ask this one
// file, so they share one answer. Its patterns start at the entries it hooks, and FCSE matches
// patterns against live code, so a second file matching the same driver would find a hook's jump.
#pragma once

namespace UFCP {

// True while the gamepad is the device being used. False until something is pressed.
bool IsPadActiveDevice();

// Sets the callback made when the answer above flips. One consumer, so a second call replaces the
// first rather than adding to it.
void OnInputDeviceChanged(void (*callback)());

// Hooks the gamepad, keyboard and mouse drivers. Call once from FCSE_Load.
void InstallInputDeviceTracking();

}
