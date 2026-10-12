// The Spawner tab: a button per archetype, each putting one in front of the player.
#include "overlay/spawner.h"

#include "commands/spawnables.h"
#include "engine/spawn.h"

#include "imgui.h"

namespace {
    constexpr int kColumns = 4;
}

namespace DevTools::Spawner {

void Draw() {
    ImGui::TextDisabled("Spawns a few metres ahead of you, facing the way you face. The line it ran "
                        "is in the Lua tab.");

    for (const Spawnables::Group& group : Spawnables::All()) {
        ImGui::SeparatorText(group.name);
        if (!ImGui::BeginTable(group.name, kColumns, ImGuiTableFlags_SizingStretchSame)) {
            continue;
        }
        for (const Spawnables::Spawnable& spawnable : group.spawnables) {
            ImGui::TableNextColumn();
            if (ImGui::Button(spawnable.label, ImVec2(-FLT_MIN, 0.0f))) {
                Spawn::Ahead(spawnable.archetype);
            }
            if (ImGui::IsItemHovered()) {
                ImGui::SetTooltip("%s", spawnable.archetype);
            }
        }
        ImGui::EndTable();
    }
}

}
