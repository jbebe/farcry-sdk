// What the Armory tab can put in the player's hands, as data: the guns of the game's own
// CHEAT_GiveWeapons box, by the class each one is carried in.
#pragma once

#include <span>

namespace DevTools::Guns {

// `archetype` is the fully qualified hidName the entity library is keyed by.
struct Gun {
    const char* label;
    const char* archetype;
};

// `category` is the class as the engine names it. It comes from a gun's selCategory, not from its
// archetype's name, which says otherwise for the M1903 and the flare gun.
struct Class {
    const char* name;
    const char* category;
    std::span<const Gun> guns;
};

std::span<const Class> All();

}
