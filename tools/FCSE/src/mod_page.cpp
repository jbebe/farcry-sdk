#include "mod_page.h"

#include "dunia_api.h"
#include "log.h"

#include <cstdint>
#include <cstdio>
#include <windows.h>

// All addresses below are Dunia.dll (Steam v1.03) RVAs, same base-plus-RVA convention
// mods_tab.cpp already uses. See mod_page.h for what this proves and how success is judged, and
// docs/docs/engine-internals/magma-menu-system.md for the full RE trail (GhidraMCP decompiles +
// disassembly against the live Dunia.dll project) each address/offset below was confirmed from.
//
// First live attempt (see the plan file) crashed the game right after the splash screen, with no
// log output at all from this file - meaning it crashed on the very first new memory access
// (optionsMenuThis+0x140), before a single Log::Loader call executed. That access assumed
// "ownerPage" (the pointer CSetNextPageMenuHandler stores at its own +0x8, confirmed via
// disassembly of 0x10188d00) is simply optionsMenuThis itself - never independently verified.
// Every native-pointer touchpoint below is now wrapped in SEH (__try/__except) and logged step by
// step, so a wrong offset guess produces a precise diagnostic instead of another blind crash, and
// the game keeps running either way.
namespace FCSE {

namespace {
    constexpr uintptr_t kDuniaPreferredBase = 0x10000000;

    // CGameMenu's get-or-create hashtable-slot helper. Confirmed via decompile: runs the
    // identical lookup GetPage/SetNextPage use (FUN_101f7a90), and on a miss inserts a fresh
    // empty node for the key before returning a pointer to its value slot - i.e. the same runtime
    // primitive CGameMenu::AddPage<T>() uses internally, minus the compile-time type restriction
    // (see the doc's "Page switching" section). The caller writes its own page pointer into *slot
    // afterwards; this function does not validate or use it.
    //
    // 2026-08-02 live test: this crashed with STATUS_ACCESS_VIOLATION (0xC0000005), caught cleanly
    // by SEH - the *same* crash a 2026-07-31 session already hit and left a full diagnosis for in
    // the shared Ghidra project (this address is already renamed CGameMenu_GetOrCreatePageSlot
    // there, with a comment covering everything below). The live dump this time added one new data
    // point: the candidate CGameMenu's own +0x2c (element count) read back as a small, sane value
    // (7), but +0x28 (bucket mask, used as a bitwise AND in both this function and the read-only
    // Find helper - CGameMenu_PageTable_Find, 0x101f7a90) read back as 0x1851AFD4, nowhere near a
    // power-of-two-minus-one shape a real bucket mask needs to be. A garbage mask this large would
    // produce a wildly out-of-bounds node-array index in InsertNode - matching the crash mechanism
    // exactly. Two live-testable explanations, not yet distinguished: (a) the hashtable genuinely
    // isn't finished initializing yet at the exact moment CFCXOptionPage::Setup fires (this hook's
    // own timing), or (b) a remaining field-offset error specific to the insert/rehash path (which
    // reads +0x14/+0x20/+0x10 in addition to +0x28/+0x2c/+0x1c - the *read-only* Find/GetPage path
    // only ever touches the latter three, which is exactly why real button clicks have never hit
    // this). The `+0x140`-as-owning-CGameMenu assumption itself is independently confirmed correct
    // (matches CSetNextPageMenuHandler::SwitchPage's own disassembly, 0x10188d00) - not a suspect.
    // Do not call this again without first trying the read-only `Find` probe below.
    constexpr uintptr_t kGetOrCreatePageSlotRva = 0x107813e0;

