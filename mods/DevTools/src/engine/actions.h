// Scripted input: actions the game takes as if its own key bindings had fired them. Where they join
// the game's input is in docs/docs/engine-internals/demo-recording.md.
#pragma once

#include <cstdint>
#include <span>
#include <string>

namespace DevTools::Actions {

// Hooks the terminal's input pass. A site not found is logged.
void Install();

// Fires the action `signal`, the CRC-32 of its name, with up to three values in the next input pass.
// Safe from any thread.
void Send(uint32_t signal, std::span<const float> values);

// Keeps every action the terminal hands out, from the player or a script, until StopRecording writes
// them to `path`, one a line: seconds from the start, the signal in hex, its values. Empty, or why not.
void StartRecording();
std::string StopRecording(const std::string& path);

// Fires a recording's actions at the times they were recorded. Empty, or why not.
std::string Play(const std::string& path);

bool Recording();
bool Playing();

}
