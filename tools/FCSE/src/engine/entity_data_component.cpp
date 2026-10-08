#include "engine/entity_data_component.h"

#include "engine/address_library.h"
#include "engine/address_symbols.h"
#include "engine/entity_component_abi.h"
#include "log.h"
#include "util/crc32.h"
#include "util/member_fn.h"
#include "util/seh.h"

#include <algorithm>
#include <array>
#include <cstdio>
#include <mutex>
#include <new>
#include <string>
#include <utility>

namespace FCSE {

namespace {

    constexpr SerializationId Key(const char* name) { return {name, Crc32(name)}; }

    constexpr char kClassName[] = "CFCSEDataComponent";
    constexpr uint32_t kClassId = Crc32(kClassName);
    constexpr uint32_t kPersistClassId = Crc32("CPersistComponent");

    // An entry's value, under the name of its type.
    constexpr SerializationId kIntValue = Key("Int");
    constexpr SerializationId kFloatValue = Key("Float");
    constexpr SerializationId kStringValue = Key("String");

    using CreateFn = void*(__cdecl*)(const uint32_t* classId);
    using DestroyFn = void*(__thiscall*)(void* component, uint32_t flags);
    using RegisterFn = bool(__thiscall*)(void* factory, const uint32_t* classId, CreateFn create,
                                         bool replace);
    using ComponentFn = void*(__thiscall*)(void* entity, const uint32_t* classId);
    using LockFn = void(__thiscall*)(void* entity);

    struct FactoryEntry {
        uint32_t classId;
        CreateFn create;
    };

    std::mutex g_lock;
    bool g_ready = false;

    CreateFn g_createPersist = nullptr;
    DestroyFn g_destroyBase = nullptr;
    ComponentFn g_getComponent = nullptr;
    ComponentFn g_createComponent = nullptr;
    LockFn g_lockEntity = nullptr;

    std::array<const void*, kComponentVtableSlots> g_vtable{};
    std::array<const void*, kMemberVtableSlots> g_memberVtable{};
    HierarchyInfo g_hierarchy{};
    const Member g_member{g_memberVtable.data(), Key("Entries")};
    const Member* const g_members[] = {&g_member};
    const Descriptor g_descriptor{g_members, 1};

    template <typename T>
    T& Field(void* object, ptrdiff_t offset) {
        return *reinterpret_cast<T*>(static_cast<char*>(object) + offset);
    }

    EntityDataStore*& StoreField(void* component) {
        return Field<EntityDataStore*>(component, kStoreOffset);
    }

    // Runs `fn` on the component's store under the store lock; false if there is none.
    template <typename Fn>
    bool WithStore(void* component, Fn fn) {
        if (component == nullptr) {
            return false;
        }
        std::lock_guard lock(g_lock);
        EntityDataStore* store = StoreField(component);
        return store != nullptr && fn(*store);
    }

    template <typename Ret, typename... Args>
    Ret CallSlot(void* object, size_t slot, Args... args) {
        using Fn = Ret(__thiscall*)(void*, Args...);
        return reinterpret_cast<Fn>((*static_cast<void* const* const*>(object))[slot])(object,
                                                                                      args...);
    }

    void ReadEntries(void* node, EntityDataStore::Layer& layer) {
        layer.clear();
        const uint32_t count = CallSlot<uint32_t>(node, kNodeChildCountSlot);
        for (uint32_t index = 0; index < count; ++index) {
            void* child = CallSlot<void*>(node, kNodeChildSlot, index);
            if (child == nullptr) {
                continue;
            }
            SerializationId tag{};
            CallSlot<SerializationId*>(child, kNodeTagSlot, &tag);

            int32_t number = 0;
            float real = 0.0f;
            const char* text = nullptr;
            if (CallSlot<bool>(child, kNodeGetIntSlot, &kIntValue, &number)) {
                layer.insert_or_assign(tag.id, number);
            } else if (CallSlot<bool>(child, kNodeGetFloatSlot, &kFloatValue, &real)) {
                layer.insert_or_assign(tag.id, real);
            } else if (CallSlot<bool>(child, kNodeGetStringSlot, &kStringValue, &text) &&
                       text != nullptr) {
                layer.insert_or_assign(tag.id, std::string(text));
            }
        }
    }

    void WriteEntries(void* node, const EntityDataStore::Layer& layer) {
        for (const auto& [key, value] : layer) {
            const SerializationId tag{nullptr, key};
            void* child = CallSlot<void*>(node, kNodeNewChildSlot, &tag);
            if (child == nullptr) {
                continue;
            }
            if (const auto* number = std::get_if<int32_t>(&value)) {
                CallSlot<void>(child, kNodeSetIntSlot, &kIntValue, *number);
            } else if (const auto* real = std::get_if<float>(&value)) {
                CallSlot<void>(child, kNodeSetFloatSlot, &kFloatValue, *real);
            } else if (const auto* text = std::get_if<std::string>(&value)) {
                CallSlot<void>(child, kNodeSetStringSlot, &kStringValue, text->c_str());
            }
        }
    }

