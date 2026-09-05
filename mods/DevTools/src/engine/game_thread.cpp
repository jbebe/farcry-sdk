// The engine's frame, as somewhere to run code.
//
// CCryEngine::Update is the body of one frame; which function that is and why it is the right seam
// is in docs/docs/engine-internals/developer-console.md. FCSE's Lua tick hooks CXGame::Update one
// level above, so the two never contend for an address.
#include "engine/game_thread.h"

#include "fcse_api.h"

#include <cstdint>
#include <cstdio>
#include <mutex>
#include <utility>
#include <vector>
#include <windows.h>

namespace {
    // __thiscall with one stack argument, declared __fastcall because MSVC will not let a free
    // function be __thiscall. For a method taking one stack argument the two are the same ABI:
    // ECX carries `this`, EDX is unused, and the callee cleans 4 bytes.
    using UpdateFn = void(__fastcall*)(void* self, void* unused, uint32_t flags);

    FCSE::Relocation<UpdateFn> g_update{FCSE::Uplay(0x004CFC90)};
    UpdateFn g_originalUpdate = nullptr;

    std::mutex g_queueLock;
    std::vector<std::function<void()>> g_pending;

    DWORD g_threadId = 0;
    bool g_draining = false;

    void __fastcall UpdateDetour(void* self, void* unused, uint32_t flags) {
        // Engine first, so a job observes the frame the engine has finished rather than a
        // half-applied one, and a slow job cannot reorder engine work.
        g_originalUpdate(self, unused, flags);

        g_threadId = GetCurrentThreadId();

        // A job asked for something that pumped a frame of its own. Whatever it queued waits for
        // the outer one to finish rather than running inside the engine call it is nested in.
        if (g_draining) {
            return;
        }

        std::vector<std::function<void()>> ready;
        {
            std::lock_guard<std::mutex> held(g_queueLock);
            ready.swap(g_pending);
        }

        g_draining = true;
        for (const std::function<void()>& job : ready) {
            job();
        }
        g_draining = false;
    }
}

namespace DevTools::GameThread {

bool Install() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!g_update) {
        api->Log("game thread: the engine's frame update was not found in this build - nothing "
                 "queued will ever run");
        return false;
    }

    // A rejected hook is already logged by FCSE, naming the plugin that owns the address.
    if (!api->Hook(reinterpret_cast<void*>(g_update.address()),
                   reinterpret_cast<void*>(&UpdateDetour),
                   reinterpret_cast<void**>(&g_originalUpdate))) {
        return false;
    }

    char line[128];
    std::snprintf(line, sizeof(line), "game thread: hooked the frame update at 0x%08zX",
                  static_cast<size_t>(g_update.address()));
    api->Log(line);
    return true;
}

bool IsCurrent() { return GetCurrentThreadId() == g_threadId; }

void Post(std::function<void()> job) {
    // Install already said there is no frame; repeating it per job would only fill the log.
    if (g_originalUpdate == nullptr) {
        return;
    }

    std::lock_guard<std::mutex> held(g_queueLock);
    g_pending.push_back(std::move(job));
}

}