    // CGameMenu_PageTable_Find - the shared, read-only lookup GetPage/SetNextPage already call
    // safely on every real button click, live-tested for years across every player who's ever
    // opened this menu. Same struct, same fields (+0x1c/+0x28/+0x2c), a strict subset of what the
    // insert path touches. Used here purely as a diagnostic: if this also crashes or misbehaves
    // against our own candidate CGameMenu pointer, the object itself isn't safely readable yet
    // (points at explanation (a) above); if it completes cleanly, the object's basic hash-lookup
    // state is trustworthy and the bug is isolated to InsertNode's own extra fields (explanation
    // (b)). Either way, strictly safer to run first than another insert attempt.
    constexpr uintptr_t kFindPageRva = 0x101f7a90;

    // CSetNextPageMenuHandler's real ctor - address already known from a previous session
    // (matches FarCry2_server's demangled symbol), confirmed live on Dunia.dll this session via
    // decompile (recovered as a real demangled symbol, not a bare FUN_ address): stores the
    // target CStringID hash at this+0x74 and a flag byte at this+0x78 - exactly the fields
    // CSetNextPageMenuHandler::SwitchPage (0x10188d00, confirmed via disassembly) reads at click
    // time via ESI+0x74/ESI+0x78.
    constexpr uintptr_t kCtorRva = 0x10188ea0;

    // Same AddButton already proven safe in mods_tab.cpp, called with the identical
    // optionsMenuThis - resolved independently here rather than reaching into mods_tab.cpp's
    // anonymous-namespace g_addButton.
    constexpr uintptr_t kAddButtonRva = 0x10cdbb80;

    // What CGameMenu's own hashtable actually keys on: the *native* CRC-32 (GetNameHash/
    // CRC32_Hash), not magma::Id::Hash - confirmed via FUN_101f7a90 (the shared lookup helper
    // GetPage/SetNextPage/the get-or-create helper all call into): it hashes/compares a single
    // dereferenced 32-bit value, nothing string-shaped at that layer. The native hash is confirmed
    // byte-for-byte identical to Python's zlib.crc32 (see the doc's CRC-32 section), so this is
    // precomputed offline (zlib.crc32(b"FCSE_ModConfigPage")) rather than calling the native
    // GetNameHash at runtime: GetNameHash's own calling convention wasn't fully pinned down this
    // session (its RET immediate didn't cleanly match the expected 2-stack-arg __thiscall shape) -
    // avoiding the call entirely removes that uncertainty from a path that would otherwise fail
    // silently (wrong hash -> button does nothing) rather than crash.
    constexpr uint32_t kModPageId = 0xbeeb1688;

    using GetOrCreatePageSlotFn = uint32_t*(__thiscall*)(void* gameMenuThis, uint32_t* key);
    using CtorFn = void*(__thiscall*)(void* thisPtr, void* ownerPage, uint32_t* targetId,
                                       void* handler, unsigned char flag);
    using AddButtonFn = void*(__thiscall*)(void* thisPtr, const wchar_t* label, char visible,
                                            void* handler);
    // (this, outNode, key) - matches CGameMenu_PageTable_Find's decompiled signature exactly
    // (param_1=this via ECX, param_2=outNode, param_3=key, both passed on the stack).
    using FindPageFn = void(__thiscall*)(void* gameMenuThis, void** outNode, uint32_t* key);

    // Generous, mostly-safe-no-op vtable. CUIPageBase has ~19 real virtual methods (Init/
    // Shutdown/Display/Hide/PushPage/PopPage/SetPage/ConfigPage/RegisterModule/UnRegisterModule/
    // AddListener/RemoveListener/AddCommand/ExecuteCommands/OnActionSignal/Update/GetLayer/
    // Unload/dtor - see the doc's CUIPageBase table). Whether any of these get called on the
    // *current* page every frame (not just the two CGameMenu::SwitchPage is confirmed, via
    // disassembly of 0x101d1990, to call directly) is NOT confirmed. Every slot defaults to a
    // no-op that touches nothing, specifically so an unexpected per-frame call lands harmlessly
    // instead of reading a garbage function pointer.
    constexpr int kVtableSlotCount = 32;
    constexpr int kActivateSlot = 2;   // vtable+0x8 - confirmed via SwitchPage disassembly
    constexpr int kDeactivateSlot = 3; // vtable+0xc - confirmed via SwitchPage disassembly

