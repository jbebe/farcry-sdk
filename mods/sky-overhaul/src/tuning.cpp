#include "tuning.h"

#include "grade.h"

#include "engine/cloud_layer.h"
#include "engine/time_of_day.h"
#include "fcse_api.h"

#include "imgui.h"

#include <algorithm>
#include <charconv>
#include <cstddef>
#include <filesystem>
#include <fstream>
#include <iterator>
#include <string>
#include <string_view>

namespace {
    using SkyOverhaul::Tuning::Values;

    // What a value tunes, which is the window's tab it is drawn in.
    struct Category {
        const char* name;
    };

    // A heading in the window.
    struct Group {
        const char* name;
        const Category* category;
    };

    struct Parameter {
        // The key in the file and the label in the window.
        const char* key;
        const Group* group;
        // Its member's offsetof in Values.
        size_t offset;
        float defaultValue;
        float min;
        float max;
        // The printf format, which carries its unit.
        const char* format;
        // Shown when the row is hovered.
        const char* help;
    };

    // A named hour in the sun's day, which has sunrise at 06:00 and sunset at 18:00.
    struct Moment {
        const char* name;
        int hour;
    };

    constexpr Category kSun = {"Sun"};
    // Holds no rows, only the moments.
    constexpr Category kSky = {"Sky"};
    constexpr Category kClouds = {"Clouds"};
    constexpr Category kNight = {"Night"};
    constexpr Category kShadows = {"Shadows"};
    constexpr Category kGrade = {"Grade"};

    // The window's tabs, in order.
    constexpr const Category* kTabs[] = {&kSun, &kSky, &kClouds, &kNight, &kShadows, &kGrade};

    constexpr Group kCloudLayer = {"Cloud layer", &kClouds};
    constexpr Group kHighCloud = {"High cloud", &kClouds};
    constexpr Group kSunGlare = {"Sun glare", &kSun};
    constexpr Group kAfterimage = {"Afterimage", &kSun};
    constexpr Group kNightVision = {"Night vision", &kNight};
    constexpr Group kCloudShadows = {"Cloud shadows", &kShadows};
    constexpr Group kColourGrade = {"Colour grade", &kGrade};

