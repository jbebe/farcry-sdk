// The player's graphic component. Putting the first-person view up hides the full body from the
// player's viewport and then discards it, so that call is taken out. The model switch and the
// component's near depth range are set again by many callers, so the plugin follows each of them.
#include "engine/graphic.h"

#include "engine/memory.h"
#include "fcse_api.h"

#include <algorithm>
#include <cstddef>
#include <iterator>

namespace {
    using PlayerBody::Field;

    // The model switch's first-person branch: the full body masked out of the player's viewport,
    // SetObjectVisible(1, true) on the avatar, then RemoveObject(0) on the full body, 9 bytes that
    // another plugin may already have taken out.
    FCSE::Relocation<uint8_t*> g_firstPersonBranch{FCSE::Pattern(
        "8B 4C 24 14 F7 D1 51 8B 48 34 E8 ?? ?? ?? ?? 6A 01 6A 01 8B CF E8 ?? ?? ?? ?? "
        "?? ?? ?? ?? ?? ?? ?? ?? ?? 5F 5E 5D 5B C2 04 00")};
    constexpr ptrdiff_t kDiscard = 0x1A;
    constexpr size_t kDiscardSize = 9;
    constexpr uint8_t kDiscardStart[] = {0x6A, 0x00, 0x8B, 0xCF, 0xE8};
    constexpr uint8_t kNop = 0x90;

    // The model switch itself: __thiscall on the pawn's data, with the player's viewport, RET 4. The
    // data holds the pawn first, and whether the full body is wanted at +0x49A.
    using SwitchFn = void(__fastcall*)(uint8_t* data, void* unused, uint32_t viewport);
    FCSE::Relocation<SwitchFn> g_switch{FCSE::Uplay(0x0007FC80)};
    SwitchFn g_originalSwitch = nullptr;
    constexpr ptrdiff_t kDataPawn = 0x00;
    constexpr ptrdiff_t kFullBodyWanted = 0x49A;

    // The component's near depth range put on each of its models. __fastcall on the component.
    using NearDepthFn = void(__fastcall*)(uint8_t* graphic);
    FCSE::Relocation<NearDepthFn> g_nearDepth{FCSE::Uplay(0x0051A6E0)};
    NearDepthFn g_originalNearDepth = nullptr;

    // On a model's scene instance helper, the viewports it is drawn in, and those it is drawn in the
    // near depth range in; and a model's visibility on the component. All __thiscall.
    using SetMaskFn = void(__fastcall*)(uint8_t* helper, void* unused, uint32_t mask);
    FCSE::Relocation<SetMaskFn> g_setViewports{FCSE::Uplay(0x00336B50)};
    FCSE::Relocation<SetMaskFn> g_setNearViewports{FCSE::Uplay(0x00336B10)};
    using SetObjectVisibleFn = bool(__fastcall*)(uint8_t* graphic, void* unused, uint32_t index,
                                                 bool visible);
    FCSE::Relocation<SetObjectVisibleFn> g_setObjectVisible{FCSE::Uplay(0x0051A440)};

    // CGraphicComponent: its models and how many; a model's scene instance helper, and the
    // helper's CGeometryResource.
    constexpr ptrdiff_t kObjects = 0x30;
    constexpr ptrdiff_t kObjectCount = 0x34;
    constexpr ptrdiff_t kObjectHelper = 0x34;
    constexpr ptrdiff_t kHelperGeometry = 0x10;
    // The resource's mesh as loaded, and in it the inverse bind matrices, 4x4 each, and how many.
    constexpr ptrdiff_t kLoaded = 0x38;
    constexpr ptrdiff_t kInverseBinds = 0x70;
    constexpr ptrdiff_t kInverseBindCount = 0x74;
    // The component's skinned meshes, one entry each: the bone of each skin slot, how many slots,
    // and the geometry resource.
    constexpr ptrdiff_t kSkins = 0x120;
    constexpr ptrdiff_t kSkinCount = 0x124;
    constexpr ptrdiff_t kSkinBones = 0x00;
    constexpr ptrdiff_t kSkinSlots = 0x04;
    constexpr ptrdiff_t kSkinGeometry = 0x14;

    // The skeleton through the component's vtable: to read, and to change, which has its bones
    // posed again. __thiscall.
    using SkeletonFn = uint8_t*(__fastcall*)(uint8_t* graphic);
    constexpr ptrdiff_t kSkeletonSlot = 0x78 / 4;
    constexpr ptrdiff_t kReposeSlot = 0x7C / 4;

    PlayerBody::Graphic::ViewModeFn g_viewMode = nullptr;
    // The model kept out of the near depth range, and its component. Game thread.
    uint8_t* g_kept = nullptr;
    unsigned g_keptIndex = 0;

