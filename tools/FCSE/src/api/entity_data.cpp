#include "api/entity_data.h"

#include "engine/entity_data_component.h"

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
        const std::optional<EntityValue> value = EntityDataComponent::Read(entity, Crc32(key));
        const T* typed = value ? std::get_if<T>(&*value) : nullptr;
        if (typed == nullptr) {
            return false;
        }
        *out = *typed;
        return true;
    }

    template <typename T>
    bool Set(void* entity, const char* key, T value) {
        return key != nullptr &&
               EntityDataComponent::Write(entity, Crc32(key), EntityValue(std::move(value)));
    }

    bool GetString(void* entity, const char* key, char* buffer, size_t capacity) {
        std::string text;
        if (buffer == nullptr || capacity == 0 || !Get(entity, key, &text)) {
            return false;
        }
        const size_t length = (std::min)(text.size(), capacity - 1);
        std::memcpy(buffer, text.data(), length);
        buffer[length] = '\0';
        return true;
    }

    bool SetString(void* entity, const char* key, const char* value) {
        return value != nullptr && Set(entity, key, std::string(value));
    }

    bool Remove(void* entity, const char* key) {
        return key != nullptr && EntityDataComponent::Erase(entity, Crc32(key));
    }

}

const FCSE_EntityDataAPI* EntityDataApi::Table() {
    static const FCSE_EntityDataAPI table{
        &EntityDataComponent::EntityOf, &Get<int32_t>, &Get<float>, &GetString,
        &Set<int32_t>, &Set<float>, &SetString, &Remove, &EntityDataComponent::Persist,
    };
    return &table;
}

}
