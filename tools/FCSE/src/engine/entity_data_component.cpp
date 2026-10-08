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
#include <new>
#include <variant>

namespace FCSE {

namespace {

    constexpr char kClassName[] = "CFCSEDataComponent";
    constexpr uint32_t kClassId = Crc32(kClassName);
    constexpr uint32_t kPersistClassId = Crc32("CPersistComponent");

    // An entry's value, under the name of its type.
    constexpr SerializationId kIntValue{"Int", Crc32("Int")};
    constexpr SerializationId kFloatValue{"Float", Crc32("Float")};
    constexpr SerializationId kStringValue{"String", Crc32("String")};

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

    std::recursive_mutex g_lock;
    bool g_ready = false;

    CreateFn g_createPersist = nullptr;
    DestroyFn g_destroyBase = nullptr;
    ComponentFn g_getComponent = nullptr;
    ComponentFn g_createComponent = nullptr;
    LockFn g_lockEntity = nullptr;

    std::array<const void*, kComponentVtableSlots> g_vtable{};
    std::array<const void*, kMemberVtableSlots> g_memberVtable{};
    HierarchyInfo<3> g_hierarchy{kClassName, 3, {}};
    const Member g_member{g_memberVtable.data(), "Entries", Crc32("Entries"), 0, 0};
    const Member* const g_members[] = {&g_member};
    const Descriptor g_descriptor{g_members, 1, 0};

    template <typename T>
    T& Field(void* object, ptrdiff_t offset) {
        return *reinterpret_cast<T*>(static_cast<char*>(object) + offset);
    }

    EntityDataStore*& StoreField(void* component) {
        return Field<EntityDataStore*>(component, kStoreOffset);
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

    // The overridden component slots. MSVC will not let a free function be __thiscall, so each is a
    // member of a type never instantiated: `this` is always the engine's component.
    struct ComponentThunk {
        void* Destroy(uint32_t flags);
        const HierarchyInfo<3>* Hierarchy();
        const Descriptor* GetDescriptor();
    };

    void* ComponentThunk::Destroy(uint32_t flags) {
        void* component = this;
        {
            std::lock_guard lock(g_lock);
            delete StoreField(component);
            StoreField(component) = nullptr;
        }
        return g_destroyBase(component, flags);
    }

    const HierarchyInfo<3>* ComponentThunk::Hierarchy() { return &g_hierarchy; }

    const Descriptor* ComponentThunk::GetDescriptor() { return &g_descriptor; }

    // The one member, which reads and writes the component node's children as entries. `this` is
    // g_member.
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
        std::lock_guard lock(g_lock);
        if (EntityDataStore* store = StoreField(component)) {
            ReadEntries(node, store->authored);
        }
    }

    void MemberThunk::Save(void* component, void* node) {
        std::lock_guard lock(g_lock);
        if (const EntityDataStore* store = StoreField(component)) {
            WriteEntries(node, store->Merged());
        }
    }

    void MemberThunk::LoadState(void* component, void* node) {
        std::lock_guard lock(g_lock);
        if (EntityDataStore* store = StoreField(component)) {
            ReadEntries(node, store->state);
        }
    }

    void MemberThunk::SaveState(void* component, void* node) {
        std::lock_guard lock(g_lock);
        if (const EntityDataStore* store = StoreField(component)) {
            WriteEntries(node, store->state);
        }
    }

    void MemberThunk::Describe(void*, void*) {}

    SerializationId* MemberThunk::Id(SerializationId* out) {
        const auto* member = reinterpret_cast<const Member*>(this);
        *out = {member->name, member->id};
        return out;
    }

    void MemberThunk::Set(void*, void*) {}

    void* MemberThunk::Get(void*) { return nullptr; }

    // Registered with the factory under kClassId. The engine's CPersistComponent is the starting
    // point because it is a complete component that needs nothing: its own fields are given over
    // to the store once its vtable is replaced.
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

