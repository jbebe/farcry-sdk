// Measured off each weapon's meshes from its aim bank's eye: its nearest detail's vertices, the
// lens and the raise; and the reticle drawn in it.
#include "scopes.h"

#include "engine/entity_name.h"
#include "shape_ar16.h"
#include "shape_mgl140.h"

namespace {
    using AimingOverhaul::Scopes::Reticle;
    using AimingOverhaul::Scopes::Scope;

    constexpr Reticle kHunter{"RETICLE_HUNTER", false};
    constexpr Reticle kPso{"RETICLE_PSO", false};
    constexpr Reticle kTactical{"RETICLE_TACTICAL", false};
    constexpr Reticle kHolosight{"RETICLE_HOLOSIGHT", true};

    // The black rim around the opening, as a share of its radius: thin, or thick on the Dragunov
    // and the AS50.
    constexpr float kThinRim = 0.15f;
    constexpr float kThickRim = 0.36f;

    constexpr Scope kScopes[] = {
        {"weapons.Special.Dart_Rifle", 9541, 2016, kThinRim, -0.00284f, -0.00148f, &kHunter, {},
         0.06f},
        {"weapons.Special.M1903", 7332, 1488, kThinRim, -0.00061f, -0.00025f, &kHunter, {},
         0.06f},
        {"weapons.Primary.Dragunov", 10167, 2088, kThickRim, -0.00254f, -0.02073f, &kPso, {},
         0.1819f},
        {"weapons.Primary.AS50", 11034, 1800, kThickRim, 0.00222f, -0.00248f, &kTactical, {},
         0.1058f},
        // The AR-16's eyecup is the MGL-140's, seen from further back.
        {"weapons.Primary.M16", 12361, 3113, kThinRim, -0.00474f, -0.00087f, &kHolosight,
         g_ar16Shape, 0.0f, 0.8f},
        {"weapons.Primary.MGL140", 13074, 2700, kThinRim, -0.00275f, 0.00213f, &kHolosight,
         g_mgl140Shape, 0.073f, 0.8f},
    };
}

const AimingOverhaul::Scopes::Scope* AimingOverhaul::Scopes::Find(const char* weaponName) {
    for (const Scope& scope : kScopes) {
        if (EntityName::Is(weaponName, scope.weapon)) {
            return &scope;
        }
    }
    return nullptr;
}
