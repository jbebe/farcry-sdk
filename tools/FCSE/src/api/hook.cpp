#include "api/hook.h"

#include "caller_identity.h"
#include "log.h"

#include <safetyhook.hpp>

#include <cstddef>
#include <cstdio>
#include <intrin.h>
#include <string>
#include <unordered_map>

namespace FCSE {

static_assert(sizeof(FCSE_MidHookContext) == sizeof(safetyhook::Context32));
static_assert(offsetof(FCSE_MidHookContext, xmm7) == offsetof(safetyhook::Context32, xmm7));
static_assert(offsetof(FCSE_MidHookContext, eflags) == offsetof(safetyhook::Context32, eflags));
static_assert(offsetof(FCSE_MidHookContext, eax) == offsetof(safetyhook::Context32, eax));
static_assert(offsetof(FCSE_MidHookContext, esp) == offsetof(safetyhook::Context32, esp));
static_assert(offsetof(FCSE_MidHookContext, eip) == offsetof(safetyhook::Context32, eip));

namespace {
    // Exactly one of the two hooks is populated.
    struct Owned {
        std::string owner;
        SafetyHookInline inlineHook;
        SafetyHookMid midHook;
    };

    std::unordered_map<void*, Owned> g_hooks;

    // Both hook kinds overwrite a 5-byte jump at their target, so two closer than that corrupt
    // each other.
    constexpr uintptr_t kJumpSize = 5;

    const Owned* FindNearby(void* target) {
        const auto at = reinterpret_cast<uintptr_t>(target);
        for (const auto& [address, owned] : g_hooks) {
            const auto other = reinterpret_cast<uintptr_t>(address);
            if ((at > other ? at - other : other - at) < kJumpSize) {
                return &owned;
            }
        }
        return nullptr;
    }

    bool Claim(const std::string& caller, void* target) {
        if (target == nullptr) {
            Log::Write(caller, "hook requested on a null target, rejected");
            return false;
        }
        if (const Owned* nearby = FindNearby(target)) {
            Log::Write(caller, "Hook conflict at address already owned by '" + nearby->owner +
                                   "', rejected");
            return false;
        }
        return true;
    }

    std::string Describe(const safetyhook::InlineHook::Error& error) {
        using Error = safetyhook::InlineHook::Error;
        const char* what = "unknown error";
        switch (error.type) {
        case Error::BAD_ALLOCATION:
            return "trampoline allocation failed";
        case Error::FAILED_TO_DECODE_INSTRUCTION:
            what = "undecodable instruction";
            break;
        case Error::SHORT_JUMP_IN_TRAMPOLINE:
            what = "short jump inside the relocated instructions";
            break;
        case Error::IP_RELATIVE_INSTRUCTION_OUT_OF_RANGE:
            what = "IP-relative instruction out of range";
            break;
        case Error::UNSUPPORTED_INSTRUCTION_IN_TRAMPOLINE:
            what = "unsupported instruction among the relocated ones";
            break;
        case Error::FAILED_TO_UNPROTECT:
            what = "VirtualProtect failed";
            break;
        case Error::NOT_ENOUGH_SPACE:
            what = "not enough room for the jump";
            break;
        }
        char line[96];
        std::snprintf(line, sizeof(line), "%s at 0x%08zX", what,
                      static_cast<size_t>(reinterpret_cast<uintptr_t>(error.ip)));
        return line;
    }

    std::string Describe(const safetyhook::MidHook::Error& error) {
        return error.type == safetyhook::MidHook::Error::BAD_ALLOCATION
                   ? "stub allocation failed"
                   : Describe(error.inline_hook_error);
    }
}

void HookManager::Shutdown() {
    g_hooks.clear();
}

bool HookManager::Hook(void* target, void* detour, void** original) {
    const std::string caller = ResolveCallerModuleName(_ReturnAddress());
    if (!Claim(caller, target)) {
        return false;
    }

    auto hook = safetyhook::InlineHook::create(target, detour);
    if (!hook) {
        Log::Write(caller, "Hook failed: " + Describe(hook.error()));
        return false;
    }

    *original = hook->original<void*>();
    g_hooks.emplace(target, Owned{caller, std::move(*hook), {}});
    Log::Write(caller, "Hook installed");
    return true;
}

bool HookManager::MidHook(void* target, FCSE_MidHookHandler handler) {
    const std::string caller = ResolveCallerModuleName(_ReturnAddress());
    if (!Claim(caller, target)) {
        return false;
    }

    auto hook = safetyhook::MidHook::create(target, reinterpret_cast<safetyhook::MidHookFn>(handler));
    if (!hook) {
        Log::Write(caller, "MidHook failed: " + Describe(hook.error()));
        return false;
    }

    g_hooks.emplace(target, Owned{caller, {}, std::move(*hook)});
    Log::Write(caller, "Mid-hook installed");
    return true;
}

}
