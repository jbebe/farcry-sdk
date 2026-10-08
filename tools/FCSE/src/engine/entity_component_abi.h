#pragma once

#include <cstddef>
#include <cstdint>

// The engine ABI FCSE's entity-data component is built on: the component factory, a component's
// vtable and fields, a property member, and the serializable node the members read and write.
//
// What each number is and how it was established lives in
// docs/docs/engine-internals/fcse-entity-data-abi.md - this header is the list, not the reasoning.
// Retail MSVC layouts throughout: the dedicated server orders the same vtables differently.
namespace FCSE {

// CEntitySystem holds the component factory; the factory's CreateObject slot and its
// {class id, create function} array.
constexpr ptrdiff_t kComponentFactoryOffset = 0x5c;
constexpr size_t kFactoryCreateSlot = 1;
constexpr ptrdiff_t kFactoryEntriesOffset = 0x20;
constexpr ptrdiff_t kFactoryCountOffset = 0x24;

// CEntityComponent's primary vtable. 30 is exact: the next pointer along starts another class's.
constexpr size_t kComponentVtableSlots = 30;
constexpr size_t kComponentDestroySlot = 0;
constexpr size_t kComponentHierarchySlot = 1;
constexpr size_t kComponentDescriptorSlot = 3;

// CEntityComponent's entity proxy, and the entity the proxy holds.
constexpr ptrdiff_t kComponentProxyOffset = 0x08;
constexpr ptrdiff_t kProxyEntityOffset = 0x0c;

// CPersistComponent, which FCSE's component is built from: its persistence level, and the value
// the engine sets for an entity it carries between worlds. FCSE's component reuses the field to
// hold its store once its vtable has replaced CPersistComponent's.
constexpr ptrdiff_t kPersistLevelOffset = 0x10;
constexpr uint32_t kPersistLevelFull = 2;
constexpr ptrdiff_t kStoreOffset = 0x10;

// ISerializableNode.
constexpr size_t kNodeChildCountSlot = 5;
constexpr size_t kNodeChildSlot = 6;
constexpr size_t kNodeNewChildSlot = 10;
constexpr size_t kNodeTagSlot = 13;
constexpr size_t kNodeSetStringSlot = 25;
constexpr size_t kNodeSetFloatSlot = 38;
constexpr size_t kNodeSetIntSlot = 43;
constexpr size_t kNodeGetStringSlot = 54;
constexpr size_t kNodeGetFloatSlot = 67;
constexpr size_t kNodeGetIntSlot = 72;

// A node's key for a child or a value: the name, for nodes that keep names, and its CRC-32.
struct SerializationId {
    const char* name;
    uint32_t id;
};

// CMemberBase: Load, Save, LoadState, SaveState, GetDescription, GetID, Set, Get - all __thiscall.
constexpr size_t kMemberVtableSlots = 8;

struct Member {
    const void* const* vtable;
    const char* name;
    uint32_t id;
    uint32_t offset;
    uint32_t extra;
};

// CNomadObjectDescriptor: the list of a class's members. FCSE's is never grown.
struct Descriptor {
    const Member* const* members;
    uint32_t count;
    uint32_t capacity;
};

// What GetHierarchyInfo returns: the class's name and the ids of every class from CNomadObject
// down to its own, which is the last.
template <size_t Depth>
struct HierarchyInfo {
    const char* name;
    uint32_t count;
    uint32_t ids[Depth];
};

}
