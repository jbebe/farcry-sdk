// The player's two models in first person on foot. See body.h.
#include "body.h"

#include "engine/camera.h"
#include "engine/graphic.h"
#include "engine/memory.h"
#include "engine/pawn.h"
#include "engine/skeleton.h"
#include "fcse_api.h"

#include <algorithm>
#include <array>
#include <atomic>

namespace {
    namespace Camera = PlayerBody::Camera;
    namespace Graphic = PlayerBody::Graphic;
    namespace Pawn = PlayerBody::Pawn;
    namespace Skeleton = PlayerBody::Skeleton;

    constexpr uint32_t kPelvis = FCSE::Crc32("Pelvis");
    constexpr uint32_t kSpine = FCSE::Crc32("Spine");
    constexpr uint32_t kCamera = FCSE::Crc32("Camera");
    constexpr uint32_t kLeftThigh = FCSE::Crc32("L Thigh");
    constexpr uint32_t kRightThigh = FCSE::Crc32("R Thigh");

    constexpr size_t kMostBones = 256;

    // What the skinning job trims: the skeleton's bones, each model's inverse binds, which tell the
    // models apart, the bones each collapses its trimmed vertices to, and the hips and legs, which
    // the full body keeps and the avatar trims. Two copies, so the job never reads one half written.
    struct Trim {
        const uint8_t* bones;
        const float* avatarBinds;
        const float* bodyBinds;
        uint32_t boneCount;
        int avatarCentre;
        int bodyCentre;
        std::array<bool, kMostBones> legs;
    };
    Trim g_trims[2] = {};
    std::atomic<const Trim*> g_trim{nullptr};

    std::atomic<bool> g_enabled{true};

    // How the player is drawn on foot in first person: as the engine draws him, with the legs under
    // the first-person view, or whole, seen from a camera away from the eye such as the free camera.
    enum class View { Engine, Legs, Whole };

    // The pawn last followed, and how it is drawn.
    uint8_t* g_player = nullptr;
    View g_view = View::Engine;

    // Metres the camera stands off the eye beyond which it sees the player from outside.
    constexpr float kOutside = 1.0f;

    // Whether the camera the player sees through stands away from the eye.
    bool Outside(uint8_t* pawn, uint8_t* graphic) {
        uint8_t* skeleton = Graphic::Skeleton(graphic, false);
        uint8_t* entity = Pawn::Entity(pawn);
        const int eye = skeleton != nullptr ? Skeleton::Find(skeleton, kCamera) : -1;
        float view[3];
        if (eye < 0 || entity == nullptr || !Camera::Place(view)) {
            return false;
        }
        const float* m = &PlayerBody::Field<float>(entity, Pawn::kEntityMatrix);
        const float* place = Skeleton::Posed(skeleton, eye) + 12;
        float off = 0.0f;
        for (int k = 0; k < 3; ++k) {
            const float d = place[0] * m[k] + place[1] * m[4 + k] + place[2] * m[8 + k] + m[12 + k] - view[k];
            off += d * d;
        }
        return off > kOutside * kOutside;
    }

    View Wanted(uint8_t* pawn, uint8_t* graphic, bool firstPerson) {
        if (!g_enabled || !firstPerson || graphic == nullptr ||
            Graphic::Count(graphic) <= Graphic::kAvatar || !Pawn::OnFoot(pawn)) {
            return View::Engine;
        }
        return Outside(pawn, graphic) ? View::Whole : View::Legs;
    }

    // The hips and the legs; bones come after their parents.
    void MarkLegs(uint8_t* skeleton, std::array<bool, kMostBones>& legs) {
        legs.fill(false);
        for (const uint32_t thigh : {kLeftThigh, kRightThigh}) {
            const int bone = Skeleton::Find(skeleton, thigh);
            if (bone >= 0) {
                legs[bone] = true;
            }
        }
        const uint32_t count = Skeleton::Count(skeleton);
        for (uint32_t i = 0; i < count; ++i) {
            const int parent = Skeleton::Parent(skeleton, i);
            if (parent >= 0 && static_cast<uint32_t>(parent) < i && legs[parent]) {
                legs[i] = true;
            }
        }
        const int pelvis = Skeleton::Find(skeleton, kPelvis);
        if (pelvis >= 0) {
            legs[pelvis] = true;
        }
    }

