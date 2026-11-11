#include "api/messaging.h"

#include "caller_identity.h"
#include "log.h"

#include <memory>
#include <mutex>
#include <string>
#include <unordered_map>
#include <vector>

namespace FCSE {

namespace {
    struct Listener {
        FCSE_MessageFn onMessage;
        void* userdata;
    };
    using Listeners = std::vector<Listener>;

    // Each channel's listeners, replaced whole by a Listen, so a Send walks the list it took
    // without holding the lock - which is what lets a listener send or listen in turn.
    std::mutex g_lock;
    std::unordered_map<std::string, std::shared_ptr<const Listeners>> g_channels;

    std::shared_ptr<const Listeners> ListenersOf(const char* channel) {
        std::lock_guard lock(g_lock);
        const auto found = g_channels.find(channel);
        return found != g_channels.end() ? found->second : nullptr;
    }
}

bool Messaging::Listen(void* caller, const char* channel, FCSE_MessageFn onMessage, void* userdata) {
    if (channel == nullptr || *channel == '\0' || onMessage == nullptr) {
        Log::FromCaller(caller, "Listen: a channel name and a callback are both needed, refused");
        return false;
    }

    {
        std::lock_guard lock(g_lock);
        auto& listeners = g_channels[channel];
        auto grown = listeners != nullptr ? std::make_shared<Listeners>(*listeners)
                                          : std::make_shared<Listeners>();
        grown->push_back({onMessage, userdata});
        listeners = std::move(grown);
    }
    Log::FromCaller(caller, std::string("listening on '") + channel + "'");
    return true;
}

uint32_t Messaging::Send(void* caller, const char* channel, const void* data, size_t size) {
    if (channel == nullptr) {
        return 0;
    }

    const auto listeners = ListenersOf(channel);
    if (listeners == nullptr) {
        return 0;
    }
    const std::string sender = ResolveCallerModuleName(caller);
    for (const Listener& listener : *listeners) {
        listener.onMessage(sender.c_str(), data, size, listener.userdata);
    }
    return static_cast<uint32_t>(listeners->size());
}

void Messaging::Reset() {
    std::lock_guard lock(g_lock);
    g_channels.clear();
}

}
