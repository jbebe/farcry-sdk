#include "commands/guns.h"

namespace {
    using DevTools::Guns::Class;
    using DevTools::Guns::Gun;

    constexpr Gun kPrimary[] = {
        {"AK-47", "weapons.Primary.AK47"},
        {"FN FAL", "weapons.Primary.FNFAL"},
        {"G3-KA4", "weapons.Primary.G3KA4"},
        {"M16", "weapons.Primary.M16"},
        {"MP5", "weapons.Primary.MP5"},
        {"AS50", "weapons.Primary.AS50"},
        {"Dragunov", "weapons.Primary.Dragunov"},
        {"M1903", "weapons.Special.M1903"},
        {"SPAS-12", "weapons.Primary.SPAS12"},
        {"USAS-12", "weapons.Primary.USAS12"},
        {"Ithaca", "weapons.Primary.Ithaca"},
        {"MGL-140", "weapons.Primary.MGL140"},
    };

    constexpr Gun kSecondary[] = {
        {"Makarov", "weapons.Secondary.Makarov"},
        {"Silenced Makarov", "weapons.Secondary.SilencedMakarov_6P9"},
        {"Star .45", "weapons.Secondary.Star45"},
        {"Desert Eagle", "weapons.Secondary.DesertEagle"},
        {"Uzi", "weapons.Secondary.Uzi"},
        {"MAC-10", "weapons.Secondary.MAC10"},
        {"M79", "weapons.Secondary.M79"},
        {"IED", "weapons.Secondary.IED"},
        {"Flare gun", "weapons.Special.Flare_Gun"},
    };

    constexpr Gun kSpecial[] = {
        {"PKM", "weapons.Special.PKM"},
        {"M249 SAW", "weapons.Special.M249_Saw"},
        {"Mortar", "weapons.Special.Mortar"},
        {"Dart rifle", "weapons.Special.Dart_Rifle"},
        {"Carl Gustaf", "weapons.Special.Carl_Gustaf"},
        {"RPG-7", "weapons.Special.RPG7"},
        {"LPO-50", "weapons.Special.LPO50"},
    };

    constexpr Class kClasses[] = {
        {"Primary", "primary", kPrimary},
        {"Secondary", "secondary", kSecondary},
        {"Special", "special", kSpecial},
    };
}

namespace DevTools::Guns {

std::span<const Class> All() { return kClasses; }

}
