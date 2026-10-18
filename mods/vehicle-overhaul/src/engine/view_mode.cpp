// The player's body as the engine draws it. The player's graphic holds two models, the full body first and the
// headless avatar second, and CPawn::ViewModeChanged shows one or the other along with the body's shadow and the
// weapons it carries. The engine only ever goes one way: putting first person up discards the full body, so that
// call is taken out. Coming back out, it opens every viewport to the full body but leaves it hidden, so it is shown
// here too.
#include "engine/view_mode.h"

#include "fcse_api.h"

#include <cstddef>
#include <cstdint>

namespace {
    using ViewModeChangedFn = void(__thiscall*)(void* pawn, bool firstPerson);
    using LockFn = void(__thiscall*)(void* entity);
    using GetComponentFn = void*(__thiscall*)(void* entity, const void* type);
    using SetObjectVisibleFn = bool(__thiscall*)(void* graphic, unsigned index, bool visible);

    // CPawn::ViewModeChanged, through its lookup of the pawn's graphic component.
    FCSE::Relocation<uint8_t*> g_viewModeChanged{FCSE::Pattern(
        "53 8B 5C 24 08 55 56 57 53 8B F9 E8 ?? ?? ?? ?? 8B 77 08 8B 6E 0C 83 46 08 01 8B CD E8 ?? ?? ?? ?? "
        "83 3D ?? ?? ?? ?? 00 75 07 33 C9 E8 ?? ?? ?? ?? 68 ?? ?? ?? ?? 8B CD E8")};
    // Its calls: CEntity::Lock, and GetComponent with the graphic component's type, pushed just before.
    constexpr ptrdiff_t kLockCall = 0x1C;
    constexpr ptrdiff_t kGraphicType = 0x32;
    constexpr ptrdiff_t kGetComponentCall = 0x38;

    // The model switch's first-person branch: SetObjectVisible(1, true) on the avatar, then RemoveObject(0) on the
    // full body as PUSH 0, MOV ECX,EDI, CALL, which pops its own argument.
    FCSE::Relocation<uint8_t*> g_firstPersonBranch{FCSE::Pattern(
        "8B 4C 24 14 F7 D1 51 8B 48 34 E8 ?? ?? ?? ?? 6A 01 6A 01 8B CF E8 ?? ?? ?? ?? 6A 00 8B CF E8 ?? ?? ?? ?? "
        "5F 5E 5D 5B C2 04 00")};
    constexpr ptrdiff_t kSetObjectVisibleCall = 0x15;
    constexpr ptrdiff_t kDiscard = 0x1A;
    constexpr size_t kDiscardSize = 9;

    constexpr unsigned kFullBody = 0;

    // A pawn's entity proxy, and the entity it holds.
    constexpr ptrdiff_t kPawnProxy = 0x08;
    constexpr ptrdiff_t kProxyEntity = 0x0C;

    // The first-person bit in the pawn's desired and effective data. The vehicle beautifier plays the third-person
    // driving clips while it is clear, and the beautifier component calls ViewModeChanged when the two differ.
    constexpr ptrdiff_t kPawnImpl = 0x10;
    constexpr ptrdiff_t kDesiredFlags = 0x140 + 4;
    constexpr ptrdiff_t kEffectiveFlags = 0x2D0 + 4;
    constexpr uint8_t kFirstPersonBit = 0x80;

    LockFn g_lock = nullptr;
    GetComponentFn g_getComponent = nullptr;
    const void* g_graphicType = nullptr;
    SetObjectVisibleFn g_setObjectVisible = nullptr;

    // The function a CALL rel32 at `call` goes to.
    template <class Fn>
    Fn CallTarget(const uint8_t* call) {
        return reinterpret_cast<Fn>(call + 5 + *reinterpret_cast<const int32_t*>(call + 1));
    }

    template <class T>
    T At(const void* base, ptrdiff_t offset) {
        return *reinterpret_cast<const T*>(static_cast<const uint8_t*>(base) + offset);
    }

    void SetFirstPersonBit(uint8_t* flags, bool firstPerson) {
        *flags = firstPerson ? *flags | kFirstPersonBit : *flags & ~kFirstPersonBit;
    }
}

namespace VehicleOverhaul::ViewMode {

bool Install() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();
    if (!g_viewModeChanged || !g_firstPersonBranch) {
        api->Log("view mode: CPawn::ViewModeChanged or the model switch was not found in this build");
        return false;
    }
    const uint8_t* viewModeChanged = g_viewModeChanged.get();
    g_lock = CallTarget<LockFn>(viewModeChanged + kLockCall);
    g_getComponent = CallTarget<GetComponentFn>(viewModeChanged + kGetComponentCall);
    g_graphicType = At<const void*>(viewModeChanged, kGraphicType);
    g_setObjectVisible = CallTarget<SetObjectVisibleFn>(g_firstPersonBranch.get() + kSetObjectVisibleCall);

    uint8_t nops[kDiscardSize];
    for (uint8_t& nop : nops) {
        nop = 0x90;
    }
    return api->Patch(g_firstPersonBranch.get() + kDiscard, nops, kDiscardSize);
}

void Set(void* pawn, bool firstPerson) {
    if (pawn == nullptr || !g_viewModeChanged) {
        return;
    }
    if (uint8_t* impl = At<uint8_t*>(pawn, kPawnImpl)) {
        SetFirstPersonBit(impl + kDesiredFlags, firstPerson);
        SetFirstPersonBit(impl + kEffectiveFlags, firstPerson);
    }
    reinterpret_cast<ViewModeChangedFn>(g_viewModeChanged.get())(pawn, firstPerson);
    if (firstPerson) {
        return;
    }
    void* proxy = At<void*>(pawn, kPawnProxy);
    void* entity = proxy != nullptr ? At<void*>(proxy, kProxyEntity) : nullptr;
    if (entity == nullptr) {
        return;
    }
    g_lock(entity);
    void* graphic = g_getComponent(entity, g_graphicType);
    if (graphic != nullptr) {
        g_setObjectVisible(graphic, kFullBody, true);
    }
}

}
