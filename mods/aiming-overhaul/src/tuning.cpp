// The sway down the sights, dirt in the eye and each weapon's shot shake, and the window and file
// they are kept in. See tuning.h.
#include "tuning.h"

#include "dirt.h"
#include "engine/aim.h"
#include "fcse_api.h"
#include "shot_shake.h"

#include "imgui.h"

#include <windows.h>

#include <algorithm>
#include <array>
#include <atomic>
#include <charconv>
#include <cstdint>
#include <filesystem>
#include <fstream>
#include <iterator>
#include <string>
#include <string_view>

namespace {
    using AimingOverhaul::Tuning::DirtLook;
    using AimingOverhaul::Tuning::SightSway;
    using AimingOverhaul::Tuning::Values;

    template <class T>
    struct Parameter {
        // The key in the file.
        const char* key;
        const char* label;
        float T::*member;
        float min;
        float max;
        // The printf format, which carries its unit.
        const char* format;
    };

    // The sights' section in the file, and its values.
    constexpr const char* kSights = "Down the sights";
    constexpr Parameter<SightSway> kSightParameters[] = {
        {"SwayStanding", "Sway standing", &SightSway::standing, 0.0f, 1.5f, "%.2fx"},
        {"SwayCrouched", "Sway crouched", &SightSway::crouched, 0.0f, 1.5f, "%.2fx"},
    };
    SightSway g_sights;

    // Dirt in the eye's section in the file, and its values.
    constexpr const char* kDirt = "Dirt in the eye";
    constexpr Parameter<DirtLook> kDirtParameters[] = {
        {"Size", "Dirt size", &DirtLook::size, 0.05f, 0.8f, "%.2f of the screen"},
        {"Darkness", "Dirt darkness", &DirtLook::darkness, 0.0f, 1.0f, "%.2f"},
        {"Blur", "Dirt blur", &DirtLook::blur, 0.0f, 30.0f, "%.1f px at 1080p"},
        {"Clear", "Dirt clears in", &DirtLook::clear, 0.5f, 10.0f, "%.1f s"},
        {"Range", "Dirt thrown from", &DirtLook::range, 0.2f, 5.0f, "%.1f m"},
    };
    DirtLook g_dirt;

    constexpr Parameter<Values> kParameters[] = {
        {"KickPerImpulse", "Single shot kick", &Values::kickPerImpulse, 0.0f, 0.6f,
         "%.3f deg per N s"},
        {"KickTime", "Single shot time", &Values::kickTime, 0.05f, 1.0f, "%.2f s"},
        {"KickDelay", "Single shot delay", &Values::kickDelay, 0.0f, 0.3f, "%.3f s"},
        {"RollPerImpulse", "Full auto roll", &Values::rollPerImpulse, 0.0f, 0.6f,
         "%.3f deg per N s"},
        {"RollCycle", "Full auto roll cycle", &Values::rollCycle, 0.04f, 2.0f, "%.2f s"},
        {"RollFade", "Kick to roll fade", &Values::rollFade, 0.0f, 2.0f, "%.2f s"},
    };

    // The rifles', submachine guns' and machine guns' values until the file gives them their own,
    // and the shotguns'; every other weapon starts from Values' own.
    constexpr Values kLongGun = {0.07f, 0.1f, 0.102f, 0.133f, 0.08f, 0.11f};
    constexpr Values kShotgun = {0.201f, 0.15f, 0.1f, 0.103f, 0.08f, 0.11f};

    // A weapon the window lists: its section in the file, its archetype under weapons or
    // DLC1Weapons, which its variants are named under too, and its values until the file gives it
    // its own.
    struct Weapon {
        const char* name;
        const char* archetype;
        Values defaults;
    };

