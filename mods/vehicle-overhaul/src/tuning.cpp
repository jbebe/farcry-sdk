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
#include <vector>

namespace {
    using VehicleOverhaul::Tuning::Switches;
    using VehicleOverhaul::Tuning::Values;

    struct Parameter {
        // The key in the file and the label in the window.
        const char* key;
        // The heading it is drawn under.
        const char* group;
        float Values::*member;
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
        {"Engine power", kEngine, &Values::enginePower, 0.7f, 0.2f, 2.0f, "%.2fx",
         "The engine's peak torque, as a multiple of the car's own."},
        {"Climb assist", kEngine, &Values::climbAssist, 0.0f, 0.0f, 1.0f, "%.2fx",
         "The torque the game adds while the nose points uphill (the Rover's is 400 on top of 95)."},
        {"Top speed", kEngine, &Values::topSpeed, 2.0f, 0.5f, 3.0f, "%.2fx",
         "Gearing, as a multiple of the car's own top speed. Higher is faster but pulls less in each gear."},
        {"Shift time", kEngine, &Values::shiftTime, 0.6f, 0.0f, 2.0f, "%.2f s",
         "How long a gear change cuts the drive while the clutch is out. Retail changes gear instantly."},
        {"Brake torque", kBrakes, &Values::brakeTorque, 0.3f, 0.1f, 1.5f, "%.2fx",
         "The brakes' strength, as a multiple of the car's own."},
        {"Lock-up time", kBrakes, &Values::lockTime, 0.5f, 0.1f, 5.0f, "%.2f s",
         "How long a fully pressed brake takes to lock the wheels into a skid. Retail never locks them."},
        {"Steering lock", kSteering, &Values::steeringLock, 1.5f, 0.5f, 2.0f, "%.2fx",
         "How far a held steering key turns the front wheels standing still or crawling, as a multiple of "
         "the car's own (25 degrees on the Datsun). The wheels wind out to it at the car's own rate."},
        {"Steering at speed", kSteering, &Values::steeringAtSpeed, 2.0f, 0.5f, 3.0f, "%.2fx",
         "How far a held steering key turns the front wheels at speed, as a multiple of the car's own (10 "
         "degrees on the Datsun, from 43 km/h up). The game narrows the lock towards it as the car speeds "
         "up."},
        {"Tyre grip", kGrip, &Values::tyreGrip, 0.3f, 0.1f, 1.5f, "%.2fx",
         "The tyres' grip before the ground's own (retail 3.0, against 0.65 to 0.8 for the ground)."},
        {"Downforce", kGrip, &Values::downforce, 0.0f, 0.0f, 1.0f, "%.2fx",
         "The game's extra 5 m/s2 of gravity on every car, which presses the tyres down."},
        {"Rolling resistance", kGrip, &Values::rollingResistance, 0.15f, 0.0f, 1.0f, "%.2fx",
         "What slows a coasting car (retail about 1.5 m/s2, ten times a real tyre's)."},
        {"Pitch response", kChassis, &Values::pitchResponse, 1.0f, 0.0f, 1.0f, "%.2f",
         "How much the tyres' forces pitch the body nose up or down. 1 is physical, retail 0.5."},
        {"Roll response", kChassis, &Values::rollResponse, 1.0f, 0.0f, 1.0f, "%.2f",
         "How much they lean the body over in a turn, up to a rollover. 1 is physical, retail 0.25."},
        {"Yaw response", kChassis, &Values::yawResponse, 1.0f, 0.0f, 1.0f, "%.2f",
         "How much they swing the tail out, up to a spin. 1 is physical, retail 0.35."},
        {"Spin damping", kChassis, &Values::spinDamping, 0.0f, 0.0f, 1.0f, "%.2fx",
         "The game's brake on slow spins of the body, as a multiple of its own."},
        {"Springs", kSuspension, &Values::springs, 0.6f, 0.3f, 1.5f, "%.2fx",
         "Spring stiffness, as a multiple of the car's own."},
        {"Travel", kSuspension, &Values::suspensionTravel, 1.0f, 0.5f, 2.5f, "%.2fx",
         "How far the wheels can move, as a multiple of the car's own suspension length. Longer lifts the "
         "body and gives soft springs room before they bottom out."},
        {"Compression damping", kSuspension, &Values::compressionDamping, 0.6f, 0.1f, 1.5f, "%.2fx",
         "How hard the shock absorbers resist being pushed in, as a multiple of the car's own. Lower lets "
         "the body dip further into a corner, a stop or a bump."},
        {"Rebound damping", kSuspension, &Values::reboundDamping, 0.3f, 0.1f, 1.5f, "%.2fx",
         "How hard they resist springing back out, as a multiple of the car's own. Lower lets the body "
         "sway and bounce longer before it settles."},
    };