    // Publishes what `graphic`'s models trim, unless it is already; nothing while either is not loaded.
    void Publish(uint8_t* graphic) {
        uint8_t* skeleton = Graphic::Skeleton(graphic, false);
        Graphic::Skin avatar;
        Graphic::Skin body;
        if (skeleton == nullptr || Skeleton::Count(skeleton) > kMostBones ||
            !Graphic::SkinOf(graphic, Graphic::kAvatar, avatar) ||
            !Graphic::SkinOf(graphic, Graphic::kFullBody, body)) {
            g_trim = nullptr;
            return;
        }
        const Trim* current = g_trim;
        if (current != nullptr && current->bones == Skeleton::Bones(skeleton) &&
            current->avatarBinds == avatar.inverseBinds && current->bodyBinds == body.inverseBinds) {
            return;
        }
        Trim& trim = g_trims[current == &g_trims[0] ? 1 : 0];
        trim.bones = Skeleton::Bones(skeleton);
        trim.avatarBinds = avatar.inverseBinds;
        trim.bodyBinds = body.inverseBinds;
        trim.boneCount = Skeleton::Count(skeleton);
        trim.avatarCentre = Skeleton::Find(skeleton, kSpine);
        trim.bodyCentre = Skeleton::Find(skeleton, kPelvis);
        MarkLegs(skeleton, trim.legs);
        g_trim = &trim;
    }

    // Draws the player as `view` wants, which the engine undoes each time it switches models.
    void Draw(uint8_t* graphic, bool firstPerson, View view) {
        constexpr const char* kNames[] = {"as the engine draws it", "with legs", "whole"};
        if (view != g_view) {
            FCSE::Logf("body: drawn %s", kNames[static_cast<int>(view)]);
        }
        if (view == View::Engine) {
            Graphic::LetGo();
            if (graphic != nullptr && Graphic::Count(graphic) > Graphic::kAvatar && firstPerson) {
                Graphic::SetVisible(graphic, Graphic::kFullBody, false);
                Graphic::SetVisible(graphic, Graphic::kAvatar, true);
            }
        } else {
            Graphic::KeepInWorld(graphic, Graphic::kFullBody);
            Graphic::SetVisible(graphic, Graphic::kAvatar, view == View::Legs);
        }
        if (view == View::Legs) {
            Publish(graphic);
        } else {
            g_trim = nullptr;
        }
        g_view = view;
    }

    // The 4x4 that takes every vertex to the bone's place.
    void Collapse(float* m, const float* posed) {
        std::fill(m, m + 12, 0.0f);
        std::copy_n(posed + 12, 3, m + 12);
        m[15] = 1.0f;
    }
}

void PlayerBody::Body::SetEnabled(bool enabled) {
    g_enabled = enabled;
}

bool PlayerBody::Body::Follow(uint8_t* pawn) {
    const bool newPawn = pawn != g_player;
    g_player = pawn;
    const bool firstPerson = Pawn::FirstPerson(pawn);
    uint8_t* graphic = Pawn::Graphic(pawn);
    const View view = Wanted(pawn, graphic, firstPerson);
    if (newPawn || view != g_view) {
        Draw(graphic, firstPerson, view);
    } else if (view == View::Legs) {
        Publish(graphic);
    }
    return g_view != View::Engine;
}

void PlayerBody::Body::OnViewMode(uint8_t* pawn, bool firstPerson) {
    if (pawn == g_player) {
        uint8_t* graphic = Pawn::Graphic(pawn);
        Draw(graphic, firstPerson, Wanted(pawn, graphic, firstPerson));
    }
}

void PlayerBody::Body::OnSkinned(float* out, const uint8_t* bones, const float* inverseBinds,
                                 const int32_t* slotBones, uint32_t count) {
    const Trim* trim = g_trim.load();
    if (trim == nullptr || bones != trim->bones) {
        return;
    }
    const bool avatar = inverseBinds == trim->avatarBinds;
    const int centre = avatar ? trim->avatarCentre : trim->bodyCentre;
    if ((!avatar && inverseBinds != trim->bodyBinds) || centre < 0) {
        return;
    }
    const float* point = Skeleton::PosedIn(bones, centre);
    for (uint32_t i = 0; i < count; ++i) {
        const int32_t bone = slotBones[i];
        if (bone >= 0 && static_cast<uint32_t>(bone) < trim->boneCount && trim->legs[bone] == avatar) {
            Collapse(out + 16 * i, point);
        }
    }
}
