// The finished frame as a picture, taken from the back buffer before the overlay draws on it.
#pragma once

#include <cstdint>
#include <future>
#include <string>
#include <vector>

struct IDirect3DDevice9;

namespace DevTools::FrameCapture {

// One frame, top row first, 4 bytes a pixel in blue, green, red, unused order. `error` says why there
// is none.
struct Image {
    int width = 0;
    int height = 0;
    std::vector<uint8_t> pixels;
    std::string error;
};

// The next frame presented. Safe from any thread. A second call before that frame replaces the first,
// whose future then reports a broken promise.
std::future<Image> Next();

// Called by the renderer from Present.
void OnPresent(IDirect3DDevice9* device);

// Writes `image` as a PNG at `path`, UTF-8. Needs COM on the calling thread. Empty, or why it failed.
std::string SavePng(const Image& image, const std::string& path);

}
