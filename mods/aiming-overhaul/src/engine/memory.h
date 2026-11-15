// Reading the engine's objects and hooking its functions.
#pragma once

#include "fcse_api.h"

#include <cstddef>
#include <cstdint>

namespace AimingOverhaul {

// The field of type T at `offset` into an engine object.
template <class T>
T& Field(uint8_t* object, ptrdiff_t offset) {
    return *reinterpret_cast<T*>(object + offset);
}

// The entity an entity proxy stands for, or null.
inline uint8_t* ProxyEntity(uint8_t* proxy) {
    return proxy != nullptr ? Field<uint8_t*>(proxy, 0x0C) : nullptr;
}

// CEntity::Lock and GetComponent, both __thiscall, which a free function spells __fastcall with an
// unused EDX.
namespace EntityCalls {
    using LockFn = void(__fastcall*)(void* entity, void* unused);
    using GetComponentFn = uint8_t*(__fastcall*)(void* entity, void* unused, const uint32_t* id);
    inline FCSE::Relocation<LockFn> g_lock{FCSE::Uplay(0x004DD4F0)};
    inline FCSE::Relocation<GetComponentFn> g_getComponent{FCSE::Uplay(0x004E2CC0)};
}

// Whether this build maps CEntity::Lock and GetComponent.
inline bool ComponentsMapped() {
    return EntityCalls::g_lock && EntityCalls::g_getComponent;
}

// The entity's component of the class named by `id`, the entity locked first; null when it has none
// or the calls are not mapped.
inline uint8_t* Component(void* entity, uint32_t id) {
    if (!ComponentsMapped()) {
        return nullptr;
    }
    EntityCalls::g_lock(entity, nullptr);
    return EntityCalls::g_getComponent(entity, nullptr, &id);
}

// The entity of `component`, such as a CWeapon, for its entity data; null for a null component.
inline void* EntityOf(void* component) {
    return component != nullptr ? FCSE::ApiPointer()->EntityData->EntityOf(component) : nullptr;
}

// Whether the entity of `component` has the integer entity data `key` set to anything but 0.
inline bool EntityFlag(void* component, const char* key) {
    void* entity = EntityOf(component);
    int32_t value = 0;
    if (entity != nullptr) {
        FCSE::ApiPointer()->EntityData->GetInt(entity, key, &value);
    }
    return value != 0;
}

// Hooks the engine function at `target`, keeping what it called in `original`. False when the
// function was not found, which is not logged here, or when FCSE refused it, which FCSE logs.
template <class Fn>
bool Hook(uintptr_t target, Fn detour, Fn* original) {
    return target != 0 && FCSE::ApiPointer()->Hook(reinterpret_cast<void*>(target),
                                                     reinterpret_cast<void*>(detour),
                                                     reinterpret_cast<void**>(original));
}

}
