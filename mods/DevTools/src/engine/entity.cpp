// Entities, and holding onto one safely.
//
// See docs/docs/engine-internals/free-camera-and-noclip.md for the holder layout and the
// by-value reference convention the manager's SetFocus uses.
#include "engine/entity.h"

#include "fcse_api.h"

#include <cstdint>

namespace {
    constexpr ptrdiff_t kRefCount = 0x08;
    constexpr ptrdiff_t kRefEntity = 0x0C;
    constexpr ptrdiff_t kEntityMatrix = 0x30;

    constexpr ptrdiff_t kManagerFocusLow = 0x08;
    constexpr ptrdiff_t kManagerFocusHigh = 0x0C;

    constexpr size_t kPhysicsEnabledSlot = 0xB4 / sizeof(void*);

    // Into the manager's resolve-and-set-focus sequence: the ref world global, and the call that
    // turns an id into a holder.
    constexpr size_t kRefWorldGlobal = 0x0F;
    constexpr size_t kRefFromIdCall = 0x13;

    // Into the ghost camera's activate handler, the one place the engine tests the physics tag,
    // registers it and fetches by it in a row.
    constexpr size_t kPhysicsTagFlag = 0x02;
    constexpr size_t kPhysicsTagRegisterCall = 0x0B;
    constexpr size_t kPhysicsTagValue = 0x11;
    constexpr size_t kGetComponentCall = 0x17;

    using RefFromIdFn = void*(__fastcall*)(void* refWorld, void* unused, void** out, uint32_t idLow,
                                           uint32_t idHigh);
    using ReleaseRefFn = void(__fastcall*)(void* ref);
    using FreeRefFn = void(__fastcall*)(void* ref);
    using SetPositionFn = void(__fastcall*)(void* entity, void* unused, float x, float y, float z);
    using SetEulerFn = void(__fastcall*)(void* entity, void* unused, float pitch, float roll,
                                         float yaw);
    using RegisterTagFn = void(__fastcall*)(void* unused);
    using GetComponentFn = void*(__fastcall*)(void* entity, void* unused, void* tag);
    using SetPhysicsEnabledFn = void(__fastcall*)(void* physics, void* unused, int32_t enabled);

    FCSE::Relocation<uint8_t*> g_focusResolve{FCSE::Pattern(
        "8B 55 0C 8B 45 08 52 50 8D 4C 24 4C 51 8B 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 8B 7C 24 44 83 7F "
        "0C 00 74 12 51 8B C4 89 38 83 47 08 01 8B 13 8B 42 78 8B CB FF D0")};
    FCSE::Relocation<uint8_t*> g_physicsTagSite{FCSE::Pattern(
        "83 3D ?? ?? ?? ?? 00 75 07 33 C9 E8 ?? ?? ?? ?? 68 ?? ?? ?? ?? 8B CE E8 ?? ?? ?? ?? 85 ED "
        "8B F8 75 0D 85 FF 75 09")};

    // SetEuler sits immediately behind SetPosition, which is the only reason either is patternable.
    FCSE::Relocation<uint8_t*> g_setPositionSite{FCSE::Pattern(
        "6A 00 8D 44 24 08 50 E8 ?? ?? ?? ?? C2 0C 00 CC 6A 00 8D 44 24 08 50 E8 ?? ?? ?? ?? C2 0C "
        "00")};

    constexpr size_t kSetEulerOffset = 0x10;

    FCSE::Relocation<ReleaseRefFn> g_releaseRef{FCSE::Uplay(0x0029B7F0)};
    FCSE::Relocation<FreeRefFn> g_freeRef{FCSE::Uplay(0x00228AD0)};

    void*** g_refWorld = nullptr;
    RefFromIdFn g_refFromId = nullptr;
    SetPositionFn g_setPosition = nullptr;
    SetEulerFn g_setEuler = nullptr;

    int32_t* g_physicsTagFlag = nullptr;
    void* g_physicsTag = nullptr;
    RegisterTagFn g_registerPhysicsTag = nullptr;
    GetComponentFn g_getComponentByTag = nullptr;

    // Past the opcode, over the displacement, to what a rel32 call points at.
    template <typename T>
    T CallTarget(uint8_t* site) {
        return reinterpret_cast<T>(site + 5 + *reinterpret_cast<int32_t*>(site + 1));
    }
}