    // The overridden component slots, as members of a type never instantiated: `this` is always the
    // engine's component.
    struct ComponentThunk {
        void* Destroy(uint32_t flags);
        const HierarchyInfo* Hierarchy();
        const Descriptor* GetDescriptor();
    };

    void* ComponentThunk::Destroy(uint32_t flags) {
        void* component = this;
        {
            std::lock_guard lock(g_lock);
            delete std::exchange(StoreField(component), nullptr);
        }
        return g_destroyBase(component, flags);
    }

    const HierarchyInfo* ComponentThunk::Hierarchy() { return &g_hierarchy; }

    const Descriptor* ComponentThunk::GetDescriptor() { return &g_descriptor; }

    // The one member, which reads and writes the component node's children as entries.
    struct MemberThunk {
        void Load(void* component, void* node);
        void Save(void* component, void* node);
        void LoadState(void* component, void* node);
        void SaveState(void* component, void* node);
        void Describe(void* unused1, void* unused2);
        SerializationId* Id(SerializationId* out);
        void Set(void* component, void* value);
        void* Get(void* component);
    };

    void MemberThunk::Load(void* component, void* node) {
        WithStore(component, [&](EntityDataStore& store) {
            ReadEntries(node, store.authored);
            return true;
        });
    }

    void MemberThunk::Save(void* component, void* node) {
        WithStore(component, [&](EntityDataStore& store) {
            WriteEntries(node, store.Merged());
            return true;
        });
    }

    void MemberThunk::LoadState(void* component, void* node) {
        WithStore(component, [&](EntityDataStore& store) {
            ReadEntries(node, store.state);
            return true;
        });
    }

    void MemberThunk::SaveState(void* component, void* node) {
        WithStore(component, [&](EntityDataStore& store) {
            WriteEntries(node, store.state);
            return true;
        });
    }

    void MemberThunk::Describe(void*, void*) {}

    SerializationId* MemberThunk::Id(SerializationId* out) {
        *out = g_member.key;
        return out;
    }

    void MemberThunk::Set(void*, void*) {}

    void* MemberThunk::Get(void*) { return nullptr; }

    // Registered under kClassId: a CPersistComponent the engine creates, whose own fields the store
    // takes over once its vtable is FCSE's.
    void* __cdecl CreateDataComponent(const uint32_t*) {
        void* component = g_createPersist(&kPersistClassId);
        if (component == nullptr) {
            return nullptr;
        }
        auto* store = new (std::nothrow) EntityDataStore();
        if (store == nullptr) {
            g_destroyBase(component, 1);
            return nullptr;
        }
        *static_cast<const void**>(component) = g_vtable.data();
        StoreField(component) = store;
        return component;
    }

    // The entity's component of a class, or null; with `create`, an entity without one is given one.
    void* ComponentOf(void* entity, uint32_t classId, bool create) {
        if (!g_ready || entity == nullptr) {
            return nullptr;
        }
        DWORD code = 0;
        void* component = nullptr;
        if (!SehCall(&code, g_lockEntity, entity) ||
            !SehCallRet(&code, &component, g_getComponent, entity, &classId) ||
            (component == nullptr && create &&
             !SehCallRet(&code, &component, g_createComponent, entity, &classId))) {
            char line[128];
            std::snprintf(line, sizeof(line),
                           "EntityData: reaching an entity's components faulted (0x%08lX)", code);
            Log::Loader(line);
            return nullptr;
        }
        return component;
    }