    constexpr Weapon kWeapons[] = {
        {"Makarov", "Secondary.Makarov"},
        {"6P9", "Secondary.SilencedMakarov_6P9"},
        {"Star .45", "Secondary.Star45"},
        {"Eagle .50", "Secondary.DesertEagle"},
        {"MAC-10", "Secondary.MAC10", kLongGun},
        {"Uzi", "Secondary.Uzi", kLongGun},
        {"MP5", "Primary.MP5", kLongGun},
        {"AK-47", "Primary.AK47", kLongGun},
        {"FAL", "Primary.FNFAL", kLongGun},
        {"G3", "Primary.G3KA4", kLongGun},
        {"AR-16", "Primary.M16", kLongGun},
        {"Homeland 37", "Primary.Ithaca", kShotgun},
        {"SPAS-12", "Primary.SPAS12", kShotgun},
        {"USAS-12", "Primary.USAS12", kShotgun},
        {"Sawed-off shotgun", "DLC1.SawedOffShotgun", kShotgun},
        {"Silenced shotgun", "DLC1.SilencedShotgun", kShotgun},
        {"Dart Rifle", "Special.Dart_Rifle", kLongGun},
        {"M1903", "Special.M1903", kLongGun},
        {"Dragunov", "Primary.Dragunov", kLongGun},
        {"AS50", "Primary.AS50", kLongGun},
        {"M249", "Special.M249_Saw", kLongGun},
        {"PKM", "Special.PKM", kLongGun},
        {"M79", "Secondary.M79"},
        {"MGL-140", "Primary.MGL140"},
        {"RPG-7", "Special.RPG7"},
        {"Carl Gustaf", "Special.Carl_Gustaf"},
        {"Crossbow", "DLC1.Crossbow"},
    };

    // A set for each weapon in kWeapons, in its order, then one for the weapons it leaves out.
    constexpr size_t kOther = std::size(kWeapons);
    std::array<Values, kOther + 1> g_weapons;
    // The weapon in hand and its set; the set the sliders edit, and the set in hand they last
    // followed.
    uint8_t* g_weapon = nullptr;
    std::atomic<size_t> g_inHand{kOther};
    size_t g_edited = kOther;
    size_t g_followed = kOther;
    // Whether a slider has moved since the file was last written.
    bool g_unsaved = false;

    const char* NameOf(size_t weapon) {
        return weapon < kOther ? kWeapons[weapon].name : "Other weapons";
    }

    size_t IndexOf(std::string_view entity) {
        const size_t root = entity.find('.');
        const std::string_view archetype =
            root == std::string_view::npos ? std::string_view{} : entity.substr(root + 1);
        for (size_t weapon = 0; weapon < kOther; ++weapon) {
            const std::string_view listed = kWeapons[weapon].archetype;
            if (archetype.starts_with(listed) &&
                (archetype.size() == listed.size() || archetype[listed.size()] == '.')) {
                return weapon;
            }
        }
        return kOther;
    }

    // Beside fcse.ini, in the folder the game was started from.
    const std::filesystem::path& File() {
        static const std::filesystem::path file = [] {
            std::wstring exe(32768, L'\0');
            exe.resize(GetModuleFileNameW(nullptr, exe.data(), static_cast<DWORD>(exe.size())));
            return std::filesystem::path(exe).parent_path() / L"aiming-overhaul.ini";
        }();
        return file;
    }

    std::string_view Trim(std::string_view text) {
        const size_t first = text.find_first_not_of(" \t\r");
        return first == std::string_view::npos
                   ? std::string_view{}
                   : text.substr(first, text.find_last_not_of(" \t\r") - first + 1);
    }

    template <class T, size_t N>
    void WriteSection(std::ofstream& file, const char* name, const Parameter<T> (&parameters)[N],
                      const T& values) {
        file << "\n[" << name << "]\n";
        for (const Parameter<T>& parameter : parameters) {
            char value[32];
            const char* end =
                std::to_chars(value, value + sizeof(value), values.*parameter.member).ptr;
            file << parameter.key << " = " << std::string_view(value, end - value) << "\n";
        }
    }

    // Sets the value `key` names to `number`, where it is one of these and a number.
    template <class T, size_t N>
    void Read(std::string_view key, std::string_view number, const Parameter<T> (&parameters)[N],
              T& values) {
        const auto parameter = std::find_if(std::begin(parameters), std::end(parameters),
                                            [&](const Parameter<T>& p) { return key == p.key; });
        float read = 0.0f;
        if (parameter != std::end(parameters) &&
            std::from_chars(number.data(), number.data() + number.size(), read).ec == std::errc{}) {
            values.*parameter->member = std::clamp(read, parameter->min, parameter->max);
        }
    }

    // True when a slider moved.
    template <class T, size_t N>
    bool DrawSliders(const Parameter<T> (&parameters)[N], T& values) {
        bool moved = false;
        for (const Parameter<T>& parameter : parameters) {
            moved |= ImGui::SliderFloat(parameter.label, &(values.*parameter.member), parameter.min,
                                        parameter.max, parameter.format,
                                        ImGuiSliderFlags_AlwaysClamp);
        }
        return moved;
    }

