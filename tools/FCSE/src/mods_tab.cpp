#include "mods_tab.h"

#include "dunia_api.h"
#include "fcse_page.h"
#include "hook.h"
#include "log.h"
#include "magma_package.h"

#include <cstdint>

namespace FCSE {

namespace {
    // Dunia.dll (Steam v1.03) RVAs, relative to the DLL's preferred image base - same
    // base-plus-RVA convention stock_constants.cpp uses for FarCry2.exe.
    //
    // kOptionsMenuRva = 0x1081aee0 - confirmed (via disassembly, not just decompile) to be the
    // real "build Options' row of category buttons" function: takes exactly one implicit arg
    // (this, via ECX, __fastcall/__thiscall-with-no-stack-args - confirmed by the trailing plain
    // RET with no stack-cleanup immediate), invoked only via a data/vtable xref (never a direct
    // CALL anywhere) - i.e. genuine virtual dispatch, fired lazily whenever Options is actually
    // shown. Its body reloads ECX = its own saved `this` immediately before every one of its 5
    // AddButton calls - structurally identical to BuildMainMenu's own proven-safe use of the same
    // pattern. This supersedes an earlier, wrong hook target (0x1084fa90): that function fires
    // eagerly before intro videos even play and is NOT this one - see the plan file for the full
    // trail of both the wrong hook and how this one was found.
    //
    // kAddButtonRva = 0x10cdbb80 - the real "add one row" call. Confirmed unsafe when called with
    // CFCXOptionPage's own pointer as `this` (crashes regardless of label/handler content) but
    // confirmed SAFE when called with kOptionsMenuRva's own `this` (exactly what that function
    // itself does, 5 times, successfully, every time Options opens today).
    constexpr uintptr_t kDuniaPreferredBase = 0x10000000;
    constexpr uintptr_t kOptionsMenuRva = 0x1081aee0;
    constexpr uintptr_t kAddButtonRva = 0x10cdbb80;

    using OptionsMenuFn = void(__thiscall*)(void* thisPtr);
    using AddButtonFn = void*(__thiscall*)(void* thisPtr, const wchar_t* label, char visible,
                                            void* handler);

    OptionsMenuFn g_originalOptionsMenu = nullptr;
    AddButtonFn g_addButton = nullptr;
    bool g_appended = false; // guards against double-appending if this ever runs more than once

    void AppendModConfigurationMenu(void* optionsMenuThis) {
        if (g_appended) {
            Log::Loader("Mods tab: options menu built again - skipping re-append (see "
                        "mods_tab.cpp's g_appended guard)");
            return;
        }
        g_appended = true;

        if (g_addButton == nullptr) {
            Log::Loader("Mods tab: AddButton address unavailable, cannot append rows");
            return;
        }

        // Load FCSE's own page layout. This is the right moment: the Options screen is built
        // lazily, well after common.mgb is up, which is what the layout's PageInstances point into.
        // Failure is not fatal - it just means no private page this run - so it is logged and the
        // existing shared-Game-tab mechanism below carries on unchanged.
        MagmaPackage::Load();

        // Plugin config registrations are intentionally not rendered here. Options gets one
        // navigation row only; the page opened by it is the configuration surface.
        FcsePage::Install(optionsMenuThis);
    }

    // MSVC won't let a free function be declared __thiscall (only real member functions), but the
    // hook target genuinely is __thiscall-with-no-stack-args (this in ECX only) - so the detour
    // itself has to be a real, ordinary member function too. This throwaway class exists solely so
    // MSVC compiles Detour() with the right calling convention; `this` here is never actually a
    // real SetupDetourThunk instance, it's whatever the engine's original caller passes as its real
    // `this` (the live options-menu-builder instance) - only ever forwarded through as an opaque
    // void*, never dereferenced as this class's own (nonexistent) state.
    struct SetupDetourThunk {
        void Detour();
    };

    void SetupDetourThunk::Detour() {
        void* optionsMenuThis = reinterpret_cast<void*>(this);
        g_originalOptionsMenu(optionsMenuThis); // real category buttons first, unmodified
        AppendModConfigurationMenu(optionsMenuThis);
    }

    using SetupDetourMemberFn = void (SetupDetourThunk::*)();

    void* RawFunctionPointer(SetupDetourMemberFn fn) {
        union {
            SetupDetourMemberFn member;
            void* raw;
        } converter;
        converter.member = fn;
        return converter.raw;
    }
}

bool ModsTab::Install() {
    uintptr_t base = DuniaApi::Base();
    if (base == 0) {
        Log::Loader("Mods tab: Dunia.dll not resolved yet, cannot install options-menu hook");
        return false;
    }

    void* optionsMenuTarget =
        reinterpret_cast<void*>(base + (kOptionsMenuRva - kDuniaPreferredBase));
    g_addButton = reinterpret_cast<AddButtonFn>(base + (kAddButtonRva - kDuniaPreferredBase));

    if (!HookManager::Hook(optionsMenuTarget, RawFunctionPointer(&SetupDetourThunk::Detour),
                            reinterpret_cast<void**>(&g_originalOptionsMenu))) {
        Log::Loader("Mods tab: failed to install options-menu hook - no \"Mods\" tab this run");
        return false;
    }

    Log::Loader("Mods tab: options-menu hook installed");
    return true;
}

} // namespace FCSE
