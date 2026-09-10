// Time measured on the performance counter between one call and the next.
#pragma once

#include <windows.h>

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

// True once for every `period` seconds of the time handed to it.
class Heartbeat {
public:
    explicit constexpr Heartbeat(float period) : m_period(period) {}

    bool Due(float elapsed) {
        m_since += elapsed;
        if (m_since < m_period) {
            return false;
        }
        m_since = 0.0f;
        return true;
    }

private:
    float m_period;
    float m_since = 0.0f;
};

}
