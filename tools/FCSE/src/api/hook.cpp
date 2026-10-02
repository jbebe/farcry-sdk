#include "api/hook.h"

#include "caller_identity.h"
#include "log.h"

#include <safetyhook.hpp>

#include <algorithm>
#include <cstddef>
#include <cstdio>
#include <intrin.h>
#include <string>
#include <variant>
#include <vector>

namespace FCSE {

static_assert(sizeof(FCSE_MidHookContext) == sizeof(safetyhook::Context32));
static_assert(offsetof(FCSE_MidHookContext, eflags) == offsetof(safetyhook::Context32, eflags));
static_assert(offsetof(FCSE_MidHookContext, eax) == offsetof(safetyhook::Context32, eax));
static_assert(offsetof(FCSE_MidHookContext, eip) == offsetof(safetyhook::Context32, eip));

namespace {
    // One installed hook, and the bytes it displaced at its target.
    struct ClaimedRange {
        uintptr_t start;
        uintptr_t end; // exclusive
        std::string owner;
        std::variant<SafetyHookInline, SafetyHookMid> hook;
    };

    // In install order, so hooks sharing a start appear innermost first.
    std::vector<ClaimedRange> g_claims;

    // A hook relocates whole instructions until they cover its 5-byte jump, so this is the least a
    // new one can displace - the true size is only known once it exists.
    constexpr uintptr_t kMinDisplaced = 5;

    bool RangesOverlap(uintptr_t aStart, uintptr_t aEnd, uintptr_t bStart, uintptr_t bEnd) {
        return aStart < bEnd && bStart < aEnd;
    }

    // A hook at another hook's start stacks on it, since safetyhook relocates that hook's jump like
    // any other instruction; one overlapping its displaced bytes anywhere else is rejected.
    bool Claim(const std::string& caller, void* target) {
        if (target == nullptr) {
            Log::Write(caller, "hook requested on a null target, rejected");
            return false;
        }

        const auto start = reinterpret_cast<uintptr_t>(target);
        for (const ClaimedRange& claim : g_claims) {
            if (claim.start != start &&
                RangesOverlap(start, start + kMinDisplaced, claim.start, claim.end)) {
                Log::Write(caller, "Hook conflict: target overlaps bytes already hooked by '" +
                                       claim.owner + "', rejected");
                return false;
            }
        }
        return true;
    }

    // Keeps an installed hook, logging it as ahead of the newest one at the same start, if any.
    template <class T>
    void Record(const std::string& caller, void* target, T hook, std::string line) {
        const auto start = reinterpret_cast<uintptr_t>(target);
        const auto sameStart = [start](const ClaimedRange& claim) { return claim.start == start; };
        const auto beneath = std::find_if(g_claims.rbegin(), g_claims.rend(), sameStart);
        if (beneath != g_claims.rend()) {
            line += ", ahead of '" + beneath->owner + "'";
        }

        const uintptr_t end = start + hook.original_bytes().size();
        g_claims.push_back({start, end, caller, std::move(hook)});
        Log::Write(caller, line);
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
            what = "short jump among the relocated instructions";
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
                      reinterpret_cast<size_t>(error.ip));
        return line;
    }

    std::string Describe(const safetyhook::MidHook::Error& error) {
        return error.type == safetyhook::MidHook::Error::BAD_ALLOCATION
                   ? "stub allocation failed"
                   : Describe(error.inline_hook_error);
    }
}

void HookManager::Shutdown() {
    // Newest first: each hook puts back the bytes it found, and only the oldest found the original.
    while (!g_claims.empty()) {
        g_claims.pop_back();
    }
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
    Record(caller, target, std::move(*hook), "Hook installed");
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

    Record(caller, target, std::move(*hook), "Mid-hook installed");
    return true;
}

}
