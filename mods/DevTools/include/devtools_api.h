// The contract for adding a window to DevTools' overlay from another FCSE plugin. DevTools owns the
// frame, the input, and each window's title bar, position and close button; a plugin registers once
// and draws what is inside. Both sides build the Dear ImGui pinned in devtools_imgui.cmake and share
// one ImGuiContext. A C++ plugin needs only DevTools::Overlay::AddWindow and PostLine, at the bottom.
#pragma once

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

// Bumped by any change to the structs or signatures below.
#define DEVTOOLS_OVERLAY_API_VERSION 2

// A module's Dear ImGui as another module sees it: the version, and the size of each struct the two
// share. A window whose layout differs from DevTools' own is refused.
typedef struct DevTools_ImGuiLayout {
    uint32_t versionNum;
    uint32_t sizeOfIO;
    uint32_t sizeOfStyle;
    uint32_t sizeOfVec2;
    uint32_t sizeOfVec4;
    uint32_t sizeOfDrawVert;
    uint32_t sizeOfDrawIdx;
    uint32_t sizeOfContext;
} DevTools_ImGuiLayout;

// DevTools' context and allocator.
typedef struct DevTools_ImGuiBinding {
    // An ImGuiContext*.
    void* context;
    void* (*MemAlloc)(size_t size, void* userData);
    void (*MemFree)(void* ptr, void* userData);
    void* memUserData;
} DevTools_ImGuiBinding;

// Points the plugin's own copy of Dear ImGui at DevTools' context and allocator, since ImGui's globals
// and heap are per module. DevTools calls it before every draw.
typedef void (*DevTools_BindImGuiFn)(const DevTools_ImGuiBinding* imgui);

// Draws a window's contents, between DevTools' Begin and End for it. Runs on the thread that presents
// the frame, and only while the overlay and this window are both open.
typedef void (*DevTools_DrawWindowFn)(void* userData);

// Adds a window after every window added before it, `width` by `height` until it is resized. Valid
// from FCSE_OnRegisterFunctions on, from any thread. The title is copied and has to be unique in the
// overlay, and a window cannot be removed. Returns false, with the reason in fcse.log, if the window
// will never be drawn.
typedef bool (*DevTools_AddWindowFn)(const DevTools_ImGuiLayout* imgui, DevTools_BindImGuiFn bind,
                                     const char* title, float width, float height,
                                     DevTools_DrawWindowFn draw, void* userData);

// Runs one line in Far Cry 2's console as if it had been typed, `#` escaping to Lua and developer-only
// commands included. The line is copied and runs at the end of the game's next frame, so it can be
// posted from any thread, a window's draw included.
typedef void (*DevTools_PostLineFn)(const char* line);

typedef struct DevTools_OverlayAPI {
    // Always DEVTOOLS_OVERLAY_API_VERSION for this layout - compare before using anything below.
    uint32_t apiVersion;
    DevTools_AddWindowFn AddWindow;
    DevTools_PostLineFn PostLine;
} DevTools_OverlayAPI;

// DevTools.dll exports one function of this type, named DevTools_GetOverlayAPI.
typedef const DevTools_OverlayAPI* (*DevTools_GetOverlayAPIFn)(void);

#ifdef __cplusplus
}

#include "fcse_api.h"
#include "imgui.h"
#include "imgui_internal.h"

namespace DevTools::Overlay {

// This module's Dear ImGui, described for DevTools to compare against its own.
inline DevTools_ImGuiLayout ImGuiLayout() {
    return {IMGUI_VERSION_NUM, sizeof(ImGuiIO),    sizeof(ImGuiStyle), sizeof(ImVec2),
            sizeof(ImVec4),    sizeof(ImDrawVert), sizeof(ImDrawIdx),  sizeof(ImGuiContext)};
}

// This module's DevTools_BindImGuiFn.
inline void BindImGui(const DevTools_ImGuiBinding* imgui) {
    ImGui::SetCurrentContext(static_cast<ImGuiContext*>(imgui->context));
    ImGui::SetAllocatorFunctions(imgui->MemAlloc, imgui->MemFree, imgui->memUserData);
}

// DevTools' overlay API, or null when DevTools.dll is not loaded.
inline const DevTools_OverlayAPI* FindOverlay() {
    HMODULE devTools = GetModuleHandleW(L"DevTools.dll");
    auto getOverlay = devTools == nullptr
                          ? nullptr
                          : reinterpret_cast<DevTools_GetOverlayAPIFn>(
                                GetProcAddress(devTools, "DevTools_GetOverlayAPI"));
    return getOverlay == nullptr ? nullptr : getOverlay();
}

// DevTools_AddWindowFn for this module, once DevTools.dll is found and its API version matches.
// Either failure is logged through FCSE::Logf, so call FCSE::Bind in FCSE_Load first.
inline bool AddWindow(const char* title, float width, float height, DevTools_DrawWindowFn draw,
                      void* userData = nullptr) {
    const DevTools_OverlayAPI* overlay = FindOverlay();
    if (overlay == nullptr) {
        FCSE::Logf("DevTools is not installed - the '%s' window is not drawn", title);
        return false;
    }

    if (overlay->apiVersion != DEVTOOLS_OVERLAY_API_VERSION) {
        FCSE::Logf("DevTools' overlay API is version %u and this plugin was built for %d - the "
                   "'%s' window is not drawn",
                   overlay->apiVersion, DEVTOOLS_OVERLAY_API_VERSION, title);
        return false;
    }

    const DevTools_ImGuiLayout layout = ImGuiLayout();
    return overlay->AddWindow(&layout, &BindImGui, title, width, height, draw, userData);
}

// DevTools_PostLineFn, once DevTools.dll is found and its API version matches, and nothing otherwise.
inline void PostLine(const char* line) {
    const DevTools_OverlayAPI* overlay = FindOverlay();
    if (overlay != nullptr && overlay->apiVersion == DEVTOOLS_OVERLAY_API_VERSION) {
        overlay->PostLine(line);
    }
}

}

#endif
