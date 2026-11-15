#pragma once

#include "fcse_api.h"

#include <cstdint>

namespace FCSE {

// Backs FCSE_PluginAPI::Listen and Send.
class Messaging {
public:
    static bool Listen(const char* channel, FCSE_MessageFn onMessage, void* userdata);
    static uint32_t Send(const char* channel, const void* data, size_t size);

    // Forgets every listener.
    static void ResetForTesting();
};

}
