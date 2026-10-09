// Fast loading.
//
// Removes the fixed waits a save load spends doing nothing; what each one waits on is in
// docs/docs/engine-internals/loading-a-save.md. The sites, Steam / GOG:
//
// - SetPollInterval (0x102B9E50 / 0x102B83C0) stores 0, so the request thread stops sleeping.
// - The shader precache call (0x10345F97 / 0x10338377) is handed a budget of 1,000.
// - Leaving the renderer's sub-state 3 (0x1079D2FC / 0x10790B1C) skips the second camera pass.
// - Session delete's offline branch (0x102FD6C8 / 0x102F5888) zeroes its one-second linger.
// - Continue's 1.5 s comparison (0x108C9410 / 0x108B8BA0) is pointed at a zero.
// - Dunia's XInputGetState import asks an absent pad again only every two seconds.
#include "engine/imports.h"
#include "fcse_api.h"

#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include <algorithm>
#include <cstdint>
#include <cstring>
#include <vector>

namespace {
    constexpr uintptr_t kShaderBudget = 1000;
    constexpr ULONGLONG kAbsentPadRetryMs = 2000;
    constexpr DWORD kPads = 4;
    constexpr DWORD kRequestThreadCores = 4;

    FCSE::Relocation<uint8_t*> g_pollSetter{
        FCSE::Pattern("8B 44 24 04 89 81 38 01 00 00 C2 04 00 CC CC CC "
                      "8B 81 34 01 00 00 89 81 38 01 00 00 C3")};
    FCSE::Relocation<uint8_t*> g_precache{
        FCSE::Pattern("8B 4E 2C 8B 89 64 01 00 00 8B 56 2C 8B 82 68 01 00 00 8B 7F 78 3B C7 77 02 "
                      "8B C7 50 51 8B 0D ?? ?? ?? ?? E8")};
    FCSE::Relocation<uint8_t*> g_secondPass{
        FCSE::Pattern("80 BD 90 00 00 00 00 75 ?? B8 01 00 00 00 89 45 48 89 44 24 14 E8")};
    FCSE::Relocation<uint8_t*> g_sessionStart{
        FCSE::Pattern("0F 95 C3 85 C0 74 ?? 83 40 08 FF 8D 48 04 75 ?? 8B 01 8B 50 04 FF D2 84 DB "
                      "0F 84")};
    FCSE::Relocation<uint8_t*> g_continueWait{
        FCSE::Pattern("F3 0F 10 86 A0 01 00 00 0F 2F 05 ?? ?? ?? ?? 0F 86 ?? ?? ?? ?? 8B 0D")};

    // Each pattern's offset to the bytes it is there for.
    constexpr size_t kPrecachePush = 27;
    constexpr size_t kSecondPassState = 10;
    constexpr size_t kSessionTest = 23;
    constexpr size_t kContinueOperand = 11;

    const float kNoWait = 0.0f;

    // A few bytes written while the option is on and put back while it is off.
    struct BytePatch {
        uint8_t* site;
        uint8_t original[4];
        uint8_t patched[4];
        size_t size;
    };

    std::vector<BytePatch> g_patches;
    bool g_on = false;

    void AddPatch(uint8_t* site, const void* patched, size_t size) {
        BytePatch patch{site, {}, {}, size};
        std::memcpy(patch.original, site, size);
        std::memcpy(patch.patched, patched, size);
        g_patches.push_back(patch);
    }

    // The precache's two counts are pushed next: how many programs to walk in EAX, the create
    // budget in ECX.
    void RaiseShaderBudget(FCSE_MidHookContext* ctx) {
        if (g_on) {
            ctx->eax = std::max<uintptr_t>(ctx->eax, kShaderBudget);
            ctx->ecx = std::max<uintptr_t>(ctx->ecx, kShaderBudget);
        }
    }

    // BL is clear when the session is offline, and ESI is the delete operation.
    void SkipOfflineLinger(FCSE_MidHookContext* ctx) {
        constexpr uintptr_t kLinger = 0x64;
        if (g_on && (ctx->ebx & 0xFF) == 0) {
            *reinterpret_cast<float*>(ctx->esi + kLinger) = 0.0f;
        }
    }

    using XInputGetStateFn = DWORD(WINAPI*)(DWORD userIndex, void* state);
    XInputGetStateFn g_originalXInputGetState = nullptr;
    ULONGLONG g_padRetryAt[kPads]{};

    DWORD WINAPI XInputGetStateDetour(DWORD userIndex, void* state) {
        if (!g_on || userIndex >= kPads) {
            return g_originalXInputGetState(userIndex, state);
        }

        const ULONGLONG now = GetTickCount64();
        if (now < g_padRetryAt[userIndex]) {
            return ERROR_DEVICE_NOT_CONNECTED;
        }

        const DWORD result = g_originalXInputGetState(userIndex, state);
        g_padRetryAt[userIndex] = result == ERROR_DEVICE_NOT_CONNECTED ? now + kAbsentPadRetryMs : 0;
        return result;
    }
}

// Finds every site and installs the hooks, which do nothing while the option is off. A part whose
// site is missing is left as shipped and the rest still apply.
void InstallFastLoadingHooks() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    SYSTEM_INFO system{};
    GetSystemInfo(&system);
    if (!g_pollSetter) {
        api->Log("fast loading: the request thread's poll setter was not found - it keeps its wait");
    } else if (system.dwNumberOfProcessors < kRequestThreadCores) {
        api->Log("fast loading: fewer than four processors - the request thread keeps its wait");
    } else {
        constexpr uint8_t kStoreZero[] = {0x33, 0xC0, 0x90, 0x90};
        AddPatch(g_pollSetter.get(), kStoreZero, sizeof(kStoreZero));
    }

    // The second pass is only safe to skip when the budget has left nothing pending.
    if (!g_precache || !api->MidHook(g_precache.get() + kPrecachePush, &RaiseShaderBudget)) {
        api->Log("fast loading: the shader precache could not be hooked - shaders load as shipped");
    } else if (!g_secondPass) {
        api->Log("fast loading: the second camera pass was not found - it still runs");
    } else {
        constexpr uint8_t kAmbient = 4;
        AddPatch(g_secondPass.get() + kSecondPassState, &kAmbient, sizeof(kAmbient));
    }

    if (!g_sessionStart || !api->MidHook(g_sessionStart.get() + kSessionTest, &SkipOfflineLinger)) {
        api->Log("fast loading: the session delete could not be hooked - it keeps its linger");
    }

    if (g_continueWait) {
        const float* zero = &kNoWait;
        AddPatch(g_continueWait.get() + kContinueOperand, &zero, sizeof(zero));
    } else {
        api->Log("fast loading: the Continue wait was not found - it keeps its 1.5 s");
    }

    if (!DevTools::Imports::Hook(api->duniaModule, "XINPUT1_3.dll", MAKEINTRESOURCEA(2),
                                 &XInputGetStateDetour,
                                 reinterpret_cast<void**>(&g_originalXInputGetState))) {
        api->Log("fast loading: Dunia's XInputGetState import was not found - pads poll as shipped");
    }
}

int GetFastLoading() { return g_on ? 1 : 0; }

void SetFastLoading(int value) {
    if ((value != 0) == g_on) {
        return;
    }

    g_on = value != 0;
    const FCSE_PluginAPI* api = FCSE::ApiPointer();
    for (const BytePatch& patch : g_patches) {
        api->Patch(patch.site, g_on ? patch.patched : patch.original, patch.size);
    }

    api->Log(g_on ? "fast loading: on - saves load without the shipped waits"
                  : "fast loading: off - saves load as shipped");
}
