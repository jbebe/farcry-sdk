// Every value the driving is tuned by, kept in bin\vehicle-overhaul.ini and edited in DevTools'
// overlay.
#include "tuning.h"

#include "physics.h"
#include "sounds.h"

#include "fcse_api.h"
#include "imgui.h"

#include <windows.h>

#include <algorithm>
#include <charconv>
#include <cstddef>
#include <filesystem>
#include <fstream>
#include <string>
#include <string_view>

namespace {
    using VehicleOverhaul::Tuning::Values;

    struct Parameter {
        // The key in the file and the label in the window.
        const char* key;
        // The heading it is drawn under.
        const char* group;
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

    constexpr const char* kEngine = "Engine and gearbox";
    constexpr const char* kBrakes = "Brakes";
    constexpr const char* kSteering = "Steering";
    constexpr const char* kGrip = "Grip";
    constexpr const char* kChassis = "Chassis";
    constexpr const char* kSuspension = "Suspension";

    constexpr Parameter kParameters[] = {
        {"Engine power", kEngine, offsetof(Values, enginePower), 0.7f, 0.2f, 2.0f, "%.2fx",
         "The engine's peak torque, as a multiple of the car's own."},
        {"Climb assist", kEngine, offsetof(Values, climbAssist), 0.0f, 0.0f, 1.0f, "%.2fx",
         "The torque the game adds while the nose points uphill (the Rover's is 400 on top of 95)."},
        {"Top speed", kEngine, offsetof(Values, topSpeed), 2.0f, 0.5f, 3.0f, "%.2fx",
         "Gearing, as a multiple of the car's own top speed. Higher is faster but pulls less in each gear."},
        {"Shift time", kEngine, offsetof(Values, shiftTime), 0.6f, 0.0f, 2.0f, "%.2f s",
         "How long a gear change cuts the drive while the clutch is out. Retail changes gear instantly."},
        {"Brake torque", kBrakes, offsetof(Values, brakeTorque), 0.3f, 0.1f, 1.5f, "%.2fx",
         "The brakes' strength, as a multiple of the car's own."},
        {"Lock-up time", kBrakes, offsetof(Values, lockTime), 0.5f, 0.1f, 5.0f, "%.2f s",
         "How long a fully pressed brake takes to lock the wheels into a skid. Retail never locks them."},
        {"Steering lock", kSteering, offsetof(Values, steeringLock), 1.5f, 0.5f, 2.0f, "%.2fx",
         "How far a held steering key turns the front wheels standing still or crawling, as a multiple of "
         "the car's own (25 degrees on the Datsun). The wheels wind out to it at the car's own rate."},
        {"Steering at speed", kSteering, offsetof(Values, steeringAtSpeed), 2.0f, 0.5f, 3.0f, "%.2fx",
         "How far a held steering key turns the front wheels at speed, as a multiple of the car's own (10 "
         "degrees on the Datsun, from 43 km/h up). The game narrows the lock towards it as the car speeds "
         "up."},
        {"Tyre grip", kGrip, offsetof(Values, tyreGrip), 0.3f, 0.1f, 1.5f, "%.2fx",
         "The tyres' grip before the ground's own (retail 3.0, against 0.65 to 0.8 for the ground)."},
        {"Downforce", kGrip, offsetof(Values, downforce), 0.0f, 0.0f, 1.0f, "%.2fx",
         "The game's extra 5 m/s2 of gravity on every car, which presses the tyres down."},
        {"Rolling resistance", kGrip, offsetof(Values, rollingResistance), 0.15f, 0.0f, 1.0f, "%.2fx",
         "What slows a coasting car (retail about 1.5 m/s2, ten times a real tyre's)."},
        {"Pitch response", kChassis, offsetof(Values, pitchResponse), 1.0f, 0.0f, 1.0f, "%.2f",
         "How much the tyres' forces pitch the body nose up or down. 1 is physical, retail 0.5."},
        {"Roll response", kChassis, offsetof(Values, rollResponse), 1.0f, 0.0f, 1.0f, "%.2f",
         "How much they lean the body over in a turn, up to a rollover. 1 is physical, retail 0.25."},
        {"Yaw response", kChassis, offsetof(Values, yawResponse), 1.0f, 0.0f, 1.0f, "%.2f",
         "How much they swing the tail out, up to a spin. 1 is physical, retail 0.35."},
        {"Spin damping", kChassis, offsetof(Values, spinDamping), 0.0f, 0.0f, 1.0f, "%.2fx",
         "The game's brake on slow spins of the body, as a multiple of its own."},
        {"Springs", kSuspension, offsetof(Values, springs), 0.6f, 0.3f, 1.5f, "%.2fx",
         "Spring stiffness, as a multiple of the car's own."},
        {"Travel", kSuspension, offsetof(Values, suspensionTravel), 1.0f, 0.5f, 2.5f, "%.2fx",
         "How far the wheels can move, as a multiple of the car's own suspension length. Longer lifts the "
         "body and gives soft springs room before they bottom out."},
        {"Compression damping", kSuspension, offsetof(Values, compressionDamping), 0.6f, 0.1f, 1.5f, "%.2fx",
         "How hard the shock absorbers resist being pushed in, as a multiple of the car's own. Lower lets "
         "the body dip further into a corner, a stop or a bump."},
        {"Rebound damping", kSuspension, offsetof(Values, reboundDamping), 0.3f, 0.1f, 1.5f, "%.2fx",
         "How hard they resist springing back out, as a multiple of the car's own. Lower lets the body "
         "sway and bounce longer before it settles."},
    };