namespace DevTools::Entity {

void Install() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (g_focusResolve) {
        g_refWorld = *reinterpret_cast<void****>(g_focusResolve.get() + kRefWorldGlobal);
        g_refFromId = CallTarget<RefFromIdFn>(g_focusResolve.get() + kRefFromIdCall);
    }

    if (g_physicsTagSite) {
        uint8_t* site = g_physicsTagSite.get();

        g_physicsTagFlag = *reinterpret_cast<int32_t**>(site + kPhysicsTagFlag);
        g_physicsTag = *reinterpret_cast<void**>(site + kPhysicsTagValue);
        g_registerPhysicsTag = CallTarget<RegisterTagFn>(site + kPhysicsTagRegisterCall);
        g_getComponentByTag = CallTarget<GetComponentFn>(site + kGetComponentCall);
    }

    if (g_setPositionSite) {
        g_setPosition = reinterpret_cast<SetPositionFn>(g_setPositionSite.get());
        g_setEuler = reinterpret_cast<SetEulerFn>(g_setPositionSite.get() + kSetEulerOffset);
    }

    if (g_refFromId == nullptr || g_getComponentByTag == nullptr || g_setPosition == nullptr) {
        api->Log("entity: not everything needed to move an entity was found in this build");
    }
}

void* AcquireFocusRef(void* manager) {
    if (manager == nullptr || g_refFromId == nullptr || g_refWorld == nullptr) {
        return nullptr;
    }

    void** refWorld = *g_refWorld;
    if (refWorld == nullptr) {
        return nullptr;
    }

    auto* bytes = static_cast<uint8_t*>(manager);
    const uint32_t idLow = *reinterpret_cast<uint32_t*>(bytes + kManagerFocusLow);
    const uint32_t idHigh = *reinterpret_cast<uint32_t*>(bytes + kManagerFocusHigh);

    // The manager's own guard for "no focus set".
    if ((idLow & idHigh) == 0xFFFFFFFF) {
        return nullptr;
    }

    void* ref = nullptr;
    g_refFromId(refWorld, nullptr, &ref, idLow, idHigh);
    return ref;
}

void ReleaseRef(void*& ref) {
    if (ref == nullptr) {
        return;
    }

    auto* count = reinterpret_cast<int32_t*>(static_cast<uint8_t*>(ref) + kRefCount);
    *count -= 1;

    if (*count == 0 && g_releaseRef && g_freeRef) {
        g_releaseRef.get()(ref);
        g_freeRef.get()(ref);
    }

    ref = nullptr;
}

void* Of(void* ref) {
    return ref != nullptr ? *reinterpret_cast<void**>(static_cast<uint8_t*>(ref) + kRefEntity)
                          : nullptr;
}

void AddRef(void* ref) {
    if (ref != nullptr) {
        *reinterpret_cast<int32_t*>(static_cast<uint8_t*>(ref) + kRefCount) += 1;
    }
}

const float* Matrix(void* entity) {
    return reinterpret_cast<const float*>(static_cast<uint8_t*>(entity) + kEntityMatrix);
}

void SetPosition(void* entity, float x, float y, float z) {
    if (g_setPosition != nullptr) {
        g_setPosition(entity, nullptr, x, y, z);
    }
}

void SetEuler(void* entity, float pitch, float roll, float yaw) {
    if (g_setEuler != nullptr) {
        g_setEuler(entity, nullptr, pitch, roll, yaw);
    }
}

void* PhysicsComponent(void* entity) {
    if (g_getComponentByTag == nullptr || g_physicsTag == nullptr) {
        return nullptr;
    }

    // The engine's own guard on every fetch of this tag.
    if (g_physicsTagFlag != nullptr && *g_physicsTagFlag == 0 && g_registerPhysicsTag != nullptr) {
        g_registerPhysicsTag(nullptr);
    }

    return g_getComponentByTag(entity, nullptr, g_physicsTag);
}

void SetPhysicsEnabled(void* physics, bool enabled) {
    if (physics == nullptr) {
        return;
    }

    auto** vtable = *reinterpret_cast<void***>(physics);
    reinterpret_cast<SetPhysicsEnabledFn>(vtable[kPhysicsEnabledSlot])(physics, nullptr,
                                                                      enabled ? 1 : 0);
}

}
