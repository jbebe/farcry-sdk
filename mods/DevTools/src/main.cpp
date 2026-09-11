// DevTools - the developer side of Far Cry 2, made reachable.
//
// A plugin for people working on the game rather than playing it: the parts of the engine Ubisoft
// left in the shipped build but closed off, opened again. Like UFCP it patches the running game
// rather than the files on disk, so nothing is overwritten and nothing survives uninstalling it.
//
// One fix per file in src/fixes/, applied unconditionally, and one option per file in src/options/,
// each a get/set pair listed in src/options/options.cpp. Adding either: write the file, then declare
// and wire it below.
//
// That one table is also what gets registered with FCSE below, so an option is described once and
// both the Mod Configuration Menu and the overlay read the same description. FCSE owns the stored
// value and writes it to bin\fcse.ini, which is what makes a setting survive a relaunch.
//
// src/engine/, src/commands/ and src/overlay/ are the game's own seams, the catalog of what to ask
// it for, and the UI that does the asking.
#include "fcse_api.h"

#include "engine/camera.h"
#include "engine/console.h"
#include "engine/debug_camera.h"
#include "engine/entity.h"
#include "engine/game_profile.h"
#include "engine/game_thread.h"
#include "engine/keys.h"
#include "engine/pawn_tick.h"
#include "engine/player.h"
#include "engine/weather.h"
#include "options/options.h"
#include "overlay/overlay.h"

#include <vector>

// -load <save>.sav crashes to desktop instead of launching into the save.
void ApplyLoadSavegameFix();

// Options that hook rather than patch. Each finds its sites at load; the overlay only flips them.
void InstallInvincibilityHooks();
void InstallInfiniteAmmoHooks();
void InstallDiamondsHooks();
void InstallSkipSystemDetectionHook();

namespace {
    // One callback for every option: which one it is arrives as the userdata FCSE hands back.
    void __cdecl OnOptionChanged(const FCSE_SettingValue* value, void* userdata) {
        const auto* option = static_cast<const DevTools::Options::Option*>(userdata);

        // Each member is read through the type it was registered as; the union's numeric members
        // overlap, but a Checkbox stores a bool and the bytes above it are not its own.
        switch (option->kind) {
        case DevTools::Options::Kind::Toggle:
            option->set(value->asCheckbox ? 1 : 0);
            break;
        case DevTools::Options::Kind::Slider:
            option->set(value->asSlider);
            break;
        case DevTools::Options::Kind::Key:
            option->set(static_cast<int>(value->asChoice));
            break;
        }
    }

    // Builds the FCSE rows from the same table the overlay draws, so the two can never describe an
    // option differently. Each callback fires from inside RegisterSettings carrying whatever
    // fcse.ini holds, so every option is in its stored state by the time this returns.
    void RegisterOptions(const FCSE_PluginAPI* api) {
        size_t keyCount = 0;
        const char* const* keyLabels = DevTools::Keys::ChoiceLabels(keyCount);

        std::vector<FCSE_Setting> rows;
        for (const DevTools::Options::Option& option : DevTools::Options::All()) {
            FCSE_Setting row{};
            row.name = option.name;
            row.onChanged = &OnOptionChanged;
            row.userdata = const_cast<DevTools::Options::Option*>(&option);

            switch (option.kind) {
            case DevTools::Options::Kind::Toggle:
                row.defaultValue = {FCSE_SettingType_Checkbox, {option.defaultValue}};
                break;
            case DevTools::Options::Kind::Slider:
                row.defaultValue = {FCSE_SettingType_Slider, {option.defaultValue}};
                row.minValue = option.minValue;
                row.maxValue = option.maxValue;
                break;
            case DevTools::Options::Kind::Key:
                row.defaultValue = {FCSE_SettingType_Choice, {option.defaultValue}};
                row.choices = keyLabels;
                row.choiceCount = static_cast<uint32_t>(keyCount);
                break;
            }

            rows.push_back(row);
        }

        api->RegisterSettings("DevTools", rows.data(), rows.size());
    }
}

extern "C" __declspec(dllexport) bool FCSE_Load(const FCSE_PluginAPI* api) {
    if (api->apiVersion != FCSE_API_VERSION) {
        return false; // FCSE logs the refusal
    }

    // Wires up the address library and the pattern scanner behind FCSE::Relocation, which is how
    // everything here finds the code it patches - so a failure leaves nothing that could work.
    if (!FCSE::Bind(api)) {
        return false;
    }

    api->Log("DevTools loaded");

    // FCSE_Load runs before any Dunia.dll engine code, so the fix is in place before the game that
    // would read it starts.
    ApplyLoadSavegameFix();

    // The frame the work runs on, the console it runs through, and the overlay that drives both.
    // Each needs the one before it, so none of them is installed alone. All three are independent of
    // the Developer console option below, because every line the console sends raises the developer
    // flag for itself.
    if (DevTools::GameThread::Install() && DevTools::Console::Install() &&
        DevTools::Overlay::Install()) {
        // Only the overlay forces weather, so there is nothing to hook without it.
        DevTools::Weather::Install();
    }

    // The player, the flags hung off their profile, and the frame all of it runs on. Subscribers are
    // called in the order they register here, so the profile's own frame goes first.
    DevTools::Player::Install();
    DevTools::GameProfile::Install();
    InstallInvincibilityHooks();
    InstallInfiniteAmmoHooks();
    InstallDiamondsHooks();

    // What a camera mode is made of. The two options below only say which key reaches one.
    DevTools::Entity::Install();
    DevTools::Camera::Install();
    DevTools::DebugCamera::Install();

    DevTools::PawnTick::Install();

    InstallSkipSystemDetectionHook();
    RegisterOptions(api);

    return true;
}
