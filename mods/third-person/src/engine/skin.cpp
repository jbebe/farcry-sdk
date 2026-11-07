// A model's skin matrices. The skinning job builds them for every skinned model of a graphic
// component, each from its own inverse bind matrices and its own map of slots to bones.
#include "engine/skin.h"

#include "engine/memory.h"
#include "fcse_api.h"

namespace {
    // One model's skin matrices: out, the skeleton's bones, the inverse binds, the slots' bones,
    // how many slots, and whether the posed matrices are copied as they are. __cdecl.
    using SkinFn = void(__cdecl*)(float* out, const uint8_t* bones, const float* inverseBinds,
                                  const int32_t* slotBones, uint32_t count, uint32_t asPosed);
    FCSE::Relocation<SkinFn> g_skin{FCSE::Uplay(0x0061F200)};
    SkinFn g_originalSkin = nullptr;

    ThirdPerson::Skin::SkinnedFn g_skinned = nullptr;

    void __cdecl SkinDetour(float* out, const uint8_t* bones, const float* inverseBinds,
                            const int32_t* slotBones, uint32_t count, uint32_t asPosed) {
        g_originalSkin(out, bones, inverseBinds, slotBones, count, asPosed);
        g_skinned(out, bones, inverseBinds, slotBones, count);
    }
}

bool ThirdPerson::Skin::Install(SkinnedFn skinned) {
    g_skinned = skinned;
    if (!Hook(g_skin.address(), &SkinDetour, &g_originalSkin)) {
        FCSE::ApiPointer()->Log("skin: the skinning cannot be hooked, so neither model is trimmed");
        return false;
    }
    return true;
}
