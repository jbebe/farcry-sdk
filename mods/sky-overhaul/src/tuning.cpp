#include "tuning.h"

#include "clouds.h"
#include "engine/cloud_layer.h"
#include "fcse_api.h"
#include "sky.h"

#include "imgui.h"

#include <algorithm>
#include <charconv>
#include <cmath>
#include <cstddef>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <iterator>
#include <span>
#include <string>
#include <string_view>

namespace {
    using SkyOverhaul::Tuning::Values;

    // A scalar is one float and a colour three, linear and allowed above one. A choice is the index
    // of its label.
    enum class Kind { Scalar, Colour, Choice };

    // Whether a value holds for the whole day or is set per key moment and blended between them.
    enum class Span { Day, Moment };

    struct Parameter {
        // The key in the file and the label in the window, unique within its span.
        const char* key;
        // The heading the window draws it under.
        const char* group;
        Kind kind;
        Span span;
        // Its member's offsetof in Values.
        size_t offset;
        float defaults[3];
        // A scalar's and a colour's every channel is held to these.
        float min;
        float max;
        // A scalar's printf format, which carries its unit.
        const char* format;
        std::span<const char* const> choices;
        // Shown when the row is hovered.
        const char* help;
    };

    // A named hour in the sun's day, which has sunrise at 06:00 and sunset at 18:00.
    struct Moment {
        const char* name;
        float hour;
    };

    // An hour of the sun's day, the two moments either side of it, and how far it is from the first
    // toward the second.
    struct Between {
        float hour;
        size_t from;
        size_t to;
        float weight;
    };

    // The most any colour channel is let go, which is several times what the brightest default asks.
    constexpr float kBrightest = 4.0f;

    constexpr const char* kDaySection = "Day";

    constexpr Span kDay = Span::Day;
    constexpr Span kMoment = Span::Moment;

    constexpr Parameter Scalar(const char* group, const char* key, Span span, size_t offset,
                               float value, float min, float max, const char* format,
                               const char* help) {
        return {key, group, Kind::Scalar, span, offset, {value}, min, max, format, {}, help};
    }

    constexpr Parameter Colour(const char* group, const char* key, size_t offset, float red,
                               float green, float blue, const char* help) {
        return {key,  group, Kind::Colour, kMoment, offset, {red, green, blue}, 0.0f, kBrightest,
                "",   {},    help};
    }

    constexpr Parameter Choice(const char* key, size_t offset,
                               std::span<const char* const> choices, const char* help) {
        return {key, "", Kind::Choice, kDay, offset, {}, 0.0f, 0.0f, "", choices, help};
    }

    constexpr const char* kSkyModes[] = {"Engine", "Overhaul"};
    constexpr const char* kCloudModes[] = {"Engine", "Off", "Overhaul"};

