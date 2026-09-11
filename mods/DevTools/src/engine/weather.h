// The environment manager's storm, rain and wind, either forced or left to the engine.
#pragma once

namespace DevTools::Weather {

enum class Mode { Engine, Off, Force };

// What to force. Off is no storm, no rain and still air; the values only apply to Force.
struct Wanted {
    Mode storm = Mode::Engine;
    float stormStrength = 1.0f;
    Mode rain = Mode::Engine;
    Mode wind = Mode::Engine;
    float windForce = 60.0f;
    float windDegrees = 0.0f;
};

// The manager as the last world frame left it; nothing is live until a world has updated once.
struct Snapshot {
    bool live = false;

    float storm = 0.0f;
    // The curve's factor, already damped by the desert.
    float curveStorm = 0.0f;
    float stormHour = 0.0f;
    float desert = 0.0f;
    float overrideValue = 0.0f;
    float overrideWeight = 0.0f;

    bool raining = false;
    // Raining, with no rain component on the player to show it.
    bool rainUnseen = false;
    float rainIntensity = 0.0f;
    float cloudCover = 0.0f;
    float rollThreshold = 0.0f;
    // Real seconds to the next rain roll, or below zero while the cover is too thin to roll.
    float nextRoll = -1.0f;

    float windForce = 0.0f;
    float windDegrees = 0.0f;
};

// Hooks the manager's rain and wind updates. A site not found is logged, and that weather stays the
// engine's.
void Install();

// Safe from any thread; takes effect on the engine's next frame.
void Set(const Wanted& wanted);

Snapshot Read();

}