    void Save() {
        std::ofstream file(File(), std::ios::trunc);
        file << "; Aiming Overhaul, the sights' and the dirt's sections, then one per weapon. Set in "
                "its window in\n; DevTools' overlay and read at launch.\n";
        WriteSection(file, kSights, kSightParameters, g_sights);
        WriteSection(file, kDirt, kDirtParameters, g_dirt);
        for (size_t weapon = 0; weapon < g_weapons.size(); ++weapon) {
            WriteSection(file, NameOf(weapon), kParameters, g_weapons[weapon]);
        }
        file.close();
        if (!file) {
            FCSE::ApiPointer()->Log("tuning: aiming-overhaul.ini could not be written");
        }
    }
}

const SightSway& AimingOverhaul::Tuning::Sights() {
    return g_sights;
}

const DirtLook& AimingOverhaul::Tuning::DirtInTheEye() {
    return g_dirt;
}

void AimingOverhaul::Tuning::Follow(uint8_t* weapon) {
    if (weapon != g_weapon) {
        g_weapon = weapon;
        g_inHand = IndexOf(Aim::WeaponName());
    }
}

const Values& AimingOverhaul::Tuning::InHand() {
    return g_weapons[g_inHand];
}

void AimingOverhaul::Tuning::Load() {
    for (size_t weapon = 0; weapon < g_weapons.size(); ++weapon) {
        g_weapons[weapon] = weapon < kOther ? kWeapons[weapon].defaults : Values{};
    }
    // A line outside a known section changes nothing.
    std::ifstream file(File());
    bool sights = false;
    bool dirt = false;
    Values* weapon = nullptr;
    std::string line;
    while (std::getline(file, line)) {
        const std::string_view text = Trim(line);
        if (text.starts_with('[') && text.ends_with(']')) {
            const std::string_view name = text.substr(1, text.size() - 2);
            sights = name == kSights;
            dirt = name == kDirt;
            weapon = nullptr;
            for (size_t i = 0; i < g_weapons.size() && weapon == nullptr; ++i) {
                if (name == NameOf(i)) {
                    weapon = &g_weapons[i];
                }
            }
            continue;
        }
        const size_t equals = text.find('=');
        if (equals == std::string_view::npos) {
            continue;
        }
        const std::string_view key = Trim(text.substr(0, equals));
        const std::string_view number = Trim(text.substr(equals + 1));
        if (sights) {
            Read(key, number, kSightParameters, g_sights);
        } else if (dirt) {
            Read(key, number, kDirtParameters, g_dirt);
        } else if (weapon != nullptr) {
            Read(key, number, kParameters, *weapon);
        }
    }
    Save();
}

void AimingOverhaul::Tuning::DrawWindow(void*) {
    ImGui::SeparatorText("Down the sights, every weapon");
    ImGui::SetItemTooltip("How much of the sway is left down the sights, standing and crouched; the "
                          "stance's own steadiness comes on top.");
    g_unsaved |= DrawSliders(kSightParameters, g_sights);

    ImGui::SeparatorText("Dirt in the eye, every weapon");
    g_unsaved |= DrawSliders(kDirtParameters, g_dirt);
    if (ImGui::Button("Throw dirt")) {
        Dirt::Throw();
    }
    ImGui::SetItemTooltip("Throws dirt in the eye as a round landing a metre to the right would.");

    ImGui::SeparatorText("Shot shake");
    if (g_inHand != g_followed) {
        g_followed = g_inHand;
        g_edited = g_followed;
    }
    if (ImGui::BeginCombo("Weapon", NameOf(g_edited))) {
        for (size_t weapon = 0; weapon < g_weapons.size(); ++weapon) {
            if (ImGui::Selectable(NameOf(weapon), weapon == g_edited)) {
                g_edited = weapon;
            }
        }
        ImGui::EndCombo();
    }
    ImGui::SetItemTooltip("The weapon the values below are for: the one in hand when it changes, or "
                          "any picked here.");
    g_unsaved |= DrawSliders(kParameters, g_weapons[g_edited]);
    ShotShake::DrawLastShot();
    if (g_unsaved && !ImGui::IsAnyItemActive()) {
        Save();
        g_unsaved = false;
    }
}
