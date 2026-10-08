#include "api/entity_data.h"

#include "engine/entity_data_component.h"
#include "util/crc32.h"

#include <algorithm>
#include <cstring>
#include <string>

namespace FCSE {

namespace {

    template <typename T>
    bool Get(void* entity, const char* key, T* out) {
        if (key == nullptr || out == nullptr) {
            return false;
        }
        auto lock = EntityDataComponent::Lock();
        const EntityDataStore* store = EntityDataComponent::StoreOf(entity, false);
        const EntityValue* value = store != nullptr ? store->Find(Crc32(key)) : nullptr;
        const T* typed = value != nullptr ? std::get_if<T>(value) : nullptr;
        if (typed == nullptr) {
            return false;
        }
        *out = *typed;
        return true;
    }

    template <typename T>
    bool Set(void* entity, const char* key, T value) {
        if (key == nullptr) {
            return false;
        }
        auto lock = EntityDataComponent::Lock();
        EntityDataStore* store = EntityDataComponent::StoreOf(entity, true);
        if (store == nullptr) {
            return false;
        }
        store->state.insert_or_assign(Crc32(key), EntityValue(std::move(value)));
        return true;
    }

    void* __cdecl EntityOf(void* component) { return EntityDataComponent::EntityOf(component); }

    bool __cdecl GetInt(void* entity, const char* key, int32_t* value) {
        return Get(entity, key, value);
    }

    bool __cdecl GetFloat(void* entity, const char* key, float* value) {
        return Get(entity, key, value);
    }

    bool __cdecl GetString(void* entity, const char* key, char* buffer, size_t capacity) {
        std::string text;
        if (buffer == nullptr || capacity == 0 || !Get(entity, key, &text)) {
            return false;
        }
        const size_t length = (std::min)(text.size(), capacity - 1);
        std::memcpy(buffer, text.data(), length);
        buffer[length] = '\0';
        return true;
    }

    bool __cdecl SetInt(void* entity, const char* key, int32_t value) {
        return Set(entity, key, value);
    }

    bool __cdecl SetFloat(void* entity, const char* key, float value) {
        return Set(entity, key, value);
    }

    bool __cdecl SetString(void* entity, const char* key, const char* value) {
        return value != nullptr && Set(entity, key, std::string(value));
    }

    bool __cdecl Remove(void* entity, const char* key) {
        if (key == nullptr) {
            return false;
        }
        auto lock = EntityDataComponent::Lock();
        EntityDataStore* store = EntityDataComponent::StoreOf(entity, false);
        return store != nullptr && store->state.erase(Crc32(key)) > 0;
    }

    bool __cdecl Persist(void* entity) { return EntityDataComponent::Persist(entity); }

}

const FCSE_EntityDataAPI* EntityDataApi::Table() {
    static const FCSE_EntityDataAPI table{
        &EntityOf, &GetInt, &GetFloat, &GetString, &SetInt,
        &SetFloat, &SetString, &Remove, &Persist,
    };
    return &table;
}

}
