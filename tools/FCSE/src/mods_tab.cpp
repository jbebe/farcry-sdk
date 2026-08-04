#include "mods_tab.h"

#include "dunia_api.h"
#include "hook.h"
#include "log.h"
#include "menu_handler.h"
#include "mod_page.h"
#include "mods_registry.h"

#include <cstdint>
#include <string>
#include <vector>

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

    std::wstring WidenAscii(const std::string& s) {
        std::wstring wide;
        wide.reserve(s.size());
        for (char c : s) {
            wide.push_back(static_cast<wchar_t>(static_cast<unsigned char>(c)));
        }
        return wide;
    }

    // Owns every label buffer ever handed to AddButton. Never freed/shrunk: AddButton's real
    // parameter type is a raw wchar_t* (confirmed against FarCry2_server's demangled signature),
    // and it's not confirmed whether Dunia.dll copies the string internally the way the engine's
    // own CStringTableMgr::Localize results are - safer to keep every buffer alive for the rest of
    // the process than risk the engine holding a dangling pointer.
    std::vector<std::wstring>& LabelStorage() {
        static std::vector<std::wstring> storage;
        return storage;
    }

    void AppendModsRows(void* optionsMenuThis) {
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

        size_t rowCount = 0;
        for (const ModsRegistry::Page& page : ModsRegistry::Pages()) {
            for (const FCSE_ConfigBool& field : page.fields) {
                std::wstring label = WidenAscii(page.pluginName) + L": " +
                                      WidenAscii(field.label != nullptr ? field.label : "?") +
                                      ((field.value != nullptr && *field.value) ? L" [ON]" : L" [OFF]");
                LabelStorage().push_back(std::move(label));
                const wchar_t* labelPtr = LabelStorage().back().c_str();

                ModsMenuHandler* handler = ModsMenuHandler::Create(const_cast<FCSE_ConfigBool*>(&field));
                g_addButton(optionsMenuThis, labelPtr, /*visible=*/1, handler);
                ++rowCount;
            }
        }

        Log::Loader("Mods tab: appended " + std::to_string(rowCount) + " row(s) to Options");

        // Experimental: also register the hand-rolled CGameMenu page and its own button - see
        // mod_page.h. Intentionally separate from the per-plugin bool rows above (different
        // mechanism entirely: a real page-switch, not just more AddButton rows on this screen).
        ModPage::Install(optionsMenuThis);
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
        AppendModsRows(optionsMenuThis);
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
