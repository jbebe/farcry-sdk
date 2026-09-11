#include "commands/catalog.h"

#include "engine/console.h"
#include "fcse_api.h"

#include <cstdio>
#include <cstring>

namespace {
    using DevTools::Commands::Arg;
    using DevTools::Commands::Choice;
    using DevTools::Commands::Command;

    constexpr Choice kOffOn[] = {{"Off", "0"}, {"On", "1"}};
    constexpr Choice kDrawMethods[] = {{"Solid", "0"}, {"Wireframe", "1"}};
    constexpr Choice kFovPresets[] = {
        {"Default", "-1"}, {"Narrow", "1"}, {"Wide", "2"}, {"Very wide", "3"}};
    // Weakest first, each labelled with its fWindForce, and blended to instantly.
    constexpr Choice kWindPresets[] = {
        {"HoD (10)", "\"Default.HoD.Wind\", 0"},
        {"Jungle (10)", "\"Default.Jungle.Wind\", 0"},
        {"Clear (60)", "\"Default.Clear.Wind\", 0"},
        {"Desert (100)", "\"Default.Desert.Wind\", 0"},
        {"Storm (120)", "\"Default.Storm.Wind\", 0"},
        {"SandStorm (150)", "\"Default.SandStorm.Wind\", 0"}};

// A config setting with nothing to say about it beyond its name and where it belongs.
#define DEVTOOLS_SETTING(settingName, settingCategory)                                             \
    {settingName, settingName, settingCategory, Arg::String, {}, nullptr, nullptr}