    bool Fail(const char* why) {
        Log::Loader(std::string("EntityData: ") + why +
                    " - plugins cannot keep data on entities this run");
        return false;
    }

}

bool EntityDataComponent::Install() {
    const uintptr_t system = AddressLibrary::Address(Symbols::kEntitySystem);
    const auto registerClass =
        AddressLibrary::Function<RegisterFn>(Symbols::kRegisterComponentClass);
    g_getComponent = AddressLibrary::Function<ComponentFn>(Symbols::kGetComponent);
    g_createComponent = AddressLibrary::Function<ComponentFn>(Symbols::kCreateComponent);
    g_lockEntity = AddressLibrary::Function<LockFn>(Symbols::kEntityLock);
    if (system == 0 || registerClass == nullptr || g_getComponent == nullptr ||
        g_createComponent == nullptr || g_lockEntity == nullptr) {
        return Fail("the entity system has no address on this game build");
    }

    DWORD code = 0;
    void* entitySystem = nullptr;
    if (!SehReadPointer(reinterpret_cast<void*>(system), 0, &entitySystem, &code) ||
        entitySystem == nullptr) {
        return Fail("the entity system cannot be read");
    }
    void* factory = static_cast<char*>(entitySystem) + kComponentFactoryOffset;

    const auto* entries = Field<const FactoryEntry*>(factory, kFactoryEntriesOffset);
    const uint32_t entryCount = Field<uint32_t>(factory, kFactoryCountOffset);
    for (uint32_t i = 0; i < entryCount && g_createPersist == nullptr; ++i) {
        if (entries[i].classId == kPersistClassId) {
            g_createPersist = entries[i].create;
        }
    }
    if (g_createPersist == nullptr) {
        return Fail("CPersistComponent is not in the component factory");
    }

    // A throwaway CPersistComponent supplies the vtable, the base destructor and the ids above it.
    void* probe = g_createPersist(&kPersistClassId);
    if (probe == nullptr) {
        return Fail("the engine would not create a CPersistComponent");
    }
    const auto* persistVtable = *static_cast<const void* const* const*>(probe);
    const HierarchyInfo persist = *CallSlot<const HierarchyInfo*>(probe, kComponentHierarchySlot);
    std::copy_n(persistVtable, kComponentVtableSlots, g_vtable.begin());
    g_destroyBase = reinterpret_cast<DestroyFn>(persistVtable[kComponentDestroySlot]);
    g_destroyBase(probe, 1);
    if (persist.count != 3 || persist.ids[2] != kPersistClassId) {
        return Fail("CPersistComponent is not shaped as expected on this game build");
    }

    g_hierarchy = {kClassName, 3, {persist.ids[0], persist.ids[1], kClassId}};
    g_vtable[kComponentDestroySlot] = RawFunctionPointer(&ComponentThunk::Destroy);
    g_vtable[kComponentHierarchySlot] = RawFunctionPointer(&ComponentThunk::Hierarchy);
    g_vtable[kComponentDescriptorSlot] = RawFunctionPointer(&ComponentThunk::GetDescriptor);
    g_memberVtable = {
        RawFunctionPointer(&MemberThunk::Load),     RawFunctionPointer(&MemberThunk::Save),
        RawFunctionPointer(&MemberThunk::LoadState), RawFunctionPointer(&MemberThunk::SaveState),
        RawFunctionPointer(&MemberThunk::Describe), RawFunctionPointer(&MemberThunk::Id),
        RawFunctionPointer(&MemberThunk::Set),      RawFunctionPointer(&MemberThunk::Get),
    };

    if (!registerClass(factory, &kClassId, &CreateDataComponent, false)) {
        return Fail("another component already claims CFCSEDataComponent's class id");
    }

    g_ready = true;
    char line[128];
    std::snprintf(line, sizeof(line), "EntityData: registered %s (class id 0x%08X)", kClassName,
                   kClassId);
    Log::Loader(line);
    return true;
}

std::optional<EntityValue> EntityDataComponent::Read(void* entity, uint32_t key) {
    std::optional<EntityValue> found;
    WithStore(ComponentOf(entity, kClassId, false), [&](EntityDataStore& store) {
        if (const EntityValue* value = store.Find(key)) {
            found = *value;
        }
        return true;
    });
    return found;
}

bool EntityDataComponent::Write(void* entity, uint32_t key, EntityValue value) {
    return WithStore(ComponentOf(entity, kClassId, true), [&](EntityDataStore& store) {
        store.state.insert_or_assign(key, std::move(value));
        return true;
    });
}

bool EntityDataComponent::Erase(void* entity, uint32_t key) {
    return WithStore(ComponentOf(entity, kClassId, false),
                     [&](EntityDataStore& store) { return store.state.erase(key) > 0; });
}

bool EntityDataComponent::Persist(void* entity) {
    void* persist = ComponentOf(entity, kPersistClassId, true);
    if (persist == nullptr) {
        return false;
    }
    uint32_t& level = Field<uint32_t>(persist, kPersistLevelOffset);
    level = (std::max)(level, kPersistLevelFull);
    return true;
}

void* EntityDataComponent::EntityOf(void* component) {
    DWORD code = 0;
    void* proxy = nullptr;
    void* entity = nullptr;
    if (component == nullptr || !SehReadPointer(component, kComponentProxyOffset, &proxy, &code) ||
        proxy == nullptr || !SehReadPointer(proxy, kProxyEntityOffset, &entity, &code)) {
        return nullptr;
    }
    return entity;
}

}
