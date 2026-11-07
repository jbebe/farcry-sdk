// A camera held out from a point on a boom that the ground and what stands on it cut short: quickly
// when something comes between, and let back out softly once it is gone.
#pragma once

namespace ThirdPerson {

class Boom {
public:
    // How far out to stand this frame along the unit `direction` from `from`, wanting `length`.
    float Follow(const float from[3], const float direction[3], float length, float seconds);

    // Starts the next frame where the way allows, without easing.
    void Reset() {
        m_cut = -1.0f;
    }

private:
    // How much shorter than wanted the boom stands, eased; negative before the first frame.
    float m_cut = -1.0f;
};

}