    constexpr Command kCommands[] = {
        {"set_health", "Set health", "Player", Arg::Float, {}, nullptr,
         "Every value tested kills - the handler sends a stim rather than setting health."},
        {"hit_me", "Damage the player", "Player", Arg::String, {}, nullptr,
         "An amount, optionally followed by crush, burn or cut."},
        {"set_weapon_reliability", "Weapon reliability", "Player", Arg::Float, {}, nullptr,
         "0 to 1."},
        {"set_no_weapon_mode", "No weapon mode", "Player", Arg::Enum, kOffOn, nullptr, nullptr},
        {"SetWeaponDifficultyLevel", "Weapon difficulty", "Player", Arg::Int, {}, nullptr, nullptr},
        {"dbg_start_malaria", "Start malaria", "Player", Arg::None, {}, nullptr, nullptr},
        {"dbg_force_malaria", "Force a malaria attack", "Player", Arg::String, {}, nullptr,
         "Optionally a major attack, and whether to take the pills away."},
        {"debug_set_player_sickness", "Sickness level", "Player", Arg::Int, {}, nullptr,
         "A level, and optionally a terminal number."},
        {"InfSetBase", "Base infamy", "Player", Arg::Int, {}, nullptr, "0 to 175."},
        {"teleport_to_current_objective", "Teleport to objective", "Player", Arg::None, {}, nullptr,
         nullptr},
        {"debug_phonecall", "Receive a phone call", "Player", Arg::None, {}, nullptr, nullptr},
        {"debug_machetetest", "Give the machete", "Player", Arg::None, {}, nullptr, nullptr},
        {"get_local_player_id", "Local player id", "Player", Arg::None, {}, nullptr, nullptr},

        {"Cheat_AddDiamonds", "Add diamonds", "Cheats", Arg::Int, {}, nullptr, nullptr},
        {"cheat_add_playerweapon", "Give a weapon", "Cheats", Arg::String, {}, nullptr,
         "A weapon name."},
        {"cheat_GodMode", "God mode", "Cheats", Arg::Enum, kOffOn, nullptr, nullptr},
        {"cheat_UnlimitedAmmo", "Unlimited ammo", "Cheats", Arg::Enum, kOffOn, nullptr, nullptr},
        {"cheat_UnlimitedReliability", "Unlimited reliability", "Cheats", Arg::Enum, kOffOn, nullptr,
         nullptr},
        {"cheat_AllWeaponsUnlock", "Unlock every weapon", "Cheats", Arg::Enum, kOffOn, nullptr,
         "Only the weapons the current map offers."},

        {"ai_IgnorePlayer", "Ignore the player", "AI", Arg::Enum, kOffOn, nullptr, nullptr},

        {"set_current_primary_buddy", "Primary buddy", "Buddies", Arg::String, {}, nullptr,
         "A buddy name, such as Marty_Alencar."},
        {"set_current_secondary_buddy", "Secondary buddy", "Buddies", Arg::String, {}, nullptr,
         nullptr},
        {"get_current_primary_buddy", "Show primary buddy", "Buddies", Arg::None, {}, nullptr,
         nullptr},
        {"get_current_secondary_buddy", "Show secondary buddy", "Buddies", Arg::None, {}, nullptr,
         nullptr},
        {"set_betray_buddies", "Betrayed buddies", "Buddies", Arg::Int, {}, nullptr, nullptr},
        {"select_main_avatar", "Main avatar", "Buddies", Arg::String, {}, nullptr, "A buddy name."},
        {"get_buddies_manager_status", "Buddy manager status", "Buddies", Arg::None, {}, nullptr,
         nullptr},

        {"set_winning_faction", "Winning faction", "Missions", Arg::String, {}, nullptr,
         "A faction name and an act."},
        {"activate_challenge", "Activate a challenge", "Missions", Arg::String, {}, nullptr,
         nullptr},
        {"complete_challenge", "Complete a challenge", "Missions", Arg::String, {}, nullptr,
         nullptr},
        {"add_bonus_plan", "Add a bonus plan", "Missions", Arg::String, {}, nullptr, nullptr},
        {"add_all_bonus_plans", "Add every bonus plan", "Missions", Arg::None, {}, nullptr, nullptr},
        {"remove_bonus_plan", "Remove a bonus plan", "Missions", Arg::String, {}, nullptr, nullptr},
        {"log_active_bonus_plans", "Log active bonus plans", "Missions", Arg::None, {}, nullptr,
         nullptr},
        {"log_available_bonus_plans", "Log available bonus plans", "Missions", Arg::None, {},
         nullptr, nullptr},
        {"get_mission_manager_status", "Mission manager status", "Missions", Arg::None, {}, nullptr,
         nullptr},
        {"force_beautifier", "Force a beautifier", "Missions", Arg::String, {}, nullptr, nullptr},
        {"EndOfGame", "End of game", "Missions", Arg::None, {}, nullptr, nullptr},
        {"InGameCredits", "In-game credits", "Missions", Arg::None, {}, nullptr, nullptr},
        {"PopUpObjective", "Pop up an objective", "Missions", Arg::String, {},
         "#PopUpObjective(%s)",
         "An objective id and a message name. The plain command drops what it is given."},

        {"env_Hour", "Hour", "Environment", Arg::Int, {}, nullptr, nullptr},
        {"env_Minutes", "Minutes", "Environment", Arg::Int, {}, nullptr, nullptr},
        {"env_Seconds", "Seconds", "Environment", Arg::Int, {}, nullptr, nullptr},
        {"env_TimeScale", "Time scale", "Environment", Arg::Int, {}, nullptr, nullptr},
        {"env_StormHour", "Storm hour", "Environment", Arg::Int, {}, nullptr, nullptr},
        {"env_WindForce", "Wind force", "Environment", Arg::Int, {}, nullptr,
         "Overwritten every frame by the world's wind - force the wind on the Environment tab, or "
         "send a wind override."},
        {"env_WindDir", "Wind direction", "Environment", Arg::Int, {}, nullptr,
         "Only where the world's drift starts from, and ignored while the Environment tab forces "
         "the wind."},
        {"env_DelayShadowMovement", "Shadow movement delay", "Environment", Arg::Int, {}, nullptr,
         nullptr},
        {"SetScriptedTimeOfDay", "Time of day", "Environment", Arg::String, {},
         "#CDynamicEnvironmentManager_GetInstance():SetScriptedTimeOfDay(%s)",
         "An hour and minutes."},
        {"SetStormFactor", "Storm factor", "Environment", Arg::String, {},
         "#CDynamicEnvironmentManager_GetInstance():SetScriptedStormFactorOverride(%s)",
         "A storm factor and how long to blend into it, in seconds. Ignored while the Environment "
         "tab forces the storm."},
        {"RemoveStormFactor", "Clear the storm factor", "Environment", Arg::String, {},
         "#CDynamicEnvironmentManager_GetInstance():RemoveScriptedStormFactorOverride(%s)",
         "How long to blend back, in seconds."},
        {"SetWindOverride", "Wind override", "Environment", Arg::Enum, kWindPresets,
         "#CDynamicEnvironmentManager_GetInstance():SetWindOverride(%s)",
         "Ignored while the Environment tab forces the wind."},
        {"RemoveWindOverride", "Clear the wind override", "Environment", Arg::None, {},
         "#CDynamicEnvironmentManager_GetInstance():RemoveWindOverride(\"\", 0)",
         "Back to the world's own wind. The name it takes is ignored."},

        {"set_debug_fov", "Field of view", "Camera", Arg::Enum, kFovPresets, nullptr,
         "A preset rather than an angle."},
        {"SetFPCameraOffsetX", "Camera offset X", "Camera", Arg::Float, {}, nullptr, nullptr},
        {"SetFPCameraOffsetY", "Camera offset Y", "Camera", Arg::Float, {}, nullptr, nullptr},
        {"SetFPCameraOffsetZ", "Camera offset Z", "Camera", Arg::Float, {}, nullptr, nullptr},
        {"SetWeaponCameraOffsetX", "Weapon camera offset X", "Camera", Arg::Float, {}, nullptr,
         nullptr},
        {"SetWeaponCameraOffsetY", "Weapon camera offset Y", "Camera", Arg::Float, {}, nullptr,
         nullptr},
        {"SetWeaponCameraOffsetZ", "Weapon camera offset Z", "Camera", Arg::Float, {}, nullptr,
         nullptr},

        {"RTGenesis", "Grow the trees", "Vegetation", Arg::Float, {}, nullptr,
         "Metres per second; zero or less turns it off."},
        {"RTRegen", "Regrow the trees", "Vegetation", Arg::Float, {}, nullptr,
         "Metres per second; zero or less turns it off."},
        {"RTDefoliant", "Strip the trees", "Vegetation", Arg::None, {}, nullptr, nullptr},
        {"RTSetWindForce", "Tree wind force", "Vegetation", Arg::Float, {}, nullptr,
         "Below zero hands the trees back to the environment's own wind."},

        {"draw_method", "Draw method", "Rendering", Arg::Enum, kDrawMethods, nullptr, nullptr},
        {"render_menu_only", "Render menus only", "Rendering", Arg::Enum, kOffOn, nullptr, nullptr},
        {"Magma_ToggleHUD", "HUD", "Rendering", Arg::Enum, kOffOn, nullptr, nullptr},
        {"rt_lod_freeze", "Freeze tree detail", "Rendering", Arg::Enum, kOffOn, nullptr, nullptr},
        {"showFps", "Frame rate", "Rendering", Arg::Enum, kOffOn, nullptr, nullptr},

        {"screenshot", "Screenshot", "Capture", Arg::None, {}, nullptr, nullptr},
        {"snapshot", "Snapshot", "Capture", Arg::None, {}, nullptr, nullptr},
        {"snapshot_viewport", "Snapshot of the viewport", "Capture", Arg::None, {}, nullptr,
         nullptr},
        {"anim_start_recording", "Record animation", "Capture", Arg::None, {}, nullptr, nullptr},

        {"Stats", "Timer groups", "Profiler", Arg::String, {}, nullptr,
         "How many levels to show, optionally forced on or off."},

        {"look_Sensitivity", "Look sensitivity", "Input", Arg::Float, {}, nullptr, nullptr},
        {"look_Sensitivity_x", "Look sensitivity X", "Input", Arg::Float, {}, nullptr, nullptr},
        {"look_Sensitivity_y", "Look sensitivity Y", "Input", Arg::Float, {}, nullptr, nullptr},
        {"look_Invert_x", "Invert X", "Input", Arg::Enum, kOffOn, nullptr, nullptr},
        {"look_Invert_y", "Invert Y", "Input", Arg::Enum, kOffOn, nullptr, nullptr},
        {"look_HelpCrosshair", "Crosshair help", "Input", Arg::Enum, kOffOn, nullptr, nullptr},

        {"snd_opstat", "Sound operation average", "Sound", Arg::String, {}, nullptr,
         "How many operations to average over."},
        {"snd_oppeak", "Sound peak hold", "Sound", Arg::String, {}, nullptr,
         "How long a peak stays up, in seconds."},

        {"clear", "Clear the console", "Console", Arg::None, {}, nullptr, nullptr},
        {"help", "Console help", "Console", Arg::None, {}, nullptr, nullptr},
        {"console_dump_elements", "Dump every command", "Console", Arg::None, {}, nullptr,
         "Writes ConsoleElementsDump.txt to the save folder."},
        {"exec", "Run a batch file", "Console", Arg::String, {}, nullptr,
         "A file from the console script folder. runbatch and rb do the same."},
        {"scriptcallbacks", "Script callbacks", "Console", Arg::None, {}, nullptr, nullptr},

        {"evict_resources", "Evict unused resources", "Game", Arg::None, {}, nullptr, nullptr},
        {"quitToMainMenu", "Quit to the main menu", "Game", Arg::None, {}, nullptr, nullptr},
        {"quit", "Quit the game", "Game", Arg::None, {}, nullptr, nullptr},
        {"ChangeWorld", "Change world", "Game", Arg::String, {},
         "#GameChangeWorldDefaultSpawnPoint(\"%s\")",
         "A world name. Untested, and standing in for load_level, which has no Lua behind it."},

#include "commands/settings.inc"
    };

#undef DEVTOOLS_SETTING
}

namespace DevTools::Commands {

std::span<const Command> All() { return kCommands; }

const Command* Find(const char* id) {
    for (const Command& command : kCommands) {
        if (std::strcmp(command.id, id) == 0) {
            return &command;
        }
    }
    return nullptr;
}

bool Format(const Command& command, const char* argument, char* out, size_t outSize) {
    const char* line = command.line != nullptr ? command.line : command.id;
    const char* text = argument != nullptr ? argument : "";
    const char* placeholder = std::strstr(line, "%s");

    const int written =
        placeholder != nullptr
            ? std::snprintf(out, outSize, "%.*s%s%s", static_cast<int>(placeholder - line), line,
                            text, placeholder + 2)
            : std::snprintf(out, outSize, "%s%s%s", line, text[0] != '\0' ? " " : "", text);

    return written >= 0 && static_cast<size_t>(written) < outSize;
}

bool Run(const Command& command, const char* argument) {
    char line[512];
    if (!Format(command, argument, line, sizeof(line))) {
        FCSE::ApiPointer()->Log("commands: the argument was too long to build a line from");
        return false;
    }

    Console::PostLine(line);
    return true;
}

}
