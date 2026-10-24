// The Test range window: a button per scenario, run as one line of the game's own Lua.
#include "window.h"

#include "scenarios.h"

#include "devtools_api.h"
#include "imgui.h"

#include <cstdio>
#include <string>

namespace {
    constexpr float kGround = 16.0625f;

    // The player arrives at the marker with no ground snap, so it sits just above the ground.
    constexpr float kAboveGround = 0.44f;

    // A soldier starts a little above the ground and drops onto it.
    constexpr float kDrop = 0.5f;

    // The player's teleport target, spawned at the start: TeleportEntity copies its position and facing.
    constexpr const char* kMarker = "multiplayer.SpawnPoints.SpawnPoint";

    constexpr const char* kTravel = "#GameChangeWorld(\"testrange\", \"2054037628277228842\")";

    // Removes everything the last scenario spawned, whose ids the global TestRange table holds.
    constexpr const char* kClear =
        "#TestRange = TestRange or {} for _, id in ipairs(TestRange) do RemoveEntity(id) end TestRange = {} ";

    const TestRange::Scenario* g_running = nullptr;

    void AppendSpawn(std::string& lua, const char* archetype, const TestRange::Spot& at, float z) {
        char spawn[256];
        std::snprintf(spawn, sizeof(spawn),
                      "table.insert(TestRange, SpawnEntityFromArchetype(\"%s\", %.2f, %.2f, %.2f, 0, 0, %.1f)) ",
                      archetype, at.x, at.y, z, at.yaw);
        lua += spawn;
    }

    void Start(const TestRange::Arena& arena, const TestRange::Scenario& scenario) {
        std::string lua = kClear;
        lua += "CDynamicEnvironmentManager_GetInstance():SetScriptedTimeOfDay(12, 0) ";
        AppendSpawn(lua, kMarker, arena.start, kGround + kAboveGround);
        lua += "TeleportEntity(GetLocalPlayerId(), TestRange[1]) ";
        for (const TestRange::Soldier& soldier : scenario.soldiers) {
            if (soldier.post != nullptr) {
                AppendSpawn(lua, soldier.post, soldier.at, kGround);
            }
            AppendSpawn(lua, soldier.archetype, soldier.at, kGround + kDrop);
        }
        DevTools::Overlay::PostLine(lua.c_str());
        g_running = &scenario;
    }
}

namespace TestRange::Window {

void Draw(void*) {
    if (ImGui::Button("Travel to the range")) {
        DevTools::Overlay::PostLine(kTravel);
    }
    ImGui::SameLine();
    if (ImGui::Button("Clear")) {
        DevTools::Overlay::PostLine(kClear);
        g_running = nullptr;
    }
    ImGui::TextWrapped("Start clears the last scenario, sets noon, takes you to its arena and spawns its "
                       "soldiers. Rows are the Difficulty Overhaul's finish checklist.");

    for (const Arena& arena : Arenas()) {
        ImGui::SeparatorText(arena.name);
        ImGui::PushID(arena.name);
        for (const Scenario& scenario : arena.scenarios) {
            if (ImGui::Button(scenario.name)) {
                Start(arena, scenario);
            }
            if (ImGui::IsItemHovered()) {
                ImGui::SetTooltip("%s\n\n%s", scenario.steps, scenario.expect);
            }
            ImGui::SameLine();
            ImGui::TextDisabled("rows %s", scenario.rows);
        }
        ImGui::PopID();
    }
    ImGui::SeparatorText("Tall grass");
    ImGui::TextDisabled("Row 10 needs tall grass, and the range has none yet.");

    if (g_running != nullptr) {
        ImGui::SeparatorText(g_running->name);
        ImGui::TextWrapped("Do: %s", g_running->steps);
        ImGui::TextWrapped("Look for: %s", g_running->expect);
    }
}

}
