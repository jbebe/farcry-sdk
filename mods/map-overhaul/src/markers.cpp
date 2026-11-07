#include "markers.h"

#include "fcse_api.h"

#include <cmath>
#include <cstddef>
#include <cstdint>
#include <unordered_map>

namespace MapOverhaul::Markers {

namespace {
    using ProcessFn = void(__fastcall*)(uint8_t* strategy, void* unused, uint8_t* marker, void* pawn, int sector,
                                        uint32_t* count);
    using PlaceFn = bool(__fastcall*)(uint8_t* strategy, void* unused, int sector, const float* from, const float* at,
                                      float* out, float* angle, bool* outside);
    using CategoryFn = const uint32_t*(__fastcall*)(void* equipment, void* unused);
    using LockFn = void(__fastcall*)(uint8_t* entity, void* unused);
    using BindFn = void(__fastcall*)(uint8_t* parent, void* unused, uint8_t* child, const uint32_t* bone, int zero,
                                     int no);

    // CGadgetMapStrategy's ProcessMarker, which places one marker's icons on the map every frame the map is
    // out, and the WorldSpaceToGadgetMapSpace it asks where; CEntity::Lock, which finishes an entity's
    // loading before it is used, and CEntity::Bind, which hangs an entity on a bone of another, taking a
    // handle by value it lets go of.
    FCSE::Relocation<uint8_t*> g_processSite{FCSE::Uplay(0x006F1640)};
    FCSE::Relocation<uint8_t*> g_placeSite{FCSE::Uplay(0x006ED0B0)};
    FCSE::Relocation<LockFn> g_lock{FCSE::Uplay(0x004DD4F0)};
    FCSE::Relocation<BindFn> g_bind{FCSE::Uplay(0x004E11B0)};
    ProcessFn g_process = nullptr;
    PlaceFn g_place = nullptr;

    // The strategy's equipment and the equipment's category, the map's being "map"; the bone hash an icon
    // is hung on as it is spawned, the map's lower-left corner; and its markers' records, each the marker's
    // handle and then, at +8 and +0xC, the handles of its icon and of a second one some markers have.
    constexpr ptrdiff_t kStrategyEquipment = 0x38;
    constexpr size_t kCategorySlot = 0x8C / sizeof(void*);
    constexpr uint32_t kMap = FCSE::Crc32("map");
    constexpr ptrdiff_t kSpawnBone = 0xC0;
    constexpr ptrdiff_t kRecords = 0x14C;
    constexpr ptrdiff_t kRecordCount = 0x150;
    constexpr ptrdiff_t kRecordSize = 0x1C;
    constexpr ptrdiff_t kRecordIcons[] = {0x08, 0x0C};
    // A handle's count of holders and its entity; the equipment's handle of its own entity.
    constexpr ptrdiff_t kHandleCount = 0x08;
    constexpr ptrdiff_t kHandleEntity = 0x0C;
    constexpr ptrdiff_t kEquipmentEntity = 0x08;

    // The sheet as render_map.py draws it and sheet.py folds it: the world from 660 to 4460 m each way at
    // 1:3 600, its middle the root's, cut in 3x3 panels, the side columns held turned 30 degrees toward the
    // eye and the folds between the columns rounded over 2 cm either side; the icons stand a little in front
    // of the paper.
    constexpr float kMiddle = 2560.0f;
    constexpr float kScale = 3600.0f;
    constexpr float kSize = 3800.0f / kScale;
    constexpr float kPanel = kSize / 3.0f;
    constexpr float kHeld = 0.52359878f;
    constexpr float kRound = 0.02f;
    constexpr float kLift = 0.002f;
    // Each panel's bone by row from the bottom and column from the left.
    constexpr uint32_t kPanels[3][3] = {
        {FCSE::Crc32("PanelBL"), FCSE::Crc32("PanelB"), FCSE::Crc32("PanelBR")},
        {FCSE::Crc32("PanelL"), FCSE::Crc32("PanelC"), FCSE::Crc32("PanelR")},
        {FCSE::Crc32("PanelTL"), FCSE::Crc32("PanelT"), FCSE::Crc32("PanelTR")},
    };

