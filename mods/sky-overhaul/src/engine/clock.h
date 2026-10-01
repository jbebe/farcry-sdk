// Time measured on the performance counter between one call and the next.
#pragma once

#include <windows.h>

#include <cmath>

namespace SkyOverhaul {

// Seconds since the previous Lap, and zero on the first.
class Stopwatch {
public:
    float Lap() {
        LARGE_INTEGER now;
        LARGE_INTEGER frequency;
        QueryPerformanceCounter(&now);
        QueryPerformanceFrequency(&frequency);
        const float seconds =
            m_last.QuadPart == 0
                ? 0.0f
                : static_cast<float>(static_cast<double>(now.QuadPart - m_last.QuadPart) /
                                     static_cast<double>(frequency.QuadPart));
        m_last = now;
        return seconds;
    }

private:
    LARGE_INTEGER m_last = {};
};

// Which of a grain's patterns shows, stepping at film's 24 a second through a cycle of 256.
class GrainClock {
public:
    // Moves on by the time since the previous call.
    void Advance() {
        m_time = std::fmod(m_time + m_stopwatch.Lap(), kSteps / kHertz);
    }

    float Pattern() const {
        return std::floor(m_time * kHertz);
    }

private:
    static constexpr float kHertz = 24.0f;
    static constexpr float kSteps = 256.0f;

    Stopwatch m_stopwatch;
    float m_time = 0.0f;
};

}
