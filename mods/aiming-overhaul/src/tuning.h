// Aiming Overhaul's window in DevTools' overlay: sliders whose values are kept in
// aiming-overhaul.ini beside fcse.ini, and read back from it at launch.
#pragma once

#include <atomic>

namespace AimingOverhaul::Tuning {

// A value the window sets, holding its default until the file is read.
struct Slider {
    const char* key;
    const char* label;
    float min;
    float max;
    const char* format;
    std::atomic<float>* value;
};

// Reads the sliders' values from the file.
void Load();

// The window's contents, for DevTools' overlay.
void DrawWindow(void*);

}
