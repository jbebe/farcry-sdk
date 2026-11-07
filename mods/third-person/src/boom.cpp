// The camera's boom. See boom.h.
#include "boom.h"

#include "ease.h"
#include "engine/ray.h"

#include <algorithm>

namespace {
    // In metres: the room kept between the camera and what it would hit, and the shortest boom.
    constexpr float kClearance = 0.3f;
    constexpr float kShortest = 0.3f;
    // The lags, in seconds, of the boom shortening and of it lengthening again.
    constexpr float kInLag = 1.0f / 30.0f;
    constexpr float kOutLag = 1.0f / 4.0f;
}

float ThirdPerson::Boom::Follow(const float from[3], const float direction[3], float length, float seconds) {
    const float shortest = (std::min)(kShortest, length);
    const float room = Ray::Cast(from, direction, length + kClearance, Ray::Layers::Ground) - kClearance;
    const float cut = length - std::clamp(room, shortest, length);
    m_cut = m_cut < 0.0f ? cut : Ease(m_cut, cut, seconds, cut > m_cut ? kInLag : kOutLag);
    return (std::max)(length - m_cut, shortest);
}
