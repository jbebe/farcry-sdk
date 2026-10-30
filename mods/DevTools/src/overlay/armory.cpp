// The Armory tab: a button per gun putting it in the player's hands, and controls for the gun in hand.
#include "overlay/armory.h"

#include "commands/guns.h"
#include "engine/inventory.h"

#include "imgui.h"

#include <algorithm>
#include <string>

namespace DevTools::Armory {

namespace {
    constexpr float kWidth = 160.0f;
    constexpr float kSliderWidth = 320.0f;

    void DrawHeld(const Inventory::Held& held) {
        ImGui::SeparatorText(held.name.c_str());

        int rounds = held.magazine + held.spare;
        ImGui::SetNextItemWidth(kSliderWidth);
        if (ImGui::SliderInt("Ammo", &rounds, 0, held.capacity, "%d", ImGuiSliderFlags_AlwaysClamp)) {
            Inventory::SetRounds(rounds);
        }
        if (ImGui::IsItemHovered()) {
            ImGui::SetTooltip("Fills the magazine, then spare, which every gun of this ammo type shares.\n"
                              "Ctrl+click to type a number.");
        }

        // Follows the gun in hand except while being edited, and applies when the edit ends, so that
        // typing 50 does not pass through 5.
        static float health = 100.0f;
        ImGui::SetNextItemWidth(kWidth);
        ImGui::InputFloat("Health %", &health, 1.0f, 10.0f, "%.1f");
        if (ImGui::IsItemDeactivatedAfterEdit()) {
            health = std::clamp(health, 0.0f, 100.0f);
            Inventory::SetHealth(health);
        } else if (!ImGui::IsItemActive()) {
            health = held.health;
        }
        if (ImGui::IsItemHovered()) {
            ImGui::SetTooltip("Of the gun's maximum reliability: 100 is new, 0 is worn out.");
        }

        ImGui::TextDisabled("%d in the magazine, %d spare, reliability %.0f of %.0f", held.magazine, held.spare,
                            held.reliability, held.maxReliability);
    }
}

void Draw() {
    ImGui::TextWrapped("A click puts the gun in your hands, full and new, and the gun of its class you carry "
                       "drops at your feet. Ammo and health below set whichever gun is in your hands.");

    const std::string result = Inventory::LastResult();
    if (!result.empty()) {
        ImGui::TextDisabled("%s", result.c_str());
    }

    const Inventory::Held held = Inventory::InHand();
    if (held.capacity > 0) {
        DrawHeld(held);
    } else {
        ImGui::TextDisabled("No gun in your hands.");
    }

    for (const Guns::Class& gunClass : Guns::All()) {
        ImGui::SeparatorText(gunClass.name);
        for (const Guns::Gun& gun : gunClass.guns) {
            if (ImGui::Button(gun.label, ImVec2(kWidth, 0.0f))) {
                Inventory::Give(gun.archetype, gunClass.category);
            }
            if (ImGui::IsItemHovered()) {
                ImGui::SetTooltip("%s", gun.archetype);
            }
        }
    }
}

}
