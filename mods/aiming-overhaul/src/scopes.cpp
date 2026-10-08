// A scope's look is entity data on its weapon's archetype, which the weapon fragments in
// layer\mods\ give it (written by data\fragments.py); its place is read from the weapon's mesh.
#include "scopes.h"

#include "engine/aim.h"
#include "fcse_api.h"
#include "shape_ar16.h"
#include "shape_mgl140.h"

#include <atomic>
#include <cstring>
#include <iterator>

namespace {
    using AimingOverhaul::Scopes::Reticle;
    using AimingOverhaul::Scopes::Scope;

    // The reticles and eyepiece shapes a scope's data can name.
    struct NamedReticle {
        const char* name;
        Reticle reticle;
    };
    constexpr NamedReticle kReticles[] = {
        {"hunter", {"RETICLE_HUNTER", false}},
        {"pso", {"RETICLE_PSO", false}},
        {"tactical", {"RETICLE_TACTICAL", false}},
        {"holosight", {"RETICLE_HOLOSIGHT", true}},
    };
    struct NamedShape {
        const char* name;
        std::span<const BYTE> shape;
    };
    constexpr NamedShape kShapes[] = {{"ar16", g_ar16Shape}, {"mgl140", g_mgl140Shape}};

    // The weapon last followed and its entity, whether its data says the plugin draws its scope but
    // its mesh is still to be read, and what was last missing from that.
    uint8_t* g_weapon = nullptr;
    void* g_entity = nullptr;
    bool g_unread = false;
    char g_missing[192] = {};

    // The scope being read, and the ones before it, which the render thread can still be using.
    Scope g_scopes[4] = {};
    size_t g_reading = 0;
    std::atomic<const Scope*> g_inHand{nullptr};

    template <class Entry, size_t N>
    const Entry* Named(const Entry (&entries)[N], const char* name) {
        for (const Entry& entry : entries) {
            if (std::strcmp(entry.name, name) == 0) {
                return &entry;
            }
        }
        FCSE::Logf("scopes: %s: nothing a scope can show is called \"%s\"",
                   AimingOverhaul::Aim::WeaponName(), name);
        return nullptr;
    }

    // The scope the entity's data describes, if it names a reticle the plugin has.
    bool ReadData(void* entity, Scope& scope) {
        const FCSE_EntityDataAPI* data = FCSE::ApiPointer()->EntityData;
        char name[32];
        const NamedReticle* reticle =
            data->GetString(entity, "AimingOverhaul.ScopeReticle", name, sizeof(name))
                ? Named(kReticles, name)
                : nullptr;
        if (reticle == nullptr) {
            return false;
        }
        scope = {&reticle->reticle};
        if (data->GetString(entity, "AimingOverhaul.ScopeShape", name, sizeof(name))) {
            const NamedShape* shape = Named(kShapes, name);
            if (shape == nullptr) {
                return false;
            }
            scope.shape = shape->shape;
        }
        data->GetFloat(entity, "AimingOverhaul.ScopeRim", &scope.rim);
        data->GetFloat(entity, "AimingOverhaul.ScopeLensX", &scope.lensX);
        data->GetFloat(entity, "AimingOverhaul.ScopeLensY", &scope.lensY);
        data->GetFloat(entity, "AimingOverhaul.ScopeRaise", &scope.raiseReach);
        data->GetFloat(entity, "AimingOverhaul.ScopeSize", &scope.size);
        return true;
    }
}

const Scope* AimingOverhaul::Scopes::Follow(uint8_t* weapon) {
    if (weapon != g_weapon) {
        g_weapon = weapon;
        g_entity = weapon != nullptr ? FCSE::ApiPointer()->EntityData->EntityOf(weapon) : nullptr;
        g_inHand = nullptr;
        g_reading = (g_reading + 1) % std::size(g_scopes);
        g_unread = g_entity != nullptr && ReadData(g_entity, g_scopes[g_reading]);
        g_missing[0] = '\0';
    }
    if (!g_unread) {
        return g_inHand;
    }
    Scope& reading = g_scopes[g_reading];
    const char* missing = WeaponMesh::Read(g_entity, reading.part);
    if (missing == nullptr) {
        g_unread = false;
        g_inHand = &reading;
        FCSE::Logf("scopes: %s is drawn from its eyepiece; its scope is %zu draws, the first from "
                   "index %u",
                   Aim::WeaponName(), reading.part.startCount, reading.part.starts[0]);
    } else if (std::strcmp(missing, g_missing) != 0) {
        std::strncpy(g_missing, missing, sizeof(g_missing) - 1);
        FCSE::Logf("scopes: %s: %s", Aim::WeaponName(), missing);
    }
    return g_inHand;
}

const Scope* AimingOverhaul::Scopes::InHand() {
    return g_inHand;
}