    // In the order the file and the window list them.
    constexpr Parameter kParameters[] = {
        {"Cloud coverage", &kCloudLayer, offsetof(Values, cloudCoverage), 0.44f, 0.0f, 1.0f, "%.2f",
         "How much of the sky the layer fills."},
        {"Cloud density", &kCloudLayer, offsetof(Values, cloudDensity), 0.021f, 0.005f, 0.3f, "%.3f",
         "How solid the cloud is where it is filled."},
        {"Cloud detail", &kCloudLayer, offsetof(Values, cloudDetail), 0.7f, 0.0f, 1.0f, "%.2f",
         "How hard the cloud's edges are torn."},
        {"Cloud haze", &kCloudLayer, offsetof(Values, cloudHaze), 17910.0f, 300.0f, 20000.0f,
         "%.0f m", "Over how far a cloud turns into the horizon behind it."},
        {"Cloud base", &kCloudLayer, offsetof(Values, cloudBase), 1013.0f, 100.0f, 4000.0f, "%.0f m",
         "Where the layer's floor sits."},
        {"Cloud thickness", &kCloudLayer, offsetof(Values, cloudThickness), 2031.0f, 100.0f, 3000.0f,
         "%.0f m", "How deep the layer is."},
        {"Cloud size", &kCloudLayer, offsetof(Values, cloudSize), 7545.0f, 500.0f, 12000.0f,
         "%.0f m", "How wide one repeat of the shape is, which is how large a cloud reads."},
        {"Cloud wind", &kCloudLayer, offsetof(Values, cloudWind), 1.0f, 0.0f, 5.0f, "%.2f",
         "How fast the layer drifts, as a multiple of the engine's wind."},
        {"Cirrus", &kHighCloud, offsetof(Values, cirrus), 0.41f, 0.0f, 1.0f, "%.2f",
         "How much of the sky the high sheet of ice cloud reaches across."},
        {"Cirrus opacity", &kHighCloud, offsetof(Values, cirrusOpacity), 0.04f, 0.0f, 1.0f, "%.2f",
         "How solid that sheet is where it reaches."},
        {"Contrails", &kHighCloud, offsetof(Values, contrails), 0.04f, 0.0f, 1.0f, "%.2f",
         "How strongly the two aircraft trails show."},

        {"Sun glare strength", &kSunGlare, offsetof(Values, glareStrength), 1.0f, 0.0f, 2.0f, "%.2f",
         "Peak brightness with the sun dead centre. Zero turns the glare off."},
        {"Sun glare spread", &kSunGlare, offsetof(Values, glareSpread), 30.0f, 10.0f, 180.0f,
         "%.0f deg", "How far off centre the sun gets before the glare reaches nothing."},
        {"Sun glare falloff", &kSunGlare, offsetof(Values, glareFalloff), 1.3f, 1.0f, 8.0f, "%.1f",
         "How sharply the glare falls away from dead centre."},
        {"Sun glare veil", &kSunGlare, offsetof(Values, glareVeil), 1.0f, 0.0f, 1.0f, "%.2f",
         "How much of the glare covers the frame wherever the sun sits in it."},
        {"Sun glare contrast", &kSunGlare, offsetof(Values, glareContrast), 1.95f, 0.0f, 4.0f,
         "%.2f", "How hard the contrast is pushed at full dazzle."},
        {"Sun glare desaturation", &kSunGlare, offsetof(Values, glareDesaturation), 1.29f, 0.0f,
         3.0f, "%.2f",
         "How fast colour drains as the dazzle rises. One reaches grey only at full dazzle."},
        {"Sun elevation ramp", &kSunGlare, offsetof(Values, elevationRamp), 40.0f, 1.0f, 45.0f,
         "%.0f deg", "The sun's elevation at which the glare reaches full strength."},

        {"Afterimage strength", &kAfterimage, offsetof(Values, afterimageStrength), 1.0f, 0.0f, 1.0f,
         "%.2f", "How strongly the burned-in view shows once the player looks away."},
        {"Afterimage seconds", &kAfterimage, offsetof(Values, afterimageSeconds), 10.0f, 1.0f,
         10.0f, "%.1f s",
         "The longest the exposure builds up to, which is also how long it takes to fade."},
        {"Afterimage darkness", &kAfterimage, offsetof(Values, afterimageDarkness), 0.9f, 0.0f, 1.0f,
         "%.2f", "How darkly the bleached core blocks the view at its peak."},
        {"Afterimage tint", &kAfterimage, offsetof(Values, afterimageTint), 0.35f, 0.0f, 1.0f,
         "%.2f", "How strongly the faint negative surround shows."},
        {"Afterimage saturation", &kAfterimage, offsetof(Values, afterimageSaturation), 0.30f, 0.0f,
         1.0f, "%.2f", "How much of that negative's colour survives."},
        {"Afterimage size", &kAfterimage, offsetof(Values, afterimageSize), 0.2f, 0.05f, 1.0f,
         "%.2f", "How wide the bleached region is, as a share of the glare's reach."},
        {"Afterimage haze", &kAfterimage, offsetof(Values, afterimageHaze), 0.5f, 0.0f, 1.0f,
         "%.2f", "How far the whole picture's range compresses while the eye recovers."},

        {"Night strength", &kNightVision, offsetof(Values, nightStrength), 1.0f, 0.0f, 1.0f, "%.2f",
         "How far the world drains to rod vision once the sun is down. Zero turns it off."},
        {"Night colour retained above", &kNightVision, offsetof(Values, nightColourAbove), 0.59f,
         0.02f, 2.0f, "%.2f",
         "The brightness at and above which a pixel keeps its colour: fires, lamps, headlights."},
        {"Purkinje shift", &kNightVision, offsetof(Values, nightPurkinje), 0.75f, 0.0f, 1.0f, "%.2f",
         "How far the drained picture turns from grey to the rods' blue-grey, with reds going dark."},
        {"Night moon colour", &kNightVision, offsetof(Values, nightMoonColour), 0.5f, 0.0f, 1.0f,
         "%.2f", "How much colour a high, uncovered moon lets the world keep. Moonless is grey."},
        {"Night noise", &kNightVision, offsetof(Values, nightNoise), 1.0f, 0.0f, 1.0f, "%.2f",
         "Faint moving grain in the darkest parts of the view."},

        {"Cloud shadow strength", &kCloudShadows, offsetof(Values, shadowStrength), 0.5f, 0.0f, 1.0f,
         "%.2f", "How much of the light on sunlit ground a cloud overhead takes away."},

        {"Grade saturation", &kColourGrade, offsetof(Values, gradeSaturation), 0.57f, 0.0f, 1.5f,
         "%.2f", "How much colour the final picture keeps. One is the scene's own; the engine's 0.5."},
        {"Grade contrast", &kColourGrade, offsetof(Values, gradeContrast), 0.13f, -0.5f, 1.0f, "%.2f",
         "How hard the S-curve bends the picture's tones. Zero leaves them straight."},
        {"Grade brightness", &kColourGrade, offsetof(Values, gradeBrightness), 1.09f, 0.5f, 2.0f,
         "%.2f", "Above one darkens the mid tones, below one lifts them. The engine's is about 1.12."},
        {"Grade warmth", &kColourGrade, offsetof(Values, gradeWarmth), 0.05f, -0.5f, 0.5f, "%.2f",
         "Above zero warms the picture, below cools it. The engine's 0.23 is its yellow cast."},
        {"Grade tint", &kColourGrade, offsetof(Values, gradeTint), -0.07f, -0.5f, 0.5f, "%.2f",
         "Below zero turns the picture green, above turns it magenta."},
    };

