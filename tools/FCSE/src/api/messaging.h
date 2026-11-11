#pragma once

#include "fcse_api.h"

#include <cstdint>

namespace FCSE {

// Backs FCSE_PluginAPI::Listen and Send. `caller` is the return address the plugin API's shims
// capture, resolved to the plugin's name only when a log line or a listener needs it.
class Messaging {
public:
    static bool Listen(void* caller, const char* channel, FCSE_MessageFn onMessage, void* userdata);
    static uint32_t Send(void* caller, const char* channel, const void* data, size_t size);

    // Forgets every listener, for the tests.
    static void Reset();
};

}