    // In the order the file and the window list them.
    constexpr Parameter kParameters[] = {
        Choice("Sky", offsetof(Values, skyMode), kSkyModes, "Whether our sky replaces the engine's."),
        Choice("Clouds", offsetof(Values, cloudMode), kCloudModes,
               "The engine's clouds, none, or ours. The engine's also feed the light shafts."),

        Scalar("Air", "Sky haze", kMoment, offsetof(Values, skyHaze), 1.0f, 0.0f, 2.5f, "%.2f",
               "How much dust and water the air carries: none is a hard blue sky over a sharp "
               "horizon, plenty a white one. Storms add to it."),
        Colour("Air", "Haze colour", offsetof(Values, hazeColour), 1.0f, 1.0f, 1.0f,
               "What the light the haze scatters is multiplied by: the glow around the sun and the "
               "pale of the horizon."),
        Scalar("Air", "Sky brightness", kMoment, offsetof(Values, skyBrightness), 1.0f, 0.0f, 3.0f,
               "%.2f", "How bright the sunlight reaching the air is, against a clear noon sky."),
        Colour("Air", "Sky colour", offsetof(Values, skyColour), 1.0f, 1.0f, 1.0f,
               "What all the light the air scatters is multiplied by."),
        Scalar("Air", "Zenith hold", kMoment, offsetof(Values, zenithHold), 1.0f, 0.0f, 1.0f, "%.2f",
               "How much of the brightness the zenith loses as the sun comes down is given back."),

        Scalar("Horizon", "Horizon gradient", kMoment, offsetof(Values, horizonGradient), 1.0f, 0.0f,
               1.0f, "%.2f",
               "How far a low sun's horizon turns from the sun's side to the far side's. A high sun "
               "is never affected."),
        Colour("Horizon", "Near horizon colour", offsetof(Values, nearHorizonColour), 1.0f, 1.0f,
               1.0f, "What the sky toward the horizon is multiplied by on the sun's side."),
        Scalar("Horizon", "Near horizon brightness", kMoment,
               offsetof(Values, nearHorizonBrightness), 1.0f, 0.0f, 3.0f, "%.2f",
               "How bright the sky toward the horizon is on the sun's side."),
        Colour("Horizon", "Far horizon colour", offsetof(Values, farHorizonColour), 1.0f, 1.0f, 1.0f,
               "What the sky toward the horizon is multiplied by on the side away from the sun."),
        Scalar("Horizon", "Far horizon brightness", kMoment, offsetof(Values, farHorizonBrightness),
               0.3f, 0.0f, 1.0f, "%.2f",
               "How bright the far side of a low sun's horizon ends up, as a share of the light the "
               "air sends from there."),
        Scalar("Horizon", "Horizon match", kMoment, offsetof(Values, horizonMatch), 1.0f, 0.0f, 1.0f,
               "%.2f",
               "How far the world's distant fog is carried from the engine's colour toward the "
               "sky's horizon."),

        Scalar("Below the horizon", "Below horizon brown", kMoment, offsetof(Values, groundBrown),
               0.5f, 0.0f, 1.0f, "%.2f",
               "How far the ground the world never drew, below a low sun's far horizon, turns from "
               "the horizon's colour toward the one below."),
        Colour("Below the horizon", "Below horizon colour", offsetof(Values, groundColour), 1.236f,
               0.939f, 0.692f,
               "That ground's colour at a brightness of one; it keeps the horizon's brightness."),

        Scalar("Night", "Night sky", kMoment, offsetof(Values, nightSky), 1.0f, 0.0f, 3.0f, "%.2f",
               "How dark the sky may go, as a multiple of the colour below. It only fills in where "
               "the air comes out darker."),
        Colour("Night", "Night sky colour", offsetof(Values, nightSkyColour), 0.039f, 0.055f, 0.094f,
               "The darkest the sky is let go: the stars' own backdrop."),
        Scalar("Night", "Twilight sky", kMoment, offsetof(Values, twilightSky), 1.0f, 0.0f, 3.0f,
               "%.2f",
               "How far that floor is lifted toward the colour below from twelve degrees below the "
               "horizon to six above."),
        Colour("Night", "Twilight colour", offsetof(Values, twilightColour), 0.50f, 1.04f, 2.11f,
               "The colour of that lift at a luminance of one."),

        Scalar("Clouds", "Cloud coverage", kMoment, offsetof(Values, cloudCoverage), 0.45f, 0.0f,
               1.0f, "%.2f", "How much of the sky the layer fills."),
        Scalar("Clouds", "Cloud density", kMoment, offsetof(Values, cloudDensity), 0.04f, 0.005f,
               0.3f, "%.3f", "How solid the cloud is where it is filled."),
        Scalar("Clouds", "Cloud detail", kMoment, offsetof(Values, cloudDetail), 0.35f, 0.0f, 1.0f,
               "%.2f", "How hard the cloud's edges are torn."),
        Scalar("Clouds", "Cloud haze", kMoment, offsetof(Values, cloudHaze), 3000.0f, 300.0f,
               20000.0f, "%.0f m", "Over how far a cloud turns into the horizon behind it."),
        Scalar("Clouds", "Cirrus", kMoment, offsetof(Values, cirrus), 0.35f, 0.0f, 1.0f, "%.2f",
               "How much of the sky the high sheet of ice cloud reaches across."),
        Scalar("Clouds", "Cirrus opacity", kMoment, offsetof(Values, cirrusOpacity), 0.7f, 0.0f, 1.0f,
               "%.2f", "How solid that sheet is where it reaches."),
        Scalar("Clouds", "Contrails", kMoment, offsetof(Values, contrails), 0.6f, 0.0f, 1.0f, "%.2f",
               "How strongly the two aircraft trails show."),

        Scalar("Moon", "Moonlight", kMoment, offsetof(Values, moonlight), 1.0f, 0.0f, 1.0f, "%.2f",
               "How strongly the moon lights the clouds once the sun has set."),
        Colour("Moon", "Moon colour", offsetof(Values, moonColour), 1.6f, 1.8f, 2.0f,
               "The moonlight on the clouds and the glow around the moon, at full strength."),
        Scalar("Moon", "Moon glow", kMoment, offsetof(Values, moonGlow), 0.25f, 0.0f, 1.0f, "%.2f",
               "How brightly the air glows around the moon."),

        Scalar("Cloud layer", "Cloud base", kDay, offsetof(Values, cloudBase), 1200.0f, 100.0f,
               4000.0f, "%.0f m", "Where the layer's floor sits."),
        Scalar("Cloud layer", "Cloud thickness", kDay, offsetof(Values, cloudThickness), 700.0f,
               100.0f, 3000.0f, "%.0f m", "How deep the layer is."),
        Scalar("Cloud layer", "Cloud size", kDay, offsetof(Values, cloudSize), 4000.0f, 500.0f,
               12000.0f, "%.0f m", "How wide one repeat of the shape is, which is how large a cloud reads."),
        Scalar("Cloud layer", "Cloud wind", kDay, offsetof(Values, cloudWind), 1.0f, 0.0f, 5.0f,
               "%.2f", "How fast the layer drifts, as a multiple of the engine's wind."),

        Scalar("Sun glare", "Sun glare strength", kDay, offsetof(Values, glareStrength), 1.0f, 0.0f,
               2.0f, "%.2f", "Peak brightness with the sun dead centre. Zero turns the glare off."),
        Scalar("Sun glare", "Sun glare spread", kDay, offsetof(Values, glareSpread), 57.0f, 10.0f,
               180.0f, "%.0f deg", "How far off centre the sun gets before the glare reaches nothing."),
        Scalar("Sun glare", "Sun glare falloff", kDay, offsetof(Values, glareFalloff), 2.0f, 1.0f,
               8.0f, "%.1f", "How sharply the glare falls away from dead centre."),
        Scalar("Sun glare", "Sun glare veil", kDay, offsetof(Values, glareVeil), 1.0f, 0.0f, 1.0f,
               "%.2f", "How much of the glare covers the frame wherever the sun sits in it."),
        Scalar("Sun glare", "Sun glare contrast", kDay, offsetof(Values, glareContrast), 1.95f, 0.0f,
               4.0f, "%.2f", "How hard the contrast is pushed at full dazzle."),
        Scalar("Sun glare", "Sun glare desaturation", kDay, offsetof(Values, glareDesaturation),
               1.29f, 0.0f, 3.0f, "%.2f",
               "How fast colour drains as the dazzle rises. One reaches grey only at full dazzle."),
        Scalar("Sun glare", "Sun elevation ramp", kDay, offsetof(Values, elevationRamp), 40.0f, 1.0f,
               45.0f, "%.0f deg", "The sun's elevation at which the glare reaches full strength."),

        Scalar("Afterimage", "Afterimage strength", kDay, offsetof(Values, afterimageStrength), 1.0f,
               0.0f, 1.0f, "%.2f", "How strongly the burned-in view shows once the player looks away."),
        Scalar("Afterimage", "Afterimage seconds", kDay, offsetof(Values, afterimageSeconds), 10.0f,
               1.0f, 10.0f, "%.1f s",
               "The longest the exposure builds up to, which is also how long it takes to fade."),
        Scalar("Afterimage", "Afterimage darkness", kDay, offsetof(Values, afterimageDarkness), 0.9f,
               0.0f, 1.0f, "%.2f", "How darkly the bleached core blocks the view at its peak."),
        Scalar("Afterimage", "Afterimage tint", kDay, offsetof(Values, afterimageTint), 0.35f, 0.0f,
               1.0f, "%.2f", "How strongly the faint negative surround shows."),
        Scalar("Afterimage", "Afterimage saturation", kDay, offsetof(Values, afterimageSaturation),
               0.30f, 0.0f, 1.0f, "%.2f", "How much of that negative's colour survives."),
        Scalar("Afterimage", "Afterimage size", kDay, offsetof(Values, afterimageSize), 0.25f, 0.05f,
               1.0f, "%.2f", "How wide the bleached region is, as a share of the glare's reach."),
        Scalar("Afterimage", "Afterimage haze", kDay, offsetof(Values, afterimageHaze), 0.35f, 0.0f,
               1.0f, "%.2f", "How far the whole picture's range compresses while the eye recovers."),
    };