    // The switches, kept in the file as 1 or 0 above the values, and on by default.
    struct Switch {
        const char* key;
        bool Values::*member;
        // The label in the window, and the heading it is drawn under; null draws it above them all.
        const char* label;
        const char* group;
        const char* help;
    };

    constexpr Switch kSwitches[] = {
        {"Enabled", &Values::enabled, "Overhaul the car you drive", nullptr,
         "Off puts the car back as the game made it, to compare."},
        {"Real engine", &Values::realEngine, "Real engine", kEngine,
         "The engine sound and rev counter follow the real gearbox: idle, revs against the clutch while "
         "pulling away, and a drop on every gear change. Off leaves the game's revs made up from speed."},
        {"Real centre of mass", &Values::realCentreOfMass, "Real centre of mass", kChassis,
         "Carries the car's weight as high as the real vehicle it depicts, from that vehicle's stability "
         "factor. Off leaves it where the game put it."},
        {"Solid axles", &Values::solidAxles, "Solid axles", kSuspension,
         "Springs each solid axle of the real vehicle as one beam, which twists under the body to keep both "
         "wheels down and lets them hang lower. Only the Land Rover so far. Off springs every wheel on its "
         "own, as the game does."},
    };

    Values g_values{};

    // The switches drawn under `group`. True when one changed.
    bool DrawSwitches(const char* group) {
        bool changed = false;
        for (const Switch& entry : kSwitches) {
            if (entry.group == group) {
                changed |= ImGui::Checkbox(entry.label, &(g_values.*entry.member));
                ImGui::SetItemTooltip("%s", entry.help);
            }
        }
        return changed;
    }

    // Moved sliders are written to the file once they are let go rather than on every frame.
    bool g_unsaved = false;

    float* Field(const Parameter& parameter) {
        return reinterpret_cast<float*>(reinterpret_cast<char*>(&g_values) + parameter.offset);
    }

    // Beside fcse.ini, in the folder the process was started from.
    const std::filesystem::path& File() {
        static const std::filesystem::path file = [] {
            std::wstring exe(32768, L'\0');
            exe.resize(GetModuleFileNameW(nullptr, exe.data(), static_cast<DWORD>(exe.size())));
            return std::filesystem::path(exe).parent_path() / L"vehicle-overhaul.ini";
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
        std::string text = "; Vehicle Overhaul. Edited in its window in DevTools' overlay; edits made here "
                           "by hand\n; are read at launch or by that window's Reload.\n";
        for (const Switch& entry : kSwitches) {
            text.append(entry.key).append(g_values.*entry.member ? " = 1\n" : " = 0\n");
        }
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

    void DrawReadout() {
        VehicleOverhaul::Physics::Status status{};
        if (!VehicleOverhaul::Physics::Latest(status)) {
            ImGui::TextDisabled("Not driving.");
            return;
        }
        ImGui::Text("%s   %.0f km/h   %.0f rpm   gear %d", status.name != nullptr ? status.name : "Unknown car",
                    status.motion.speed * 3.6f, status.motion.rpm, status.motion.gear + 1);
        ImGui::Text("Centre of mass %.2f m up, tips over at %.2f g; the tyres hold %.2f g here", status.height,
                    status.stability, status.grip);
    }
}

VehicleOverhaul::Tuning::Values VehicleOverhaul::Tuning::Current() { return g_values; }

void VehicleOverhaul::Tuning::Load() {
    for (const Switch& entry : kSwitches) {
        g_values.*entry.member = true;
    }
    for (const Parameter& parameter : kParameters) {
        *Field(parameter) = parameter.defaultValue;
    }

    std::ifstream file(File());

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
        float read = 0.0f;
        if (std::from_chars(value.data(), value.data() + value.size(), read).ec != std::errc{}) {
            continue;
        }
        for (const Switch& entry : kSwitches) {
            if (key == entry.key) {
                g_values.*entry.member = read != 0.0f;
            }
        }
        for (const Parameter& parameter : kParameters) {
            if (key == parameter.key) {
                *Field(parameter) = std::clamp(read, parameter.min, parameter.max);
            }
        }
    }

    Save();
}

namespace {
    void DrawDriving() {
        ImGui::PushItemWidth(-ImGui::GetFontSize() * 10.0f);

        DrawReadout();
        g_unsaved |= DrawSwitches(nullptr);

        const char* group = nullptr;
        for (const Parameter& parameter : kParameters) {
            if (parameter.group != group) {
                group = parameter.group;
                ImGui::SeparatorText(group);
                g_unsaved |= DrawSwitches(group);
            }
            g_unsaved |= ImGui::SliderFloat(parameter.key, Field(parameter), parameter.min, parameter.max,
                                            parameter.format, ImGuiSliderFlags_AlwaysClamp);
            ImGui::SetItemTooltip("%s", parameter.help);
        }

        ImGui::Separator();
        if (ImGui::Button("Reload")) {
            VehicleOverhaul::Tuning::Load();
        }
        ImGui::SameLine();
        ImGui::TextDisabled("%s", Path());
        ImGui::PopItemWidth();

        if (g_unsaved && !ImGui::IsAnyItemActive()) {
            Save();
            g_unsaved = false;
        }
    }
}

void VehicleOverhaul::Tuning::DrawWindow(void*) {
    if (!ImGui::BeginTabBar("tabs")) {
        return;
    }
    if (ImGui::BeginTabItem("Driving")) {
        DrawDriving();
        ImGui::EndTabItem();
    }
    if (ImGui::BeginTabItem("Sounds")) {
        Sounds::DrawTab();
        ImGui::EndTabItem();
    }
    ImGui::EndTabBar();
}