    // On the game thread, while a marker of the map is placed: the strategy, and the panel the marker lies
    // on once asked; and each icon entity's panel, as last hung.
    uint8_t* g_strategy = nullptr;
    uint32_t g_panel = 0;
    std::unordered_map<uint8_t*, uint32_t> g_hung;

    template <class T>
    T& Field(const void* object, ptrdiff_t offset) {
        return *reinterpret_cast<T*>(static_cast<uint8_t*>(const_cast<void*>(object)) + offset);
    }

    bool IsMap(uint8_t* strategy) {
        void* equipment = Field<void*>(strategy, kStrategyEquipment);
        return equipment != nullptr && *Field<CategoryFn*>(equipment, 0)[kCategorySlot](equipment, nullptr) == kMap;
    }

    // The record of a marker, matched as the game matches it.
    uint8_t* RecordOf(uint8_t* strategy, const uint8_t* marker) {
        uint8_t* records = Field<uint8_t*>(strategy, kRecords);
        const uint32_t count = Field<uint32_t>(strategy, kRecordCount);
        for (uint32_t i = 0; i < count; ++i) {
            uint8_t* record = records + i * kRecordSize;
            const uint8_t* handle = Field<uint8_t*>(record, 0);
            if (Field<uint32_t>(handle, 0) == Field<uint32_t>(marker, 0) &&
                Field<uint32_t>(handle, 4) == Field<uint32_t>(marker, 4) &&
                Field<uint8_t*>(handle, kHandleEntity) == Field<uint8_t*>(marker, kHandleEntity)) {
                return record;
            }
        }
        return nullptr;
    }

    uint8_t* IconOf(uint8_t* record, ptrdiff_t slot) {
        return record != nullptr ? Field<uint8_t*>(Field<uint8_t*>(record, slot), kHandleEntity) : nullptr;
    }

    // v turned about the y axis.
    void TurnAboutY(float* v, float angle) {
        const float c = std::cos(angle), s = std::sin(angle);
        const float x = v[0] * c + v[2] * s, z = -v[0] * s + v[2] * c;
        v[0] = x;
        v[2] = z;
    }

    // Where a world position lies on the held sheet: its panel, and the spot in that panel's frame, as
    // sheet.py's held() shapes the paper.
    uint32_t Place(const float* at, float* out, bool* outside) {
        float x = (at[0] - kMiddle) / kScale;
        float y = (at[1] - kMiddle) / kScale;
        const float half = kSize / 2.0f, fold = kPanel / 2.0f;
        *outside = std::fabs(x) > half || std::fabs(y) > half;
        x = std::fmax(-half, std::fmin(half, x));
        y = std::fmax(-half, std::fmin(half, y));
        const int column = x < -fold ? -1 : x > fold ? 1 : 0;
        const int row = y < -fold ? -1 : y > fold ? 1 : 0;
        // In the root's frame: flat in the middle column, along an arc over a rounded fold, turned beyond it.
        const float side = x > 0.0f ? 1.0f : -1.0f, from = std::fabs(x);
        const float radius = kRound / std::tan(kHeld / 2.0f);
        float across, toward, bent;
        if (from <= fold - kRound) {
            across = from, toward = 0.0f, bent = 0.0f;
        } else if (from <= fold + kRound) {
            bent = kHeld * (from - (fold - kRound)) / (2.0f * kRound);
            across = fold - kRound + radius * std::sin(bent), toward = radius * (1.0f - std::cos(bent));
        } else {
            across = fold + (from - fold) * std::cos(kHeld), toward = (from - fold) * std::sin(kHeld), bent = kHeld;
        }
        float point[3] = {side * across - column * fold, y - row * fold, toward};
        float normal[3] = {-side * std::sin(bent), 0.0f, std::cos(bent)};
        // Into the panel's frame, which the clips hold turned toward the eye on either side.
        const float unturn = column < 0 ? -kHeld : column > 0 ? kHeld : 0.0f;
        TurnAboutY(point, unturn);
        TurnAboutY(normal, unturn);
        for (int k = 0; k < 3; ++k) {
            out[k] = point[k] + kLift * normal[k];
        }
        return static_cast<uint32_t>((row + 1) * 3 + column + 1);
    }

