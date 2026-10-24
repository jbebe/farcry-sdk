// The Range tab: a button per test range scenario, run as one chunk of the game's own Lua.
#include "overlay/range.h"

#include "commands/scenarios.h"
#include "engine/lua.h"
#include "engine/spawn.h"

#include "imgui.h"

#include <string>

namespace {
    using DevTools::Scenarios::Arena;
    using DevTools::Scenarios::Scenario;
    using DevTools::Scenarios::Spot;

    constexpr float kGround = 16.0625f;

    // The player arrives at the marker with no ground snap, so it sits just above the ground.
    constexpr float kAboveGround = 0.44f;

    // A soldier starts a little above the ground and drops onto it.
    constexpr float kDrop = 0.5f;

    // The player's teleport target, spawned at the start: TeleportEntity copies its position and facing.
    constexpr const char* kMarker = "multiplayer.SpawnPoints.SpawnPoint";

    constexpr const char* kTravel = "GameChangeWorld(\"testrange\", \"2054037628277228842\")";

    // Removes everything the last scenario spawned, whose ids the global TestRange table holds from 1
    // to TestRange.n. The game's Lua has no ipairs, so the count is kept by hand.
    constexpr const char* kClear =
        "TestRange = TestRange or {n = 0} for i = 1, TestRange.n do RemoveEntity(TestRange[i]) end TestRange = {n = 0} ";

    const Scenario* g_running = nullptr;

    void AppendSpawn(std::string& lua, const char* archetype, const Spot& at, float z) {
        lua += "TestRange.n = TestRange.n + 1 TestRange[TestRange.n] = "
               + DevTools::Spawn::Call(archetype, at.x, at.y, z, at.yaw) + " ";
    }

    void Start(const Arena& arena, const Scenario& scenario) {
        std::string lua = kClear;
        lua += "CDynamicEnvironmentManager_GetInstance():SetScriptedTimeOfDay(12, 0) ";
        AppendSpawn(lua, kMarker, arena.start, kGround + kAboveGround);
        lua += "TeleportEntity(GetLocalPlayerId(), TestRange[1]) ";
        for (const DevTools::Scenarios::Soldier& soldier : scenario.soldiers) {
            if (soldier.post != nullptr) {
                AppendSpawn(lua, soldier.post, soldier.at, kGround);
            }
            AppendSpawn(lua, soldier.archetype, soldier.at, kGround + kDrop);
        }
        DevTools::Lua::Post(lua);
        g_running = &scenario;
    }
}

namespace DevTools::Range {

void Draw() {
    if (ImGui::Button("Travel to the range")) {
        Lua::Post(kTravel);
    }
    ImGui::SameLine();
    if (ImGui::Button("Clear")) {
        Lua::Post(kClear);
        g_running = nullptr;
    }
    ImGui::TextWrapped("Start clears the last scenario, sets noon, takes you to its arena and spawns its "
                       "soldiers. Rows are the Difficulty Overhaul's finish checklist.");

    for (const Arena& arena : Scenarios::Arenas()) {
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
