// The Armory tab: how a gun should arrive, and a button per gun, each putting it in the player's hands.
#include "overlay/armory.h"

#include "commands/guns.h"
#include "engine/inventory.h"

#include "imgui.h"

#include <algorithm>
#include <string>

namespace {
    constexpr float kWidth = 160.0f;

    // More than any gun holds, so a gun arrives full until told otherwise.
    constexpr int kFull = 999;

    void DrawRounds(const char* label, int& rounds, const char* tooltip) {
        ImGui::SetNextItemWidth(kWidth);
        ImGui::InputInt(label, &rounds);
        rounds = std::max(rounds, 0);
        if (ImGui::IsItemHovered()) {
            ImGui::SetTooltip("%s", tooltip);
        }
    }
}

namespace DevTools::Armory {

void Draw() {
    ImGui::TextWrapped("A click puts the gun in your hands. The gun of its class you carry drops at your "
                       "feet; one of the same model is kept and set instead. Rounds beyond what the gun "
                       "holds fill it.");

    static int loaded = kFull;
    static int spare = kFull;
    static float health = 1.0f;

    DrawRounds("Magazine", loaded, "The LPO-50 has no magazine: this is its fuel.");
    DrawRounds("Spare", spare, "One pool per ammo type, shared with every gun that takes it.");

    ImGui::SetNextItemWidth(kWidth);
    ImGui::InputFloat("Health", &health, 0.05f, 0.25f, "%.2f");
    health = std::clamp(health, 0.0f, 1.0f);
    if (ImGui::IsItemHovered()) {
        ImGui::SetTooltip("The gun's reliability: 1 is new, 0 is worn out.");
    }

    const std::string result = Inventory::LastResult();
    if (!result.empty()) {
        ImGui::TextDisabled("%s", result.c_str());
    }

    for (const Guns::Class& gunClass : Guns::All()) {
        ImGui::SeparatorText(gunClass.name);
        for (const Guns::Gun& gun : gunClass.guns) {
            if (ImGui::Button(gun.label, ImVec2(kWidth, 0.0f))) {
                Inventory::Give(gun.archetype, gunClass.category, loaded, spare, health);
            }
            if (ImGui::IsItemHovered()) {
                ImGui::SetTooltip("%s", gun.archetype);
            }
        }
    }
}

}
