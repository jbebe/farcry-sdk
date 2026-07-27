#include "hook.h"

#include "caller_identity.h"
#include "log.h"

#include <MinHook.h>

#include <unordered_map>

namespace FCSE {

namespace {
    std::unordered_map<void*, std::string> g_owners; // target address -> owning plugin name
}

bool HookManager::Initialize() {
    MH_STATUS status = MH_Initialize();
    if (status != MH_OK) {
        Log::Loader(std::string("MH_Initialize failed: ") + MH_StatusToString(status));
        return false;
    }
    return true;
}

void HookManager::Shutdown() {
    MH_Uninitialize();
}

bool HookManager::Hook(void* target, void* detour, void** original) {
    std::string caller = ResolveCallerModuleName(_ReturnAddress());

    if (target == nullptr) {
        Log::FromCaller(_ReturnAddress(), "Hook() called with a null target, rejected");
        return false;
    }

    auto existing = g_owners.find(target);
    if (existing != g_owners.end()) {
        Log::FromCaller(_ReturnAddress(),
                         "Hook conflict at address already owned by '" + existing->second +
                             "', rejected");
        return false;
    }

    MH_STATUS status = MH_CreateHook(target, detour, original);
    if (status != MH_OK) {
        Log::FromCaller(_ReturnAddress(),
                         std::string("MH_CreateHook failed: ") + MH_StatusToString(status));
        return false;
    }

    status = MH_EnableHook(target);
    if (status != MH_OK) {
        Log::FromCaller(_ReturnAddress(),
                         std::string("MH_EnableHook failed: ") + MH_StatusToString(status));
        MH_RemoveHook(target);
        return false;
    }

    g_owners[target] = caller;
    Log::FromCaller(_ReturnAddress(), "Hook installed");
    return true;
}

} // namespace FCSE
