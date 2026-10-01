// The Lua tab: a line to run or evaluate, and the lines Lua has reported.
#include "overlay/lua_console.h"

#include "engine/lua.h"

#include "imgui.h"
#include "misc/cpp/imgui_stdlib.h"

#include <string>

namespace {
    std::string g_input;
}

namespace DevTools::LuaConsole {

void Draw() {
    const float buttons = ImGui::CalcTextSize("Evaluate").x + ImGui::CalcTextSize("Run").x +
                          ImGui::CalcTextSize("Clear").x + ImGui::GetStyle().FramePadding.x * 6.0f +
                          ImGui::GetStyle().ItemSpacing.x * 3.0f;
    ImGui::SetNextItemWidth(-buttons);
    const bool entered = ImGui::InputText("##lua", &g_input, ImGuiInputTextFlags_EnterReturnsTrue);

    ImGui::SameLine();
    if ((ImGui::Button("Run") || entered) && !g_input.empty()) {
        Lua::Post(g_input);
    }
    ImGui::SameLine();
    if (ImGui::Button("Evaluate") && !g_input.empty()) {
        Lua::PostEvaluate(g_input);
    }
    ImGui::SameLine();
    if (ImGui::Button("Clear")) {
        Lua::Clear();
    }

    ImGui::TextDisabled("Script errors and System:Log lines land here and in fcse.log.");
    ImGui::BeginChild("lines", ImVec2(0.0f, 0.0f), ImGuiChildFlags_Borders);
    for (const std::string& line : Lua::Lines()) {
        ImGui::TextUnformatted(line.c_str());
    }
    if (ImGui::GetScrollY() >= ImGui::GetScrollMaxY()) {
        ImGui::SetScrollHereY(1.0f);
    }
    ImGui::EndChild();
}

}
