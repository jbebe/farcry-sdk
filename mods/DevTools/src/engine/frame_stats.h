// The game's frames as they reach the screen: how long each took, and how long the GPU spent on it.
//
// Everything here is called and read on the thread that presents, which is also where the overlay
// draws, so none of it is locked.
#pragma once

#include <limits>

struct IDirect3DDevice9;

namespace DevTools::FrameStats {

// A value nothing measured.
inline constexpr float kUnmeasured = std::numeric_limits<float>::quiet_NaN();

// One presented frame. A value not measured this frame is NaN: the first frame has no interval, and
// the GPU's time arrives a few frames late, or never on a driver without timestamp queries.
struct Frame {
    float ms;
    float gpuMs;
};

// The swap chain the frames are presented through, as of the last device reset.
struct Display {
    bool known = false;
    unsigned width = 0;
    unsigned height = 0;
    bool vsync = false;
};

// The newest few hundred values of one series, oldest at `offset`, as ImGui::PlotLines takes them.
struct History {
    const float* values;
    int count;
    int offset;
};

// Call as Present is entered, before anything of ours is drawn. Returns the frame just ended.
Frame Presenting();

// Call once Present has returned; the next frame starts here.
void Presented(IDirect3DDevice9* device);

// Call before the device resets. Every query goes, and the next frame builds them again.
void DeviceLost();

History FrameTimes();
History GpuTimes();
Display ReadDisplay();

// False once the device has refused a timestamp query; the GPU is never timed after that.
bool GpuTimed();

}
