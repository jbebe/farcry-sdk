// The player's flashlight, as the Flashlight plugin sends its beam on FCSE's "flashlight.beam" channel.
#pragma once

namespace DifficultyOverhaul::Flashlight {

struct Lamp {
    bool lit = false;
    // Whether the head sits inside the beam, and how far it is from the lamp.
    bool inBeam = false;
    float distance = 0.0f;
};

// The lamp as a soldier whose head is at `head` has it; unlit while no beam has come.
Lamp Seen(const float* head);

}