    // The switches, kept in the file as 1 or 0 above the cars, and on by default.
    struct Switch {
        const char* key;
        bool Switches::*member;
        // The label in the window, and the heading it is drawn under; null draws it above them all.
        const char* label;
        const char* group;
        const char* help;
    };

    constexpr Switch kSwitches[] = {
        {"Enabled", &Switches::enabled, "Overhaul the car you drive", nullptr,
         "Off puts the car back as the game made it, to compare."},
        {"Real engine", &Switches::realEngine, "Real engine", kEngine,
         "The engine sound and rev counter follow the real gearbox: idle, revs against the clutch while "
         "pulling away, and a drop on every gear change. Off leaves the game's revs made up from speed."},
        {"Real centre of mass", &Switches::realCentreOfMass, "Real centre of mass", kChassis,
         "Carries the car's weight as high as the real vehicle it depicts, from that vehicle's stability "
         "factor. Off leaves it where the game put it."},
        {"Solid axles", &Switches::solidAxles, "Solid axles", kSuspension,
         "Springs each solid axle of the real vehicle as one beam, which twists under the body to keep both "
         "wheels down and lets them hang lower. Only the Land Rover so far. Off springs every wheel on its "
         "own, as the game does."},
    };

    Switches g_switches{};
    // A set for each vehicle in RealVehicle::All(), in its order, then one for the cars it leaves out.
    std::vector<Values> g_cars(VehicleOverhaul::RealVehicle::All().size() + 1);
    // The set the sliders edit.
    size_t g_edited = 0;

    size_t IndexOf(const VehicleOverhaul::RealVehicle::Spec* real) {
        return real != nullptr ? static_cast<size_t>(real - VehicleOverhaul::RealVehicle::All().data())
                               : g_cars.size() - 1;
    }

    // Also its section in the file.
    const char* NameOf(size_t car) {
        return car + 1 < g_cars.size() ? VehicleOverhaul::RealVehicle::All()[car].name : "Other cars";
    }

    // The switches drawn under `group`. True when one changed.
    bool DrawSwitches(const char* group) {
        bool changed = false;
        for (const Switch& entry : kSwitches) {
            if (entry.group == group) {
                changed |= ImGui::Checkbox(entry.label, &(g_switches.*entry.member));
                ImGui::SetItemTooltip("%s", entry.help);
            }
        }
        return changed;
    }

    // Moved sliders are written to the file once they are let go rather than on every frame.
    bool g_unsaved = false;

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
            text.append(entry.key).append(g_switches.*entry.member ? " = 1\n" : " = 0\n");
        }
        for (size_t car = 0; car < g_cars.size(); ++car) {
            text.append("\n[").append(NameOf(car)).append("]\n");
            for (const Parameter& parameter : kParameters) {
                char value[32];
                const char* end = std::to_chars(value, value + sizeof(value), g_cars[car].*parameter.member).ptr;
                text.append(parameter.key).append(" = ").append(value, end - value).append("\n");
            }
        }

        std::ofstream file(File(), std::ios::trunc);
        file << text;
        file.close();
        if (!file) {
            FCSE::Logf("tuning: %s could not be written", Path());
        }
    }
}

