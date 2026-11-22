#include "overlay/sky_weather.h"

#include "sky_overhaul_weather.h"

#include "imgui.h"

#include <cstdio>
#include <windows.h>

namespace {
    // Sky Overhaul's weather API, looked up once; null without the plugin or at another version.
    const SkyOverhaul_WeatherAPI* Api() {
        static const SkyOverhaul_WeatherAPI* api = [] {
            HMODULE sky = GetModuleHandleW(L"SkyOverhaul.dll");
            auto get = sky == nullptr ? nullptr
                                      : reinterpret_cast<SkyOverhaul_GetWeatherAPIFn>(
                                            GetProcAddress(sky, "SkyOverhaul_GetWeatherAPI"));
            const SkyOverhaul_WeatherAPI* found = get == nullptr ? nullptr : get();
            return found != nullptr && found->apiVersion == SKYOVERHAUL_WEATHER_API_VERSION ? found
                                                                                            : nullptr;
        }();
        return api;
    }

    // "C5 Lots of cumulus and cirrus", or "-" for no such row.
    void RowLabel(const SkyOverhaul_WeatherAPI* api, int row, char* out, size_t size) {
        const char* id = api->RowId(row);
        const char* name = api->RowName(row);
        if (id == nullptr || name == nullptr) {
            snprintf(out, size, "-");
            return;
        }
        snprintf(out, size, "%s %s", id, name);
    }

    // Keeps the next button of a line on it while it fits before `right`.
    void Flow(const char* label, float right) {
        const ImGuiStyle& style = ImGui::GetStyle();
        const float width = ImGui::CalcTextSize(label).x + style.FramePadding.x * 2.0f;
        if (ImGui::GetItemRectMax().x + style.ItemSpacing.x + width <= right) {
            ImGui::SameLine();
        }
    }

    // Holds this row, or lets the timeline run at -1; lit while it is the one in force.
    void HoldButton(const SkyOverhaul_WeatherAPI* api, int row, const char* label, int forced) {
        const bool held = row < 0 ? forced < 0 : row == forced;
        if (held) {
            ImGui::PushStyleColor(ImGuiCol_Button, ImGui::GetStyleColorVec4(ImGuiCol_ButtonActive));
        }
        if (ImGui::Button(label)) {
            api->Force(row);
        }
        if (held) {
            ImGui::PopStyleColor();
        }
    }
}

std::string DevTools::SkyWeather::Hold(const std::string& id) {
    const SkyOverhaul_WeatherAPI* api = Api();
    if (api == nullptr) {
        return "Sky Overhaul's weather is not loaded";
    }
    if (id == "timeline") {
        api->Force(-1);
        return "";
    }
    for (int row = 0; row < api->RowCount(); ++row) {
        if (id == api->RowId(row)) {
            api->Force(row);
            return "";
        }
    }
    return "no weather " + id;
}

std::string DevTools::SkyWeather::Plant(const std::string& where) {
    const SkyOverhaul_WeatherAPI* api = Api();
    if (api == nullptr) {
        return "Sky Overhaul's weather is not loaded";
    }
    float degrees = 0.0f;
    float distance = 0.0f;
    if (where == "off") {
        distance = -1.0f;
    } else if (where == "over") {
        distance = 0.0f;
    } else if (sscanf_s(where.c_str(), "upwind %f", &distance) == 1 ||
               sscanf_s(where.c_str(), "ahead %f", &distance) == 1) {
        SkyOverhaul_WeatherState now;
        api->Read(&now);
        degrees = where[0] == 'u' ? now.windDegrees + 180.0f : now.facingDegrees;
    } else if (sscanf_s(where.c_str(), "%f %f", &degrees, &distance) != 2) {
        return "skycell over | ahead <metres> | upwind <metres> | <degrees> <metres> | off";
    }
    api->Plant(degrees, distance);
    return "";
}

void DevTools::SkyWeather::Draw() {
    const SkyOverhaul_WeatherAPI* api = Api();
    if (api == nullptr) {
        ImGui::TextDisabled("Needs Sky Overhaul.");
        return;
    }

    SkyOverhaul_WeatherState now;
    api->Read(&now);

    // A line of buttons per family of weathers, C1 to C7 and on, wrapped to the tab's width.
    const float right = ImGui::GetCursorScreenPos().x + ImGui::GetContentRegionAvail().x;
    HoldButton(api, -1, "Timeline", now.forced);
    char family = 0;
    for (int row = 0; row < api->RowCount(); ++row) {
        char label[96];
        RowLabel(api, row, label, sizeof(label));
        if (label[0] == family) {
            Flow(label, right);
        }
        family = label[0];
        HoldButton(api, row, label, now.forced);
    }

    ImGui::Spacing();
    if (ImGui::Button("Storm overhead")) {
        Plant("over");
    }
    ImGui::SameLine();
    if (ImGui::Button("Storm 6 km upwind")) {
        Plant("upwind 6000");
    }
    ImGui::SameLine();
    if (ImGui::Button("No storm")) {
        Plant("off");
    }

    ImGui::Spacing();
    if (!now.live) {
        ImGui::TextDisabled("Nothing until a world runs with Sky Overhaul's Weather on.");
        return;
    }

    char row[96];
    RowLabel(api, now.row, row, sizeof(row));
    if (now.next != now.row) {
        char next[96];
        RowLabel(api, now.next, next, sizeof(next));
        ImGui::Text("%s, turning into %s (%.0f%%)", row, next, now.blend * 100.0f);
    } else {
        ImGui::Text("%s", row);
    }
    if (now.forced < 0) {
        const int minutes = static_cast<int>(now.secondsLeft / 60.0f);
        ImGui::TextDisabled("Next weather in %d h %02d min of game time", minutes / 60,
                            minutes % 60);
    }
    ImGui::TextDisabled("Storm %.2f, cumulus %.2f, cirrus %.2f, rain %.2f", now.storminess,
                        now.coverage, now.cirrus, now.rain);
    ImGui::TextDisabled("Wind %.0f toward %.0f deg", now.windForce, now.windDegrees);
    if (now.fogWeight > 0.0f) {
        ImGui::TextDisabled("Fog to %.0f m at %.0f%%", now.fogEnd, now.fogWeight * 100.0f);
    } else {
        ImGui::TextDisabled("The game's fog");
    }
    ImGui::TextDisabled("Mist %.2f, dust %.2f, wet ground %.2f", now.mist, now.dust, now.wet);
    if (now.cellDistance >= 0.0f) {
        ImGui::TextDisabled("Storm cells %.0f%% of it, nearest %.1f km off", now.cell * 100.0f,
                            now.cellDistance / 1000.0f);
    } else {
        ImGui::TextDisabled("No storm cell near");
    }
}
