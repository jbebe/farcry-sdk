// CCryEngine::Update, the body of one frame, run on the game thread.
#include "engine/game_thread.h"

#include "engine/memory.h"
#include "fcse_api.h"

#include <atomic>
#include <windows.h>

namespace InventoryOverhaul::GameThread {

namespace {
    using UpdateFn = void(__fastcall*)(void* engine, void* unused, uint32_t flags);

    FCSE::Relocation<UpdateFn> g_updateSite{FCSE::Uplay(0x004CFC90)};
    UpdateFn g_update = nullptr;
    std::atomic<DWORD> g_threadId{0};

    void __fastcall OnUpdate(void* engine, void* unused, uint32_t flags) {
        g_threadId = GetCurrentThreadId();
        g_update(engine, unused, flags);
    }
}

bool Install() { return Hook(g_updateSite, &OnUpdate, &g_update); }

bool IsCurrent() { return GetCurrentThreadId() == g_threadId; }

}