VehicleOverhaul::Tuning::Switches VehicleOverhaul::Tuning::CurrentSwitches() { return g_switches; }

VehicleOverhaul::Tuning::Values VehicleOverhaul::Tuning::Current(const RealVehicle::Spec* real) {
    return g_cars[IndexOf(real)];
}

void VehicleOverhaul::Tuning::Load() {
    for (const Switch& entry : kSwitches) {
        g_switches.*entry.member = true;
    }
    Values defaults{};
    for (const Parameter& parameter : kParameters) {
        defaults.*parameter.member = parameter.defaultValue;
    }
    std::fill(g_cars.begin(), g_cars.end(), defaults);
    for (size_t car = 0; car + 1 < g_cars.size(); ++car) {
        const RealVehicle::Spec& real = RealVehicle::All()[car];
        if (real.topSpeed > 0.0f) {
            g_cars[car].topSpeed = real.topSpeed;
        }
        if (real.springs > 0.0f) {
            g_cars[car].springs = real.springs;
        }
    }

    std::ifstream file(File());

    // A value outside a known car's section, or that is not a number, leaves its row at the default.
    Values* section = nullptr;
    std::string line;
    while (std::getline(file, line)) {
        const std::string_view text = Trim(line);
        if (text.starts_with('[') && text.ends_with(']')) {
            const std::string_view name = text.substr(1, text.size() - 2);
            section = nullptr;
            for (size_t car = 0; car < g_cars.size() && section == nullptr; ++car) {
                if (name == NameOf(car)) {
                    section = &g_cars[car];
                }
            }
            continue;
        }
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
                g_switches.*entry.member = read != 0.0f;
            }
        }
        for (const Parameter& parameter : kParameters) {
            if (section != nullptr && key == parameter.key) {
                (*section).*parameter.member = std::clamp(read, parameter.min, parameter.max);
            }
        }
    }

    Save();
}

namespace {
    void DrawDriving() {
        ImGui::PushItemWidth(-ImGui::GetFontSize() * 10.0f);

        VehicleOverhaul::Physics::Status status{};
        const bool driving = VehicleOverhaul::Physics::Latest(status);
        if (driving) {
            ImGui::Text("%.0f km/h   %.0f rpm   gear %d", status.motion.speed * 3.6f, status.motion.rpm,
                        status.motion.gear + 1);
            ImGui::Text("Centre of mass %.2f m up, tips over at %.2f g; the tyres hold %.2f g here", status.height,
                        status.stability, status.grip);
        } else {
            ImGui::TextDisabled("Not driving.");
        }
        g_unsaved |= DrawSwitches(nullptr);

        if (driving) {
            g_edited = IndexOf(status.real);
        }
        ImGui::BeginDisabled(driving);
        if (ImGui::BeginCombo("Car", NameOf(g_edited))) {
            for (size_t car = 0; car < g_cars.size(); ++car) {
                if (ImGui::Selectable(NameOf(car), car == g_edited)) {
                    g_edited = car;
                }
            }
            ImGui::EndCombo();
        }
        ImGui::EndDisabled();
        ImGui::SetItemTooltip("The car the values below are for: the one you drive, or any while you are not "
                              "driving.");

        Values& values = g_cars[g_edited];
        const char* group = nullptr;
        for (const Parameter& parameter : kParameters) {
            if (parameter.group != group) {
                group = parameter.group;
                ImGui::SeparatorText(group);
                g_unsaved |= DrawSwitches(group);
            }
            g_unsaved |= ImGui::SliderFloat(parameter.key, &(values.*parameter.member), parameter.min,
                                            parameter.max, parameter.format, ImGuiSliderFlags_AlwaysClamp);
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
