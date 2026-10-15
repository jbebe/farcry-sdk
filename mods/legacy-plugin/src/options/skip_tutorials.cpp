// Skip tutorial pop-ups: the tutorial boxes that interrupt play, or those and the floating hints.
//
// From FC2JackalFix (MIT, (c) 2026 Joshhhuaaa, TGP482) source/gameplay/tutorialmessages.ixx. The
// choice indexes kSkipTutorialsChoices in main.cpp. Sites: the two pop-up builders, detoured to
// answer the script's continue callback through Domino without a box; the floating-hint builder,
// detoured to take its hint straight down; and the Domino and message-box calls that takes.
#include "fcse_api.h"

#include <cstdint>

namespace {
    enum SkipLevel : uint32_t {
        kSkipNone = 0,
        kSkipPopups = 1,
        kSkipFloating = 2,
    };

    uint32_t g_level = kSkipNone;

    // CGameMessageBoxHelper::ActiveFloatingId, and the id the manager writes when nothing is up.
    constexpr ptrdiff_t kActiveFloatingId = 0x18;
    constexpr uint32_t kNoMessageBox = 0xFFFFFFFF;

    // Into the matches: the pop-up's Domino registry global and register call; the release call;
    // the floating replace half's message-box manager global and hide call.
    constexpr ptrdiff_t kPopupRegistry = 0x2E;
    constexpr ptrdiff_t kPopupRegister = 0x32;
    constexpr ptrdiff_t kReleaseCall = 0x0F;
    constexpr ptrdiff_t kHideManager = 0x17;
    constexpr ptrdiff_t kHideCall = 0x1C;

    // The pop-up and its custom-page twin share a shape; the frame size and spill slot tell them
    // apart.
    FCSE::Relocation<uint8_t*> g_popup{FCSE::Pattern(
        "81 EC 80 01 00 00 8B 84 24 98 01 00 00 53 55 56 57 50 51 89 4C 24 20 "
        "8B 8C 24 AC 01 00 00 33 DB 3B CB 8B C4 89 08 74 04 83 41 04 01 8B 0D ?? ?? ?? ?? E8")};
    FCSE::Relocation<uint8_t*> g_customPopup{FCSE::Pattern(
        "81 EC 6C 01 00 00 8B 84 24 88 01 00 00 53 55 56 57 50 51 89 4C 24 1C "
        "8B 8C 24 9C 01 00 00 33 DB 3B CB 8B C4 89 08 74 04 83 41 04 01 8B 0D ?? ?? ?? ?? E8")};
    FCSE::Relocation<uint8_t*> g_invoke{FCSE::Pattern(
        "83 EC 24 56 57 8B 79 44 8D 71 34 8D 44 24 30 50 8D 4C 24 0C 51 8B CE E8")};

    // Release shares its prologue with other functions, so it is reached through the release loop
    // at the tail of the helper's result handler.
    FCSE::Relocation<uint8_t*> g_releaseLoop{
        FCSE::Pattern("74 19 8B 46 04 8B 0E 50 51 8B 0D ?? ?? ?? ?? E8")};

    FCSE::Relocation<uint8_t*> g_floating{FCSE::Pattern(
        "81 EC AC 01 00 00 53 55 56 57 8D 44 24 13 89 4C 24 18 50 8D 4C 24 24 33 DB BE")};
    FCSE::Relocation<uint8_t*> g_floatingReplace{FCSE::Pattern(
        "8B 7C 24 20 8B 57 18 8D 77 18 83 C4 08 3B 15 ?? ?? ?? ?? 74 0C 8B 0D ?? ?? ?? ?? 56 E8")};

    // The handle comes back in EDX:EAX, id low and a constant 2 high.
    using DominoRegisterFn = uint64_t(__fastcall*)(void* registry, void* edx, void* object,
                                                   const char* method);

    // The handle goes back as two arguments, which must stay adjacent on the stack: the function
    // takes the address of the first to rebuild the key.
    using DominoInvokeFn = void(__fastcall*)(void* registry, void* edx, uint32_t id, uint32_t type,
                                             const void* argument);
    using DominoReleaseFn = void(__fastcall*)(void* registry, void* edx, uint32_t id,
                                              uint32_t type);
    using HideMessageBoxFn = void(__fastcall*)(void* manager, void* edx, const uint32_t* id);

    // __thiscall, six stack arguments, RET 0x18.
    using PopupFn = void(__fastcall*)(void* helper, void* edx, const char* title, const char* text,
                                      const char* button, const char* actionMap, void* object,
                                      const char* method);

    // The same with a hud.mgb page id ahead of the captions, RET 0x1C.
    using CustomPopupFn = void(__fastcall*)(void* helper, void* edx, const char* page,
                                            const char* title, const char* text,
                                            const char* button, const char* actionMap,
                                            void* object, const char* method);

    // Returns an MSVC8 std::string by value into the caller's buffer, the first stack argument.
    using FloatingFn = void*(__fastcall*)(void* helper, void* edx, void* result, const char* text,
                                          const char* actionMap, void* object, const char* method);