    // A model's scene instance helper, or null.
    uint8_t* Helper(uint8_t* graphic, unsigned index) {
        uint8_t* object = index < PlayerBody::Graphic::Count(graphic)
                              ? Field<uint8_t**>(graphic, kObjects)[index]
                              : nullptr;
        return object != nullptr ? Field<uint8_t*>(object, kObjectHelper) : nullptr;
    }

    void __fastcall SwitchDetour(uint8_t* data, void* unused, uint32_t viewport) {
        g_originalSwitch(data, unused, viewport);
        g_viewMode(Field<uint8_t*>(data, kDataPawn), Field<uint8_t>(data, kFullBodyWanted) == 0);
    }

    void __fastcall NearDepthDetour(uint8_t* graphic) {
        g_originalNearDepth(graphic);
        if (graphic == g_kept) {
            if (uint8_t* helper = Helper(graphic, g_keptIndex)) {
                g_setNearViewports(helper, nullptr, 0);
            }
        }
    }
}

bool PlayerBody::Graphic::Install(ViewModeFn viewMode) {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();
    if (!g_firstPersonBranch || !g_switch || !g_nearDepth || !g_setViewports || !g_setNearViewports ||
        !g_setObjectVisible) {
        api->Log("graphic: the model switch or the calls drawing a model were not found");
        return false;
    }
    uint8_t* discard = g_firstPersonBranch.get() + kDiscard;
    if (!std::all_of(discard, discard + kDiscardSize, [](uint8_t b) { return b == kNop; })) {
        if (!std::equal(std::begin(kDiscardStart), std::end(kDiscardStart), discard)) {
            api->Log("graphic: the full body's discard is not the call this plugin knows");
            return false;
        }
        uint8_t nops[kDiscardSize];
        std::fill(std::begin(nops), std::end(nops), kNop);
        if (!api->Patch(discard, nops, kDiscardSize)) {
            return false;
        }
    }
    g_viewMode = viewMode;
    if (!Hook(g_switch.address(), &SwitchDetour, &g_originalSwitch)) {
        return false;
    }
    // A hook is live from here, so this only logs.
    if (!Hook(g_nearDepth.address(), &NearDepthDetour, &g_originalNearDepth)) {
        api->Log("graphic: the near depth range cannot be followed, so a weapon's throw may put the "
                 "legs in front of walls until the view mode next changes");
    }
    return true;
}

uint32_t PlayerBody::Graphic::Count(uint8_t* graphic) {
    return Field<uint32_t>(graphic, kObjectCount);
}

void PlayerBody::Graphic::KeepInWorld(uint8_t* graphic, unsigned index) {
    uint8_t* helper = Helper(graphic, index);
    if (helper == nullptr) {
        return;
    }
    g_kept = graphic;
    g_keptIndex = index;
    g_setViewports(helper, nullptr, 0xFFFFFFFFu);
    g_setNearViewports(helper, nullptr, 0);
    g_setObjectVisible(graphic, nullptr, index, true);
}

void PlayerBody::Graphic::LetGo() {
    g_kept = nullptr;
}

void PlayerBody::Graphic::SetVisible(uint8_t* graphic, unsigned index, bool visible) {
    if (Helper(graphic, index) != nullptr) {
        g_setObjectVisible(graphic, nullptr, index, visible);
    }
}

uint8_t* PlayerBody::Graphic::Skeleton(uint8_t* graphic, bool repose) {
    return Field<SkeletonFn*>(graphic, 0)[repose ? kReposeSlot : kSkeletonSlot](graphic);
}

bool PlayerBody::Graphic::SkinOf(uint8_t* graphic, unsigned index, Skin& out) {
    uint8_t* helper = Helper(graphic, index);
    uint8_t* geometry = helper != nullptr ? Field<uint8_t*>(helper, kHelperGeometry) : nullptr;
    uint8_t* loaded = geometry != nullptr ? Field<uint8_t*>(geometry, kLoaded) : nullptr;
    if (loaded == nullptr) {
        return false;
    }
    uint8_t** skins = Field<uint8_t**>(graphic, kSkins);
    const uint32_t skinCount = Field<uint32_t>(graphic, kSkinCount);
    for (uint32_t i = 0; i < skinCount; ++i) {
        if (skins[i] != nullptr && Field<uint8_t*>(skins[i], kSkinGeometry) == geometry) {
            out.inverseBinds = Field<const float*>(loaded, kInverseBinds);
            out.bones = Field<const int32_t*>(skins[i], kSkinBones);
            out.count = (std::min)(Field<uint32_t>(skins[i], kSkinSlots),
                                   Field<uint32_t>(loaded, kInverseBindCount));
            return out.inverseBinds != nullptr && out.bones != nullptr && out.count > 0;
        }
    }
    return false;
}
