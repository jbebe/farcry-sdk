// See docs/docs/engine-internals/first-person-aiming.md, "A scope's sight picture".
#include "engine/entity_name.h"

#include <cstddef>
#include <excpt.h>

namespace {
    // CWeapon's entity, through the proxy at +0x08, and that entity's name.
    constexpr ptrdiff_t kWeaponProxy = 0x08;
    constexpr ptrdiff_t kProxyEntity = 0x0C;
    constexpr ptrdiff_t kEntityName = 0x14;

    template <class T>
    T Field(uint8_t* object, ptrdiff_t offset) {
        return *reinterpret_cast<T*>(object + offset);
    }
}

void WeaponOverhaul::EntityName::Read(uint8_t* weapon, char (&name)[64]) {
    name[0] = '\0';
    __try {
        uint8_t* proxy = weapon != nullptr ? Field<uint8_t*>(weapon, kWeaponProxy) : nullptr;
        uint8_t* entity = proxy != nullptr ? Field<uint8_t*>(proxy, kProxyEntity) : nullptr;
        const char* text = entity != nullptr ? Field<const char*>(entity, kEntityName) : nullptr;
        for (size_t i = 0; text != nullptr && i + 1 < sizeof(name) && text[i] != '\0'; i++) {
            name[i] = text[i];
            name[i + 1] = '\0';
        }
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        name[0] = '\0';
    }
}

bool WeaponOverhaul::EntityName::Is(std::string_view name, std::string_view archetype) {
    return name.starts_with(archetype) &&
           (name.size() == archetype.size() || name[archetype.size()] == '.');
}