    void LogFault(const char* what, DWORD code) {
        char line[160];
        std::snprintf(line, sizeof(line), "EntityData: %s faulted (0x%08lX)", what, code);
        Log::Loader(line);
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
        Log::Loader("EntityData: the entity system has no address on this game build - plugins "
                    "cannot keep data on entities this run");
        return false;
    }

    void* entitySystem = *reinterpret_cast<void**>(system);
    if (entitySystem == nullptr) {
        Log::Loader("EntityData: the entity system does not exist yet - plugins cannot keep data "
                    "on entities this run");
        return false;
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
        Log::Loader("EntityData: CPersistComponent is not in the component factory - plugins "
                    "cannot keep data on entities this run");
        return false;
    }

    // A throwaway CPersistComponent supplies the vtable to copy, the base destructor, and the
    // hierarchy ids above its own.
    void* probe = g_createPersist(&kPersistClassId);
    if (probe == nullptr) {
        Log::Loader("EntityData: the engine would not create a CPersistComponent");
        return false;
    }
    const auto* persistVtable = *static_cast<const void* const* const*>(probe);
    const auto* persistHierarchy = CallSlot<const HierarchyInfo<3>*>(probe, kComponentHierarchySlot);
    g_destroyBase = reinterpret_cast<DestroyFn>(persistVtable[kComponentDestroySlot]);
    const bool shapeAsExpected =
        persistHierarchy->count == 3 && persistHierarchy->ids[2] == kPersistClassId;
    if (shapeAsExpected) {
        std::copy_n(persistVtable, kComponentVtableSlots, g_vtable.begin());
        g_hierarchy.ids[0] = persistHierarchy->ids[0];
        g_hierarchy.ids[1] = persistHierarchy->ids[1];
        g_hierarchy.ids[2] = kClassId;
    }
    g_destroyBase(probe, 1);
    if (!shapeAsExpected) {
        Log::Loader("EntityData: CPersistComponent is not shaped as expected on this game build - "
                    "plugins cannot keep data on entities this run");
        return false;
    }

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
        Log::Loader("EntityData: another component already claims CFCSEDataComponent's class id - "
                    "plugins cannot keep data on entities this run");
        return false;
    }

    g_ready = true;
    char line[128];
    std::snprintf(line, sizeof(line), "EntityData: registered %s (class id 0x%08X)", kClassName,
                   kClassId);
    Log::Loader(line);
    return true;
}

std::unique_lock<std::recursive_mutex> EntityDataComponent::Lock() {
    return std::unique_lock(g_lock);
}

EntityDataStore* EntityDataComponent::StoreOf(void* entity, bool create) {
    if (!g_ready || entity == nullptr) {
        return nullptr;
    }
    DWORD code = 0;
    void* component = nullptr;
    if (!SehCall(&code, g_lockEntity, entity) ||
        !SehCallRet(&code, &component, g_getComponent, entity, &kClassId)) {
        LogFault("looking up an entity's data", code);
        return nullptr;
    }
    if (component == nullptr && create &&
        !SehCallRet(&code, &component, g_createComponent, entity, &kClassId)) {
        LogFault("giving an entity its data component", code);
        return nullptr;
    }
    return component != nullptr ? StoreField(component) : nullptr;
}

bool EntityDataComponent::Persist(void* entity) {
    if (!g_ready || entity == nullptr) {
        return false;
    }
    DWORD code = 0;
    void* persist = nullptr;
    if (!SehCall(&code, g_lockEntity, entity) ||
        !SehCallRet(&code, &persist, g_getComponent, entity, &kPersistClassId) ||
        (persist == nullptr &&
         !SehCallRet(&code, &persist, g_createComponent, entity, &kPersistClassId))) {
        LogFault("persisting an entity", code);
        return false;
    }
    if (persist == nullptr) {
        return false;
    }
    uint32_t& level = Field<uint32_t>(persist, kPersistLevelOffset);
    if (level < kPersistLevelFull) {
        level = kPersistLevelFull;
    }
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