    constexpr Moment kMoments[] = {
        {"Night", 0}, {"Dawn", 5},       {"Sunrise", 6}, {"Morning", 8},
        {"Noon", 12}, {"Afternoon", 16}, {"Sunset", 18}, {"Dusk", 19},
    };

    Values g_values = {};
    // The moment whose tab was open when the moments last drew, or -1 before they first did.
    int g_openMoment = -1;
    // Set by an edit and cleared by the save that follows once nothing is held, so a slider writes
    // the file when it is let go rather than on every frame it moves.
    bool g_unsaved = false;

    float* Field(const Parameter& parameter) {
        return reinterpret_cast<float*>(reinterpret_cast<char*>(&g_values) + parameter.offset);
    }

    // Beside fcse.ini, in the folder the process was started from.
    const std::filesystem::path& File() {
        static const std::filesystem::path file = [] {
            std::wstring exe(32768, L'\0');
            exe.resize(GetModuleFileNameW(nullptr, exe.data(), static_cast<DWORD>(exe.size())));
            return std::filesystem::path(exe).parent_path() / L"sky-overhaul.ini";
        }();
        return file;
    }

    const char* Path() {
        static const std::string path = [] {
            const std::u8string utf8 = File().u8string();
            return std::string(utf8.begin(), utf8.end());
        }();
        return path.c_str();
    }

    std::string_view Trim(std::string_view text) {
        const size_t first = text.find_first_not_of(" \t\r");
        if (first == std::string_view::npos) {
            return {};
        }
        return text.substr(first, text.find_last_not_of(" \t\r") - first + 1);
    }

    void Save() {
        std::string text = "; Sky Overhaul. Edited in its window in DevTools' overlay; edits made "
                           "here by hand\n; are read at launch or by that window's Reload.\n";
        for (const Parameter& parameter : kParameters) {
            char value[32];
            const char* end = std::to_chars(value, value + sizeof(value), *Field(parameter)).ptr;
            text.append(parameter.key).append(" = ").append(value, end - value).append("\n");
        }

        std::ofstream file(File(), std::ios::trunc);
        file << text;
        file.close();
        if (!file) {
            FCSE::Logf("tuning: %s could not be written", Path());
        }
    }

    // Every row of a category, under a heading wherever the group changes. True when one changed.
    bool DrawGroups(const Category& category) {
        bool changed = false;
        const Group* group = nullptr;
        for (const Parameter& parameter : kParameters) {
            if (parameter.group->category != &category) {
                continue;
            }
            if (parameter.group != group) {
                group = parameter.group;
                ImGui::SeparatorText(group->name);
            }
            changed |= ImGui::SliderFloat(parameter.key, Field(parameter), parameter.min,
                                          parameter.max, parameter.format,
                                          ImGuiSliderFlags_AlwaysClamp);
            ImGui::SetItemTooltip("%s", parameter.help);
        }
        return changed;
    }

