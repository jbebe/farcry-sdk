#include "ui/mods_tab.h"

#include "engine/address_library.h"
#include "engine/address_symbols.h"
#include "engine/dunia_api.h"
#include "ui/fcse_page.h"
#include "api/hook.h"
#include "log.h"
#include "ui/magma_package.h"

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
    // The row itself is added by fcse_page.cpp, which resolves CListMenuPage::AddButton for
    // itself - this file only owns the hook and the `this` to hand it.
    using OptionsMenuFn = void(__thiscall*)(void* thisPtr);

    OptionsMenuFn g_originalOptionsMenu = nullptr;

    // The engine builds one CFCXOptionPage per game state, not one for the whole game.
    // CFCXGRStateMain::Init (main menu), CFCXGRStatePause::Init (single-player pause) and
    // CFCXPauseMultiService::DoInit (multiplayer pause) each call CGameMenu::AddPage<CFCXOptionPage>
    // against their own embedded CGameMenu, and each builds the identical Options subtree
    // (Game/Display/Sound/Network + Controller). All three instances share this one Setup, which runs
    // once per instance, lazily the first time that state's Options screen is shown - so the hook
    // fires three times per session, once per screen.
    //
    // This used to be gated on a single bool, which let whichever screen was shown first have the row
    // and silently denied it to the other two: exactly why the two pause menus had no Mod
    // Configuration Menu entry.
    //
    // Nothing is remembered about which pages have already been served, deliberately. Setup runs
    // exactly once per page instance and the engine's own correctness depends on that - it is a
    // one-time builder that allocates every button's handler with raw CMemMng::NMalloc and never
    // frees it, and a second run would give the player a second set of stock category buttons. So
    // "once per Setup call" already means "once per page", and appending unconditionally here yields
    // exactly one row per screen.
    //
    // The tempting alternative - remember the pages already appended to, and skip the ones seen
    // before - is unsound, because these pages do not outlive their state. Each state's CGameMenu is
    // destroyed with the state (CGameMenu::~CGameMenu tears its page table down) and the pages it
    // owned are abandoned, so a later state's CMemMng::NMalloc(300) may well hand back the address a
    // dead page used to occupy. A remembered pointer would then match a different, live page and
    // silently deny it the row: the original bug, returned, and now dependent on allocator behaviour
    // rather than reproducible.
    void AppendModConfigurationMenu(void* optionsMenuThis) {
        // Load FCSE's own page layout. This is the right moment: the Options screen is built
        // lazily, well after common.mgb is up, which is what the layout's PageInstances point into.
        // Failure is not fatal - it just means no private page this run - so it is logged and the
        // existing shared-Game-tab mechanism below carries on unchanged. Reached once per Options
        // screen now rather than once per session, which is safe: Load() does its work on the first
        // call and returns that same result afterwards.
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
        reinterpret_cast<void*>(AddressLibrary::Address(Symbols::kOptionsMenu));
    if (optionsMenuTarget == nullptr) {
        Log::Loader("Mods tab: the options-menu setup function has no address on this game build "
                    "- no \"Mods\" tab this run");
        return false;
    }

    if (!HookManager::Hook(optionsMenuTarget, RawFunctionPointer(&SetupDetourThunk::Detour),
                            reinterpret_cast<void**>(&g_originalOptionsMenu))) {
        Log::Loader("Mods tab: failed to install options-menu hook - no \"Mods\" tab this run");
        return false;
    }

    Log::Loader("Mods tab: options-menu hook installed");
    return true;
}

} // namespace FCSE