    struct ModPageObject {
        void** vtable; // must stay first - what CGameMenu::SwitchPage reads through

        // Absorbs any native code that reads/writes page fields directly rather than through the
        // vtable. Confirmed necessary for at least one field: SwitchPage itself writes a
        // CGameMenu backpointer to +0x20 before calling activate (disassembly-confirmed). Sized
        // with real margin since other direct field accesses, if any, aren't ruled out.
        unsigned char reserved[252];

        void SafeNoOp() {}

        void OnActivate() {
            Log::Loader("ModPage: ACTIVATED - CGameMenu::SwitchPage called our vtable+0x8. Hand-"
                        "rolled page switch confirmed working end to end.");
        }

        void OnDeactivate() { Log::Loader("ModPage: deactivated"); }
    };

    using MemberFn = void (ModPageObject::*)();

    void* RawFunctionPointer(MemberFn fn) {
        union {
            MemberFn member;
            void* raw;
        } converter;
        converter.member = fn;
        return converter.raw;
    }

    void* g_vtable[kVtableSlotCount];
    bool g_vtableReady = false;

    void** EnsureVtable() {
        if (!g_vtableReady) {
            void* noOp = RawFunctionPointer(&ModPageObject::SafeNoOp);
            for (void*& slot : g_vtable) {
                slot = noOp;
            }
            g_vtable[kActivateSlot] = RawFunctionPointer(&ModPageObject::OnActivate);
            g_vtable[kDeactivateSlot] = RawFunctionPointer(&ModPageObject::OnDeactivate);
            g_vtableReady = true;
        }
        return g_vtable;
    }

    bool g_installed = false; // guards against double-install if this ever runs more than once

    // Every function below wraps exactly one native-pointer touchpoint in SEH and contains no
    // C++ objects with destructors (MSVC disallows mixing __try/__except with automatic object
    // unwinding in the same function) - callers do all string/logging work outside these.

    bool SafeReadPointer(void* base, ptrdiff_t offset, void** outValue, DWORD* outCode = nullptr) {
        __try {
            *outValue = *reinterpret_cast<void**>(reinterpret_cast<char*>(base) + offset);
            return true;
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            if (outCode != nullptr) {
                *outCode = GetExceptionCode();
            }
            return false;
        }
    }

    bool SafeConstructHandler(CtorFn ctor, void* handlerStorage, void* ownerPage,
                               uint32_t* targetId, DWORD* outCode = nullptr) {
        __try {
            ctor(handlerStorage, ownerPage, targetId, nullptr, 1);
            return true;
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            if (outCode != nullptr) {
                *outCode = GetExceptionCode();
            }
            return false;
        }
    }

    bool SafeFindPage(FindPageFn fn, void* gameMenu, uint32_t* key, void** outNode,
                       DWORD* outCode = nullptr) {
        __try {
            fn(gameMenu, outNode, key);
            return true;
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            if (outCode != nullptr) {
                *outCode = GetExceptionCode();
            }
            return false;
        }
    }

    bool SafeGetOrCreateSlot(GetOrCreatePageSlotFn fn, void* gameMenu, uint32_t* key,
                              uint32_t** outSlot, DWORD* outCode = nullptr) {
        __try {
            *outSlot = fn(gameMenu, key);
            return true;
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            if (outCode != nullptr) {
                *outCode = GetExceptionCode();
            }
            return false;
        }
    }

    bool SafeAddButton(AddButtonFn fn, void* thisPtr, const wchar_t* label, void* handler,
                        DWORD* outCode = nullptr) {
        __try {
            fn(thisPtr, label, 1, handler);
            return true;
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            if (outCode != nullptr) {
                *outCode = GetExceptionCode();
            }
            return false;
        }
    }

    void LogPtr(const char* what, void* value) {
        char buf[128];
        std::snprintf(buf, sizeof(buf), "ModPage: %s = 0x%p", what, value);
        Log::Loader(buf);
    }