    void** g_dominoRegistry = nullptr;
    void** g_messageBoxManager = nullptr;
    DominoRegisterFn g_dominoRegister = nullptr;
    DominoInvokeFn g_dominoInvoke = nullptr;
    DominoReleaseFn g_dominoRelease = nullptr;
    HideMessageBoxFn g_hideMessageBox = nullptr;

    PopupFn g_originalPopup = nullptr;
    CustomPopupFn g_originalCustomPopup = nullptr;
    FloatingFn g_originalFloating = nullptr;

    // MSVC8 std::string. Zero length selects the handler's one-argument call and leaves buffer and
    // capacity unread.
    struct DuniaString {
        void* proxy;
        char buffer[16];
        uint32_t size;
        uint32_t capacity;
    };
    static_assert(sizeof(DuniaString) == 0x1C, "MSVC8 std::string is 0x1C bytes");

    uint8_t* CallTarget(uint8_t* call) {
        return call + 5 + *reinterpret_cast<int32_t*>(call + 1);
    }

    // Answers the script's continue callback without a box ever existing.
    void FireContinue(void* object, const char* method) {
        if (object == nullptr || method == nullptr || *g_dominoRegistry == nullptr) {
            return;
        }

        void* registry = *g_dominoRegistry;
        const uint64_t handle = g_dominoRegister(registry, nullptr, object, method);
        const uint32_t id = static_cast<uint32_t>(handle);
        const uint32_t type = static_cast<uint32_t>(handle >> 32);

        DuniaString argument{};
        argument.capacity = 15;

        g_dominoInvoke(registry, nullptr, id, type, &argument);
        g_dominoRelease(registry, nullptr, id, type);
    }

    // HideFloatingTutorialMessageBox without its script binding's string parse. Leaves the floating
    // tag at helper+0x38 set; only serialisation reads it.
    void HideActiveFloating(void* helper) {
        if (helper == nullptr || *g_messageBoxManager == nullptr) {
            return;
        }

        uint32_t& id = *reinterpret_cast<uint32_t*>(static_cast<uint8_t*>(helper) +
                                                    kActiveFloatingId);
        if (id == kNoMessageBox) {
            return;
        }

        g_hideMessageBox(*g_messageBoxManager, nullptr, &id);
        id = kNoMessageBox;
    }

    void __fastcall PopupDetour(void* helper, void* edx, const char* title, const char* text,
                                const char* button, const char* actionMap, void* object,
                                const char* method) {
        if (g_level < kSkipPopups) {
            g_originalPopup(helper, edx, title, text, button, actionMap, object, method);
            return;
        }
        FireContinue(object, method);
    }

    void __fastcall CustomPopupDetour(void* helper, void* edx, const char* page, const char* title,
                                      const char* text, const char* button, const char* actionMap,
                                      void* object, const char* method) {
        if (g_level < kSkipPopups) {
            g_originalCustomPopup(helper, edx, page, title, text, button, actionMap, object,
                                  method);
            return;
        }
        FireContinue(object, method);
    }

    // Run then taken down rather than skipped: the script keeps the string and its later hide
    // does nothing.
    void* __fastcall FloatingDetour(void* helper, void* edx, void* result, const char* text,
                                    const char* actionMap, void* object, const char* method) {
        void* returned = g_originalFloating(helper, edx, result, text, actionMap, object, method);
        if (g_level >= kSkipFloating) {
            HideActiveFloating(helper);
        }
        return returned;
    }
}

void InstallSkipTutorials() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!g_popup || !g_customPopup || !g_invoke || !g_releaseLoop) {
        api->Log("skip tutorial pop-ups: the tutorial message box builders were not found in this "
                 "build - not applied");
        return;
    }

    g_dominoRegistry = *reinterpret_cast<void***>(g_popup.get() + kPopupRegistry);
    g_dominoRegister =
        reinterpret_cast<DominoRegisterFn>(CallTarget(g_popup.get() + kPopupRegister));
    g_dominoInvoke = reinterpret_cast<DominoInvokeFn>(g_invoke.get());
    g_dominoRelease =
        reinterpret_cast<DominoReleaseFn>(CallTarget(g_releaseLoop.get() + kReleaseCall));

    api->Hook(g_popup.get(), reinterpret_cast<void*>(&PopupDetour),
              reinterpret_cast<void**>(&g_originalPopup));
    api->Hook(g_customPopup.get(), reinterpret_cast<void*>(&CustomPopupDetour),
              reinterpret_cast<void**>(&g_originalCustomPopup));

    // The pop-ups stay skippable without the floating hints.
    if (!g_floating || !g_floatingReplace) {
        api->Log("skip tutorial pop-ups: the floating hint builder was not found in this build - "
                 "hints are not skipped");
        return;
    }

    g_messageBoxManager = *reinterpret_cast<void***>(g_floatingReplace.get() + kHideManager);
    g_hideMessageBox =
        reinterpret_cast<HideMessageBoxFn>(CallTarget(g_floatingReplace.get() + kHideCall));

    api->Hook(g_floating.get(), reinterpret_cast<void*>(&FloatingDetour),
              reinterpret_cast<void**>(&g_originalFloating));
}

void __cdecl OnSkipTutorialsChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
    g_level = value->asChoice;
}
