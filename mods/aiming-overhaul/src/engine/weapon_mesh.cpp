// See docs/docs/engine-internals/first-person-aiming.md, "Where the scope is drawn from".
#include "engine/weapon_mesh.h"

#include "engine/memory.h"
#include "fcse_api.h"

#include <algorithm>
#include <excpt.h>
#include <iterator>

namespace {
    using AimingOverhaul::Field;

    // Both __thiscall, which a free function spells __fastcall with an unused EDX.
    using LockFn = void(__fastcall*)(void* entity, void* unused);
    using GetComponentFn = uint8_t*(__fastcall*)(void* entity, void* unused, const uint32_t* id);

    FCSE::Relocation<LockFn> g_lock{FCSE::Uplay(0x004DD4F0)};
    FCSE::Relocation<GetComponentFn> g_getComponent{FCSE::Uplay(0x004E2CC0)};

    constexpr uint32_t kGraphicComponent = FCSE::Crc32("CGraphicComponent");
    constexpr uint32_t kScopePart = FCSE::Crc32("SCOPE_HI");

    // CGraphicComponent: its parts and how many. A part: its name's hash, the hash of the node its
    // nearest detail is drawn by, and the helper holding its geometry resource.
    constexpr ptrdiff_t kParts = 0x30;
    constexpr ptrdiff_t kPartCount = 0x34;
    constexpr ptrdiff_t kPartName = 0x0C;
    constexpr ptrdiff_t kPartNearestNode = 0x2C;
    constexpr ptrdiff_t kPartHelper = 0x34;
    constexpr ptrdiff_t kHelperGeometry = 0x10;

    // CGeometryResource: the mesh as loaded. In it, the details nearest first, and the file's part
    // table, whose entries start with the hash of a node.
    constexpr ptrdiff_t kLoaded = 0x38;
    constexpr ptrdiff_t kDetailCount = 0x04;
    constexpr ptrdiff_t kDetails = 0x08;
    constexpr ptrdiff_t kPartTable = 0x60;
    constexpr size_t kPartEntry = 0x08;
    // A detail: its draws and how many, and the resource holding its vertices.
    constexpr ptrdiff_t kDraws = 0x0C;
    constexpr ptrdiff_t kDrawCount = 0x10;
    constexpr ptrdiff_t kDetailVertices = 0x1C;
    // A draw: its part table entry, and its first index.
    constexpr size_t kDrawEntry = 0x1C;
    constexpr ptrdiff_t kDrawPart = 0x04;
    constexpr ptrdiff_t kDrawStart = 0x0C;
    // A vertex resource's device buffer, and the Direct3D buffer in that.
    constexpr ptrdiff_t kDeviceBuffer = 0x0C;
    constexpr ptrdiff_t kDirect3DBuffer = 0x08;

    // The graphic component's SCOPE_HI part.
    uint8_t* FindScope(uint8_t* graphic) {
        uint8_t** parts = Field<uint8_t**>(graphic, kParts);
        const uint32_t count = Field<uint32_t>(graphic, kPartCount);
        for (uint32_t i = 0; i < count; i++) {
            if (parts[i] != nullptr && Field<uint32_t>(parts[i], kPartName) == kScopePart) {
                return parts[i];
            }
        }
        return nullptr;
    }

    // The draws of the node at the nearest detail of the loaded mesh.
    bool ReadNearest(uint8_t* loaded, uint32_t node, AimingOverhaul::WeaponMesh::ScopePart& part) {
        if (Field<uint32_t>(loaded, kDetailCount) == 0) {
            return false;
        }
        uint8_t* nearest = Field<uint8_t*>(loaded, kDetails);
        uint8_t* table = Field<uint8_t*>(loaded, kPartTable);
        uint8_t* draws = Field<uint8_t*>(nearest, kDraws);
        const uint32_t count = Field<uint32_t>(nearest, kDrawCount);
        for (uint32_t i = 0; i < count && part.startCount < std::size(part.starts); i++) {
            uint8_t* draw = draws + i * kDrawEntry;
            const uint32_t entry = Field<uint32_t>(draw, kDrawPart);
            if (Field<uint32_t>(table + entry * kPartEntry, 0) == node) {
                part.starts[part.startCount++] = Field<uint32_t>(draw, kDrawStart);
            }
        }
        part.vertices = Field<uint8_t*>(nearest, kDetailVertices);
        return part.vertices != nullptr && part.startCount > 0;
    }
}

bool AimingOverhaul::WeaponMesh::ScopePart::Draws(uint32_t start) const {
    return std::find(starts, starts + startCount, start) != starts + startCount;
}

bool AimingOverhaul::WeaponMesh::Read(void* entity, ScopePart& part) {
    part = {};
    if (entity == nullptr || !g_lock || !g_getComponent) {
        return false;
    }
    __try {
        g_lock(entity, nullptr);
        uint8_t* graphic = g_getComponent(entity, nullptr, &kGraphicComponent);
        uint8_t* scope = graphic != nullptr ? FindScope(graphic) : nullptr;
        uint8_t* helper = scope != nullptr ? Field<uint8_t*>(scope, kPartHelper) : nullptr;
        uint8_t* geometry = helper != nullptr ? Field<uint8_t*>(helper, kHelperGeometry) : nullptr;
        uint8_t* loaded = geometry != nullptr ? Field<uint8_t*>(geometry, kLoaded) : nullptr;
        return loaded != nullptr &&
               ReadNearest(loaded, Field<uint32_t>(scope, kPartNearestNode), part);
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        part = {};
        return false;
    }
}

const void* AimingOverhaul::WeaponMesh::VertexBuffer(uint8_t* resource) {
    __try {
        uint8_t* device = resource != nullptr ? Field<uint8_t*>(resource, kDeviceBuffer) : nullptr;
        return device != nullptr ? Field<void*>(device, kDirect3DBuffer) : nullptr;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return nullptr;
    }
}
