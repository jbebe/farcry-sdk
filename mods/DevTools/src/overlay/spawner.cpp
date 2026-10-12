// The Spawner tab: a button per archetype, each putting one in front of the player.
#include "overlay/spawner.h"

#include "commands/spawnables.h"
#include "engine/spawn.h"

#include "imgui.h"

#include <algorithm>

namespace {
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
    ImGui::TextDisabled("Spawns a few metres ahead of you, facing the way you face. The line it ran "
                        "is in the Lua tab.");

    static const float width = ButtonWidth();
    for (const Spawnables::Group& group : Spawnables::All()) {
        ImGui::SeparatorText(group.name);
        for (const Spawnables::Spawnable& spawnable : group.spawnables) {
            if (ImGui::Button(spawnable.label, ImVec2(width, 0.0f))) {
                Spawn::Ahead(spawnable.archetype);
            }
            if (ImGui::IsItemHovered()) {
                ImGui::SetTooltip("%s", spawnable.archetype);
            }
        }
    }
}

}