    void LogFailed(const char* what, DWORD code) {
        char buf[160];
        std::snprintf(buf, sizeof(buf), "ModPage: %s raised SEH exception 0x%08lX (caught)", what,
                      static_cast<unsigned long>(code));
        Log::Loader(buf);
    }

    // Independent sanity check on a candidate CGameMenu* using fields already confirmed from
    // other decompiles this session (not FUN_107813e0 itself, so this corroborates or refutes
    // that helper's own success/failure with separate evidence): +0x10/+0x14 (end-sentinel
    // fields FUN_101f7a90/GetPage compare against), +0x1c (node array base pointer
    // FUN_101f7a90 indexes into), +0x28/+0x2c (bucket mask/bound used in the same hash lookup),
    // +0x34 (the ctor's self-registration helper pointer). A real CGameMenu should show small,
    // sane-looking values for the mask/count fields and plausible pointers for the rest; garbage
    // here means the candidate pointer isn't really a CGameMenu at all.
    void DumpCandidateGameMenu(void* candidate) {
        struct { const char* name; ptrdiff_t offset; } fields[] = {
            {"+0x10", 0x10}, {"+0x14", 0x14}, {"+0x1c", 0x1c},
            {"+0x28", 0x28}, {"+0x2c", 0x2c}, {"+0x34", 0x34},
        };
        for (const auto& field : fields) {
            void* value = nullptr;
            DWORD code = 0;
            if (SafeReadPointer(candidate, field.offset, &value, &code)) {
                char label[32];
                std::snprintf(label, sizeof(label), "candidate%s", field.name);
                LogPtr(label, value);
            } else {
                char what[32];
                std::snprintf(what, sizeof(what), "reading candidate%s", field.name);
                LogFailed(what, code);
            }
        }
    }
} // namespace

void ModPage::Install(void* optionsMenuThis) {
    if (g_installed) {
        return;
    }
    g_installed = true;

    uintptr_t base = DuniaApi::Base();
    if (base == 0 || optionsMenuThis == nullptr) {
        Log::Loader("ModPage: Dunia.dll not resolved or no owner page, skipping");
        return;
    }

    LogPtr("optionsMenuThis", optionsMenuThis);

    auto getOrCreateSlot = reinterpret_cast<GetOrCreatePageSlotFn>(
        base + (kGetOrCreatePageSlotRva - kDuniaPreferredBase));
    auto ctor = reinterpret_cast<CtorFn>(base + (kCtorRva - kDuniaPreferredBase));
    auto addButton = reinterpret_cast<AddButtonFn>(base + (kAddButtonRva - kDuniaPreferredBase));

    static uint32_t s_pageId = kModPageId;

    // Build the real handler first, using the doc's already-confirmed
    // "CSetNextPageMenuHandler(ownerPage, &targetId, nullptr, true)" construction pattern (the
    // same one the engine's own 5 real category buttons use) with ownerPage=optionsMenuThis -
    // that part matches AddButton's own already-safe `this`. Then read back what the ctor's base
    // class actually stored at +0x8, rather than assuming it equals optionsMenuThis (that
    // assumption is the prime suspect for the previous crash).
    void* handlerStorage = new unsigned char[256]();
    DWORD code = 0;
    if (!SafeConstructHandler(ctor, handlerStorage, optionsMenuThis, &s_pageId, &code)) {
        LogFailed("CSetNextPageMenuHandler ctor", code);
        Log::Loader("ModPage: not installed this session, game continues normally");
        return;
    }
    Log::Loader("ModPage: handler constructed OK");

    void* ownerPageFromHandler = nullptr;
    if (!SafeReadPointer(handlerStorage, 0x8, &ownerPageFromHandler, &code)) {
        LogFailed("reading handler+0x8", code);
        Log::Loader("ModPage: not installed this session, game continues normally");
        return;
    }
    LogPtr("handler+0x8 (ownerPage)", ownerPageFromHandler);
    Log::Loader(ownerPageFromHandler == optionsMenuThis
                    ? "ModPage: ownerPage MATCHES optionsMenuThis"
                    : "ModPage: ownerPage MISMATCHES optionsMenuThis - assumption was wrong");

    // Try the empirically-found ownerPage first; if it mismatched optionsMenuThis, also try
    // optionsMenuThis directly, purely for comparison data in the log (whichever one produces a
    // sane-looking pointer is the one to trust next time).
    void* gameMenu = nullptr;
    bool haveGameMenu = SafeReadPointer(ownerPageFromHandler, 0x140, &gameMenu, &code);
    if (haveGameMenu) {
        LogPtr("ownerPage+0x140 (CGameMenu*)", gameMenu);
    } else {
        LogFailed("reading ownerPage+0x140", code);
    }

    if (ownerPageFromHandler != optionsMenuThis) {
        void* altGameMenu = nullptr;
        if (SafeReadPointer(optionsMenuThis, 0x140, &altGameMenu, &code)) {
            LogPtr("optionsMenuThis+0x140 (for comparison)", altGameMenu);
        } else {
            LogFailed("reading optionsMenuThis+0x140", code);
        }
    }

    if (!haveGameMenu || gameMenu == nullptr) {
        Log::Loader("ModPage: no usable CGameMenu pointer found - Mods page not installed this "
                    "session, game continues normally");
        return;
    }

    // Independent corroboration before trusting FUN_107813e0 with this pointer - see
    // DumpCandidateGameMenu's own comment for what "sane" looks like here.
    DumpCandidateGameMenu(gameMenu);

    // Read-only probe BEFORE the risky insert - see kFindPageRva's own comment for why this
    // specific ordering distinguishes "object not ready yet" from "insert-path-specific bug".
    // Querying our own kModPageId (never yet inserted) should cleanly report a miss; what matters
    // is whether the call completes at all.
    auto findPage = reinterpret_cast<FindPageFn>(base + (kFindPageRva - kDuniaPreferredBase));
    void* findResult = nullptr;
    if (!SafeFindPage(findPage, gameMenu, &s_pageId, &findResult, &code)) {
        LogFailed("CGameMenu_PageTable_Find (read-only probe)", code);
        Log::Loader("ModPage: the CGameMenu object itself isn't safely readable yet at this hook "
                    "point (crashed on a plain lookup, not just insert) - not installed this "
                    "session, game continues normally");
        return;
    }
    LogPtr("Find probe result (expect a miss/sentinel, not null)", findResult);
    Log::Loader("ModPage: read-only Find completed without crashing - object's basic hash-lookup "
                "state is trustworthy, proceeding to the actual insert");

    uint32_t* slot = nullptr;
    if (!SafeGetOrCreateSlot(getOrCreateSlot, gameMenu, &s_pageId, &slot, &code)) {
        LogFailed("FUN_107813e0 (get-or-create slot)", code);
        Log::Loader("ModPage: not installed this session, game continues normally");
        return;
    }
    if (slot == nullptr) {
        Log::Loader("ModPage: get-or-create slot returned null (no exception) - Mods page not "
                    "installed this session, game continues normally");
        return;
    }
    LogPtr("get-or-create slot", slot);

    // Allocated oversized and never freed - same accepted one-time-per-session tradeoff as
    // ModsMenuHandler (menu_handler.cpp).
    auto* page = new ModPageObject();
    page->vtable = EnsureVtable();
    *slot = reinterpret_cast<uint32_t>(page);

    if (!SafeAddButton(addButton, optionsMenuThis, L"Mod Configuration Menu (experimental)",
                       handlerStorage, &code)) {
        LogFailed("AddButton", code);
        Log::Loader("ModPage: page registered but no button to reach it this session");
        return;
    }

    Log::Loader("ModPage: hand-rolled page registered, button appended to Options - click it "
                "in-game to verify (watch for \"ModPage: ACTIVATED\" in this log)");
}

} // namespace FCSE
