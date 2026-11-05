// Aiming Overhaul's window and the file its values are kept in. See tuning.h.
#include "tuning.h"

#include "fcse_api.h"
#include "shot_shake.h"

#include "imgui.h"

#include <windows.h>

#include <algorithm>
#include <charconv>
#include <filesystem>
#include <fstream>
#include <string>
#include <string_view>

namespace {
    using AimingOverhaul::Tuning::Slider;

    // Whether a slider has moved since the file was last written.
    bool g_unsaved = false;

    // Beside fcse.ini, in the folder the game was started from.
    const std::filesystem::path& File() {
        static const std::filesystem::path file = [] {
            std::wstring exe(32768, L'\0');
            exe.resize(GetModuleFileNameW(nullptr, exe.data(), static_cast<DWORD>(exe.size())));
            return std::filesystem::path(exe).parent_path() / L"aiming-overhaul.ini";
        }();
        return file;
    }

    std::string_view Trim(std::string_view text) {
        const size_t first = text.find_first_not_of(" \t\r");
        return first == std::string_view::npos
                   ? std::string_view{}
                   : text.substr(first, text.find_last_not_of(" \t\r") - first + 1);
    }

    void Save() {
        std::ofstream file(File(), std::ios::trunc);
        file << "; Aiming Overhaul, set in its window in DevTools' overlay and read at launch.\n";
        for (const Slider& slider : AimingOverhaul::ShotShake::Sliders()) {
            char value[32];
            const char* end = std::to_chars(value, value + sizeof(value), slider.value->load()).ptr;
            file << slider.key << " = " << std::string_view(value, end - value) << "\n";
        }
        file.close();
        if (!file) {
            FCSE::ApiPointer()->Log("tuning: aiming-overhaul.ini could not be written");
        }
    }
}

void AimingOverhaul::Tuning::Load() {
    const std::span<const Slider> sliders = ShotShake::Sliders();
    // A line that is not a known key and a number leaves its slider at the default.
    std::ifstream file(File());
    std::string line;
    while (std::getline(file, line)) {
        const std::string_view text = Trim(line);
        const size_t equals = text.find('=');
        if (equals == std::string_view::npos) {
            continue;
        }
        const std::string_view key = Trim(text.substr(0, equals));
        const std::string_view number = Trim(text.substr(equals + 1));
        float read = 0.0f;
        const auto slider = std::find_if(sliders.begin(), sliders.end(),
                                         [&](const Slider& s) { return key == s.key; });
        if (slider != sliders.end() &&
            std::from_chars(number.data(), number.data() + number.size(), read).ec == std::errc{}) {
            slider->value->store(std::clamp(read, slider->min, slider->max));
        }
    }
}

void AimingOverhaul::Tuning::DrawWindow(void*) {
    for (const Slider& slider : ShotShake::Sliders()) {
        float value = slider.value->load();
        if (ImGui::SliderFloat(slider.label, &value, slider.min, slider.max, slider.format,
                               ImGuiSliderFlags_AlwaysClamp)) {
            slider.value->store(value);
            g_unsaved = true;
        }
    }
    ShotShake::DrawLastShot();
    if (g_unsaved && !ImGui::IsAnyItemActive()) {
        Save();
        g_unsaved = false;
    }
}
