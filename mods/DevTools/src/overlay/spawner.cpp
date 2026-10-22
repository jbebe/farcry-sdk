// The Spawner tab: a button per archetype, each putting one, or a squad, in front of the player.
#include "overlay/spawner.h"

#include "commands/spawnables.h"
#include "engine/spawn.h"

#include "imgui.h"

#include <algorithm>

namespace {
    constexpr int kLargestSquad = 6;

    // Wide enough for the longest label, so the buttons line up as one column.
    float ButtonWidth() {
        float widest = 0.0f;
        for (const DevTools::Spawnables::Group& group : DevTools::Spawnables::All()) {
            for (const DevTools::Spawnables::Spawnable& spawnable : group.spawnables) {
                widest = std::max(widest, ImGui::CalcTextSize(spawnable.label).x);
            }
        }
        return widest + ImGui::GetStyle().FramePadding.x * 2.0f;
    }
}

namespace DevTools::Spawner {

void Draw() {
    ImGui::TextDisabled("Vehicles spawn a few metres ahead of you, facing the way you face; soldiers "
                        "25 m ahead, facing you. The line it ran is in the Lua tab.");

    static const float width = ButtonWidth();
    static int squad = 1;
    ImGui::SetNextItemWidth(width);
    ImGui::SliderInt("Squad size", &squad, 1, kLargestSquad);

    for (const Spawnables::Group& group : Spawnables::All()) {
        ImGui::SeparatorText(group.name);
        // The factions share labels, so each group gets its own ID scope.
        ImGui::PushID(group.name);
        for (const Spawnables::Spawnable& spawnable : group.spawnables) {
            if (ImGui::Button(spawnable.label, ImVec2(width, 0.0f))) {
                if (group.squad) {
                    Spawn::Squad(spawnable.archetype, squad);
                } else {
                    Spawn::Ahead(spawnable.archetype);
                }
            }
            if (ImGui::IsItemHovered()) {
                ImGui::SetTooltip("%s", spawnable.archetype);
            }
        }
        ImGui::PopID();
    }
}

}