    // A ramp from black to white as the scene holds it, and under it as the grade would leave it.
    void DrawGreys() {
        // Odd, so the middle step is mid grey.
        constexpr int kSteps = 17;
        ImGui::SeparatorText("Greys, before and after");

        const float width = ImGui::GetContentRegionAvail().x;
        const float cell = width / kSteps;
        const float height = ImGui::GetFrameHeight() * 1.5f;
        const ImVec2 origin = ImGui::GetCursorScreenPos();
        ImDrawList* list = ImGui::GetWindowDrawList();
        float mid[3] = {};
        for (int i = 0; i < kSteps; i++) {
            const float grey = static_cast<float>(i) / (kSteps - 1);
            const float before[3] = {grey, grey, grey};
            float after[3];
            SkyOverhaul::Grade::Apply(g_values, before, after);
            if (i == kSteps / 2) {
                std::copy_n(after, 3, mid);
            }
            const float x = origin.x + cell * i;
            list->AddRectFilled({x, origin.y}, {x + cell, origin.y + height},
                                ImGui::ColorConvertFloat4ToU32({grey, grey, grey, 1.0f}));
            list->AddRectFilled({x, origin.y + height}, {x + cell, origin.y + 2.0f * height},
                                ImGui::ColorConvertFloat4ToU32({after[0], after[1], after[2], 1.0f}));
        }
        ImGui::Dummy({width, 2.0f * height});
        ImGui::Text("Mid grey becomes %.0f %.0f %.0f", mid[0] * 255.0f, mid[1] * 255.0f,
                    mid[2] * 255.0f);
    }

    void DrawNow() {
        SkyOverhaul::CloudLayer::Lighting lighting;
        if (!SkyOverhaul::CloudLayer::Latest(lighting)) {
            ImGui::TextDisabled("No world has been drawn yet.");
            return;
        }
        const int minutes = static_cast<int>(lighting.timeOfDay * 24.0f * 60.0f) % (24 * 60);
        ImGui::Text("Sun's time %02d:%02d", minutes / 60, minutes % 60);
    }

    // A tab per moment. Picking one sends the game's clock to its hour, from which the day runs on.
    void DrawMoments() {
        DrawNow();

        // Eight moments do not fit across the window, so they scroll rather than cut their names.
        if (!ImGui::BeginTabBar("moments", ImGuiTabBarFlags_FittingPolicyScroll |
                                               ImGuiTabBarFlags_TabListPopupButton)) {
            return;
        }
        for (int i = 0; i < static_cast<int>(std::size(kMoments)); i++) {
            if (!ImGui::BeginTabItem(kMoments[i].name)) {
                continue;
            }
            if (g_openMoment >= 0 && g_openMoment != i) {
                SkyOverhaul::TimeOfDay::Set(kMoments[i].hour * 60);
            }
            g_openMoment = i;
            ImGui::Text("At %02d:00", kMoments[i].hour);
            ImGui::EndTabItem();
        }
        ImGui::EndTabBar();
    }
}

SkyOverhaul::Tuning::Values SkyOverhaul::Tuning::Current() {
    return g_values;
}

void SkyOverhaul::Tuning::Load() {
    for (const Parameter& parameter : kParameters) {
        *Field(parameter) = parameter.defaultValue;
    }

    std::ifstream file(File());
    FCSE::Logf("tuning: %s %s", file.is_open() ? "reading" : "creating", Path());

    // A value that is not a number leaves its row at the default.
    std::string line;
    while (std::getline(file, line)) {
        const std::string_view text = Trim(line);
        const size_t equals = text.find('=');
        if (equals == std::string_view::npos) {
            continue;
        }
        const std::string_view key = Trim(text.substr(0, equals));
        const std::string_view value = Trim(text.substr(equals + 1));
        for (const Parameter& parameter : kParameters) {
            float read = 0.0f;
            if (key == parameter.key &&
                std::from_chars(value.data(), value.data() + value.size(), read).ec == std::errc{}) {
                *Field(parameter) = std::clamp(read, parameter.min, parameter.max);
            }
        }
    }

    Save();
}

void SkyOverhaul::Tuning::DrawWindow(void*) {
    ImGui::PushItemWidth(-ImGui::GetFontSize() * 12.0f);

    if (ImGui::BeginTabBar("categories")) {
        for (const Category* category : kTabs) {
            if (!ImGui::BeginTabItem(category->name)) {
                continue;
            }
            if (category == &kSky) {
                DrawMoments();
            } else {
                g_unsaved |= DrawGroups(*category);
            }
            if (category == &kGrade) {
                DrawGreys();
            }
            ImGui::EndTabItem();
        }
        ImGui::EndTabBar();
    }

    ImGui::Separator();
    if (ImGui::Button("Reload")) {
        Load();
    }
    ImGui::SameLine();
    ImGui::TextDisabled("%s", Path());
    ImGui::PopItemWidth();

    if (g_unsaved && !ImGui::IsAnyItemActive()) {
        Save();
        g_unsaved = false;
    }
}
