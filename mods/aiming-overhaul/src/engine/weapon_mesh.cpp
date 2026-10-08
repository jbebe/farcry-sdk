// See docs/docs/engine-internals/first-person-aiming.md, "Where the scope is drawn from".
#include "engine/weapon_mesh.h"

#include "engine/memory.h"
#include "fcse_api.h"

#include <algorithm>
#include <cstdarg>
#include <cstdio>
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
    constexpr uint32_t kNearestScopeNode = FCSE::Crc32("SCOPE_HI_LOD0");

    // CGraphicComponent: its parts and how many. A part: its name's hash, and the helper holding
    // its geometry resource.
    constexpr ptrdiff_t kParts = 0x30;
    constexpr ptrdiff_t kPartCount = 0x34;
    constexpr ptrdiff_t kPartName = 0x0C;
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

    // What Read last found missing.
    char g_missing[192];

    const char* Missing(const char* format, ...) {
        va_list args;
        va_start(args, format);
        std::vsnprintf(g_missing, sizeof(g_missing), format, args);
        va_end(args);
        return g_missing;
    }

    // The scope's draws at the nearest detail of the loaded mesh; what is missing, if any.
    const char* ReadNearest(uint8_t* loaded, AimingOverhaul::WeaponMesh::ScopePart& part) {
        const uint32_t details = Field<uint32_t>(loaded, kDetailCount);
        if (details == 0) {
            return "no detail in its loaded mesh";
        }
        uint8_t* nearest = Field<uint8_t*>(loaded, kDetails);
        uint8_t* table = Field<uint8_t*>(loaded, kPartTable);
        uint8_t* draws = Field<uint8_t*>(nearest, kDraws);
        const uint32_t count = Field<uint32_t>(nearest, kDrawCount);
        for (uint32_t i = 0; i < count && part.startCount < std::size(part.starts); i++) {
            uint8_t* draw = draws + i * kDrawEntry;
            const uint32_t entry = Field<uint32_t>(draw, kDrawPart);
            if (Field<uint32_t>(table + entry * kPartEntry, 0) == kNearestScopeNode) {
                part.starts[part.startCount++] = Field<uint32_t>(draw, kDrawStart);
            }
        }
        part.vertices = Field<uint8_t*>(nearest, kDetailVertices);
        if (part.startCount == 0) {
            const uint32_t first = count > 0 ? Field<uint32_t>(draws, kDrawPart) : 0;
            return Missing("no SCOPE_HI_LOD0 draw among the %u at the nearest of %u details; the "
                           "first is of node %08X",
                           count, details, Field<uint32_t>(table + first * kPartEntry, 0));
        }
        return part.vertices == nullptr ? "no vertex resource at the nearest detail" : nullptr;
    }
}

bool AimingOverhaul::WeaponMesh::ScopePart::Draws(uint32_t start) const {
    return std::find(starts, starts + startCount, start) != starts + startCount;
}

const char* AimingOverhaul::WeaponMesh::Read(void* entity, ScopePart& part) {
    part = {};
    if (!g_lock || !g_getComponent) {
        return "CEntity::Lock or GetComponent is not mapped on this build";
    }
    __try {
        g_lock(entity, nullptr);
        uint8_t* graphic = g_getComponent(entity, nullptr, &kGraphicComponent);
        if (graphic == nullptr) {
            return "no graphic component";
        }
        uint8_t* scope = FindScope(graphic);
        if (scope == nullptr) {
            return Missing("no SCOPE_HI among its %u parts", Field<uint32_t>(graphic, kPartCount));
        }
        uint8_t* helper = Field<uint8_t*>(scope, kPartHelper);
        uint8_t* geometry = helper != nullptr ? Field<uint8_t*>(helper, kHelperGeometry) : nullptr;
        if (geometry == nullptr) {
            return Missing("no geometry resource on SCOPE_HI (helper %p)", helper);
        }
        uint8_t* loaded = Field<uint8_t*>(geometry, kLoaded);
        if (loaded == nullptr) {
            return Missing("geometry resource %p has no loaded mesh", geometry);
        }
        return ReadNearest(loaded, part);
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        part = {};
        return "a fault walking the engine's objects";
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