    uint32_t BoneOf(uint32_t panel) {
        return kPanels[panel / 3][panel % 3];
    }

    // Hangs an icon already spawned on another panel on its marker's; the strategy places it there next frame.
    void Rehang(uint8_t* strategy, uint8_t* record, ptrdiff_t slot, uint32_t panel) {
        uint8_t* handle = Field<uint8_t*>(record, slot);
        uint8_t* map = Field<uint8_t*>(Field<uint8_t*>(Field<uint8_t*>(strategy, kStrategyEquipment),
                                                       kEquipmentEntity), kHandleEntity);
        if (map == nullptr) {
            return;
        }
        const uint32_t bone = BoneOf(panel);
        ++Field<int32_t>(handle, kHandleCount);
        g_lock(map, nullptr);
        g_bind(map, nullptr, handle, &bone, 0, 0);
    }

    bool __fastcall OnPlace(uint8_t* strategy, void* unused, int sector, const float* from, const float* at, float* out,
                            float* angle, bool* outside) {
        if (strategy != g_strategy) {
            return g_place(strategy, unused, sector, from, at, out, angle, outside);
        }
        g_panel = Place(at, out, outside);
        *angle = 0.0f;
        // An icon spawned after this is hung on the marker's panel; ProcessMarker puts the corner back.
        Field<uint32_t>(strategy, kSpawnBone) = BoneOf(g_panel);
        return true;
    }

    void __fastcall OnProcess(uint8_t* strategy, void* unused, uint8_t* marker, void* pawn, int sector,
                              uint32_t* count) {
        if (!IsMap(strategy)) {
            return g_process(strategy, unused, marker, pawn, sector, count);
        }
        uint8_t* before = RecordOf(strategy, marker);
        uint8_t* was[] = {IconOf(before, kRecordIcons[0]), IconOf(before, kRecordIcons[1])};
        const uint32_t corner = Field<uint32_t>(strategy, kSpawnBone);
        g_strategy = strategy;
        g_panel = UINT32_MAX;
        g_process(strategy, unused, marker, pawn, sector, count);
        g_strategy = nullptr;
        Field<uint32_t>(strategy, kSpawnBone) = corner;
        if (g_panel == UINT32_MAX) {
            return;
        }
        uint8_t* record = RecordOf(strategy, marker);
        for (int k = 0; k < 2; ++k) {
            uint8_t* icon = IconOf(record, kRecordIcons[k]);
            if (icon == nullptr) {
                continue;
            }
            const auto hung = g_hung.find(icon);
            if (icon != was[k]) {
                g_hung[icon] = g_panel;
            } else if (hung == g_hung.end() || hung->second != g_panel) {
                Rehang(strategy, record, kRecordIcons[k], g_panel);
                g_hung[icon] = g_panel;
            }
        }
    }

    template <class Fn>
    bool Hook(uintptr_t site, Fn detour, Fn* original) {
        return site != 0 && FCSE::ApiPointer()->Hook(reinterpret_cast<void*>(site), reinterpret_cast<void*>(detour),
                                                     reinterpret_cast<void**>(original));
    }
}

bool Install() {
    const bool hooked = g_lock && g_bind && Hook(g_placeSite.address(), &OnPlace, &g_place) &&
                        Hook(g_processSite.address(), &OnProcess, &g_process);
    FCSE::Logf("markers: %s", hooked ? "the map's icons at their places on the whole sheet, hung on its panels"
                                     : "off, the map's marker placing was not found");
    return hooked;
}

}