    constexpr Moment kMoments[] = {
        {"Night", 0.0f},  {"Dawn", 5.0f},       {"Sunrise", 6.0f}, {"Morning", 8.0f},
        {"Noon", 12.0f},  {"Afternoon", 16.0f}, {"Sunset", 18.0f}, {"Dusk", 19.0f},
    };

    Values g_day = {};
    Values g_moments[std::size(kMoments)] = {};
    // The moment shown at every hour while it is tuned, or -1. Never stored.
    int g_preview = -1;
    // Set by an edit and cleared by the save that follows once nothing is held, so a slider writes
    // the file when it is let go rather than on every frame it moves.
    bool g_unsaved = false;

    float* Field(Values& values, const Parameter& parameter) {
        return reinterpret_cast<float*>(reinterpret_cast<char*>(&values) + parameter.offset);
    }

    int Channels(const Parameter& parameter) {
        return parameter.kind == Kind::Colour ? 3 : 1;
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

    // Leaves the row as it was when the text is not a value of its kind.
    void ReadValue(const Parameter& parameter, std::string_view text, Values& values) {
        float* field = Field(values, parameter);
        if (parameter.kind == Kind::Choice) {
            const auto found = std::find(parameter.choices.begin(), parameter.choices.end(), text);
            if (found != parameter.choices.end()) {
                field[0] = static_cast<float>(found - parameter.choices.begin());
            }
            return;
        }

        float read[3];
        const char* at = text.data();
        const char* end = at + text.size();
        for (int channel = 0; channel < Channels(parameter); channel++) {
            at = std::find_if(at, end, [](char c) { return c != ' '; });
            const auto [next, error] = std::from_chars(at, end, read[channel]);
            if (error != std::errc{}) {
                return;
            }
            at = next;
        }
        for (int channel = 0; channel < Channels(parameter); channel++) {
            field[channel] = std::clamp(read[channel], parameter.min, parameter.max);
        }
    }

    void WriteSection(std::string& out, const char* name, Span span, Values& values) {
        out.append("\n[").append(name).append("]\n");
        for (const Parameter& parameter : kParameters) {
            if (parameter.span != span) {
                continue;
            }
            out.append(parameter.key).append(" = ");
            const float* field = Field(values, parameter);
            if (parameter.kind == Kind::Choice) {
                out.append(parameter.choices[static_cast<size_t>(field[0])]);
            } else {
                for (int channel = 0; channel < Channels(parameter); channel++) {
                    char text[32];
                    const char* end = std::to_chars(text, text + sizeof(text), field[channel]).ptr;
                    out.append(channel == 0 ? "" : " ").append(text, end - text);
                }
            }
            out.append("\n");
        }
    }

    // The stored values a section of the file holds, or null for one it does not know.
    Values* Section(std::string_view name) {
        for (size_t i = 0; i < std::size(kMoments); i++) {
            if (name == kMoments[i].name) {
                return &g_moments[i];
            }
        }
        return name == kDaySection ? &g_day : nullptr;
    }

    // Where a time-of-day coordinate falls among the moments, wrapping across midnight.
    Between Locate(float timeOfDay) {
        const float hour =
            std::isfinite(timeOfDay) ? (timeOfDay - std::floor(timeOfDay)) * 24.0f : 0.0f;

        size_t from = std::size(kMoments) - 1;
        for (size_t i = 0; i < std::size(kMoments); i++) {
            if (kMoments[i].hour <= hour) {
                from = i;
            }
        }
        const size_t to = (from + 1) % std::size(kMoments);

        const float start = kMoments[from].hour - (kMoments[from].hour > hour ? 24.0f : 0.0f);
        const float end = kMoments[to].hour + (kMoments[to].hour <= start ? 24.0f : 0.0f);
        return {hour, from, to, (hour - start) / (end - start)};
    }

    void ApplyChoices() {
        SkyOverhaul::Sky::SetEnabled(g_day.skyMode == 1.0f);
        SkyOverhaul::CloudLayer::SetMode(g_day.cloudMode == 0.0f
                                             ? SkyOverhaul::CloudLayer::Mode::Engine
                                             : SkyOverhaul::CloudLayer::Mode::Off);
        SkyOverhaul::Clouds::SetEnabled(g_day.cloudMode == 2.0f);
    }

    void Save() {
        std::string text = "; Sky Overhaul. Edited in its window in DevTools' overlay; edits made "
                           "here by hand\n; are read at launch or by that window's Reload.\n";
        WriteSection(text, kDaySection, Span::Day, g_day);
        for (size_t i = 0; i < std::size(kMoments); i++) {
            WriteSection(text, kMoments[i].name, Span::Moment, g_moments[i]);
        }

        std::ofstream file(File(), std::ios::trunc);
        file << text;
        file.close();
        if (!file) {
            FCSE::Logf("tuning: %s could not be written", Path());
        }
    }

    // True when the row changed.
    bool DrawRow(const Parameter& parameter, Values& values) {
        float* field = Field(values, parameter);
        bool changed = false;
        switch (parameter.kind) {
        case Kind::Scalar:
            changed = ImGui::SliderFloat(parameter.key, field, parameter.min, parameter.max,
                                         parameter.format, ImGuiSliderFlags_AlwaysClamp);
            break;
        case Kind::Colour:
            changed = ImGui::ColorEdit3(parameter.key, field,
                                        ImGuiColorEditFlags_Float | ImGuiColorEditFlags_HDR);
            // HDR leaves the picker unbounded above.
            for (int channel = 0; changed && channel < 3; channel++) {
                field[channel] = std::clamp(field[channel], parameter.min, parameter.max);
            }
            break;
        case Kind::Choice: {
            int index = static_cast<int>(field[0]);
            changed = ImGui::Combo(parameter.key, &index, parameter.choices.data(),
                                   static_cast<int>(parameter.choices.size()));
            field[0] = static_cast<float>(index);
            break;
        }
        }
        ImGui::SetItemTooltip("%s", parameter.help);
        return changed;
    }

    // Every row of a span but the modes, under a heading wherever the group changes.
    bool DrawGroups(Span span, Values& values) {
        bool changed = false;
        const char* group = nullptr;
        for (const Parameter& parameter : kParameters) {
            if (parameter.span != span || parameter.kind == Kind::Choice) {
                continue;
            }
            if (group == nullptr || std::strcmp(group, parameter.group) != 0) {
                group = parameter.group;
                ImGui::SeparatorText(group);
            }
            changed |= DrawRow(parameter, values);
        }
        return changed;
    }

    void DrawNow() {
        SkyOverhaul::CloudLayer::Lighting lighting;
        if (!SkyOverhaul::CloudLayer::Latest(lighting)) {
            ImGui::TextDisabled("No world has been drawn yet.");
            return;
        }
        const Between between = Locate(lighting.timeOfDay);
        const int minutes = static_cast<int>(between.hour * 60.0f);
        ImGui::Text("Sun's time %02d:%02d - %.0f%% %s, %.0f%% %s", minutes / 60, minutes % 60,
                    (1.0f - between.weight) * 100.0f, kMoments[between.from].name,
                    between.weight * 100.0f, kMoments[between.to].name);
    }

    // A moment's header row: its hour, whether it is shown at every hour, and copying it to the
    // others. True when the copy was made.
    bool DrawMomentHeader(size_t index) {
        const int moment = static_cast<int>(index);
        const int minutes = static_cast<int>(kMoments[index].hour * 60.0f);
        ImGui::Text("At %02d:%02d", minutes / 60, minutes % 60);

        // While a moment is shown at every hour, it is the open tab's.
        bool shown = g_preview >= 0;
        if (shown) {
            g_preview = moment;
        }
        ImGui::SameLine();
        if (ImGui::Checkbox("Show at every hour", &shown)) {
            g_preview = shown ? moment : -1;
        }

        ImGui::SameLine();
        if (ImGui::Button("Copy to every moment")) {
            ImGui::OpenPopup("copy");
        }
        bool copied = false;
        if (ImGui::BeginPopup("copy")) {
            ImGui::Text("Overwrite every other moment with %s?", kMoments[index].name);
            if (ImGui::Button("Overwrite")) {
                const Values source = g_moments[index];
                std::fill(std::begin(g_moments), std::end(g_moments), source);
                copied = true;
                ImGui::CloseCurrentPopup();
            }
            ImGui::EndPopup();
        }
        return copied;
    }
}

SkyOverhaul::Tuning::Values SkyOverhaul::Tuning::Evaluate(float timeOfDay) {
    Between between = Locate(timeOfDay);
    if (g_preview >= 0) {
        between.from = between.to = static_cast<size_t>(g_preview);
    }
    Values& from = g_moments[between.from];
    Values& to = g_moments[between.to];

    Values result = g_day;
    for (const Parameter& parameter : kParameters) {
        if (parameter.span != Span::Moment) {
            continue;
        }
        const float* a = Field(from, parameter);
        const float* b = Field(to, parameter);
        float* out = Field(result, parameter);
        for (int channel = 0; channel < Channels(parameter); channel++) {
            out[channel] = a[channel] + (b[channel] - a[channel]) * between.weight;
        }
    }
    return result;
}

void SkyOverhaul::Tuning::Load() {
    Values defaults = {};
    for (const Parameter& parameter : kParameters) {
        std::copy_n(parameter.defaults, Channels(parameter), Field(defaults, parameter));
    }
    g_day = defaults;
    std::fill(std::begin(g_moments), std::end(g_moments), defaults);

    std::ifstream file(File());
    FCSE::Logf("tuning: %s %s", file.is_open() ? "reading" : "creating", Path());

    Values* section = nullptr;
    std::string line;
    while (std::getline(file, line)) {
        const std::string_view text = Trim(line);
        if (text.size() >= 2 && text.front() == '[' && text.back() == ']') {
            section = Section(text.substr(1, text.size() - 2));
            continue;
        }
        const size_t equals = text.find('=');
        if (section == nullptr || equals == std::string_view::npos) {
            continue;
        }
        const Span span = section == &g_day ? Span::Day : Span::Moment;
        const std::string_view key = Trim(text.substr(0, equals));
        for (const Parameter& parameter : kParameters) {
            if (parameter.span == span && key == parameter.key) {
                ReadValue(parameter, Trim(text.substr(equals + 1)), *section);
            }
        }
    }

    ApplyChoices();
    Save();
}

void SkyOverhaul::Tuning::DrawWindow(void*) {
    ImGui::PushItemWidth(-ImGui::GetFontSize() * 12.0f);
    bool changed = false;

    for (const Parameter& parameter : kParameters) {
        if (parameter.kind == Kind::Choice && DrawRow(parameter, g_day)) {
            ApplyChoices();
            changed = true;
        }
    }

    DrawNow();

    if (ImGui::BeginTabBar("moments")) {
        for (size_t i = 0; i < std::size(kMoments); i++) {
            if (ImGui::BeginTabItem(kMoments[i].name)) {
                changed |= DrawMomentHeader(i);
                changed |= DrawGroups(Span::Moment, g_moments[i]);
                ImGui::EndTabItem();
            }
        }
        ImGui::EndTabBar();
    }

    if (ImGui::CollapsingHeader("Whole day")) {
        changed |= DrawGroups(Span::Day, g_day);
    }

    ImGui::Separator();
    if (ImGui::Button("Reload")) {
        Load();
    }
    ImGui::SameLine();
    ImGui::TextDisabled("%s", Path());
    ImGui::PopItemWidth();

    g_unsaved |= changed;
    if (g_unsaved && !ImGui::IsAnyItemActive()) {
        Save();
        g_unsaved = false;
    }
}
