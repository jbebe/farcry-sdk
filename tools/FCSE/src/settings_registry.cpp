#include "settings_registry.h"

#include "caller_identity.h"
#include "ini_file.h"
#include "log.h"

#include <intrin.h>

namespace FCSE {

namespace {
    IniFile g_ini;
    std::wstring g_configPath;
    std::vector<SettingsRegistry::Group> g_groups;
    bool g_dirty = false;

    // A group name becomes an INI [header] verbatim, so it must not contain the brackets that
    // delimit one, nor a line break that would split it across lines.
    bool IsValidGroupName(const std::string& name) {
        return !name.empty() && name.find_first_of("[]\r\n") == std::string::npos &&
               name.find_first_not_of(" \t") != std::string::npos;
    }

    // A setting name becomes an INI key verbatim, so on top of the group rules it must not contain
    // the '=' that separates key from value. It also has to survive a write/read round trip
    // unchanged: the writer emits `key = value` and the reader trims both sides, so a name with
    // leading or trailing whitespace would come back as a different name and silently orphan its
    // stored value on the next launch.
    bool IsValidSettingName(const std::string& name) {
        if (name.empty() || name.find_first_of("=[]\r\n") != std::string::npos) {
            return false;
        }
        return name.front() != ' ' && name.front() != '\t' && name.back() != ' ' &&
               name.back() != '\t';
    }

    bool IsKnownType(FCSE_SettingType type) { return type == FCSE_SettingType_Checkbox; }

    std::string FormatValue(const FCSE_SettingValue& value) {
        switch (value.type) {
        case FCSE_SettingType_Checkbox:
            return value.asCheckbox ? "true" : "false";
        }
        return std::string();
    }

    std::string ToLowerAscii(const std::string& text) {
        std::string lowered = text;
        for (char& c : lowered) {
            if (c >= 'A' && c <= 'Z') {
                c = static_cast<char>(c - 'A' + 'a');
            }
        }
        return lowered;
    }

    // Accepts the spellings a player might reasonably hand-write. Anything else is a parse failure,
    // which the caller reports and recovers from by falling back to the plugin's default - never by
    // guessing.
    bool ParseValue(FCSE_SettingType type, const std::string& text, FCSE_SettingValue* out) {
        switch (type) {
        case FCSE_SettingType_Checkbox: {
            std::string lowered = ToLowerAscii(text);
            bool isTrue = lowered == "true" || lowered == "1" || lowered == "yes" || lowered == "on";
            bool isFalse =
                lowered == "false" || lowered == "0" || lowered == "no" || lowered == "off";
            if (!isTrue && !isFalse) {
                return false;
            }
            out->type = FCSE_SettingType_Checkbox;
            out->asCheckbox = isTrue;
            return true;
        }
        }
        return false;
    }

    SettingsRegistry::Group& EnsureGroup(const std::string& pluginName) {
        for (SettingsRegistry::Group& group : g_groups) {
            if (group.pluginName == pluginName) {
                return group;
            }
        }
        SettingsRegistry::Group group;
        group.pluginName = pluginName;
        g_groups.push_back(std::move(group));
        return g_groups.back();
    }

    bool GroupHasSetting(const SettingsRegistry::Group& group, const std::string& name) {
        for (const std::unique_ptr<SettingsRegistry::Setting>& setting : group.settings) {
            if (setting->name == name) {
                return true;
            }
        }
        return false;
    }

    void Notify(const SettingsRegistry::Setting& setting) {
        if (setting.onChanged != nullptr) {
            setting.onChanged(&setting.value, setting.userdata);
        }
    }
}

void SettingsRegistry::Init(const std::wstring& configPath) {
    g_configPath = configPath;

    if (!g_ini.Load(configPath)) {
        Log::Loader("settings: fcse.ini exists but could not be read - this run starts from "
                    "defaults, and saving would overwrite it, so persistence is disabled");
        g_configPath.clear();
        return;
    }

    if (g_ini.IsEmpty()) {
        g_ini.AddPreambleComment("FCSE settings. One group per plugin, created when that plugin");
        g_ini.AddPreambleComment("registers settings. Editing a value here takes effect on the");
        g_ini.AddPreambleComment("next launch; the in-game Mod Configuration Menu writes back to");
        g_ini.AddPreambleComment("this same file. Groups for plugins you no longer have installed");
        g_ini.AddPreambleComment("are kept, not deleted.");
        g_dirty = true;
    }

    Log::Loader("settings: fcse.ini loaded");
}

bool SettingsRegistry::RegisterSettings(const char* pluginName, const FCSE_Setting* settings,
                                         size_t settingCount) {
    void* caller = _ReturnAddress();

    if (pluginName == nullptr || settings == nullptr || settingCount == 0) {
        Log::FromCaller(caller, "RegisterSettings() called with a null/empty argument, rejected");
        return false;
    }

    std::string groupName = pluginName;
    if (!IsValidGroupName(groupName)) {
        Log::FromCaller(caller, "RegisterSettings(\"" + groupName +
                                     "\") rejected - a plugin name cannot be empty or contain "
                                     "'[', ']' or a line break");
        return false;
    }

    Group& group = EnsureGroup(groupName);
    size_t accepted = 0;

    for (size_t i = 0; i < settingCount; ++i) {
        const FCSE_Setting& declared = settings[i];
        std::string name = declared.name != nullptr ? declared.name : std::string();

        if (!IsValidSettingName(name)) {
            Log::FromCaller(caller, "RegisterSettings(\"" + groupName + "\") skipped setting #" +
                                         std::to_string(i) +
                                         " - a setting name cannot be empty, contain '=', '[', "
                                         "']' or a line break, or start/end with whitespace");
            continue;
        }
        if (!IsKnownType(declared.defaultValue.type)) {
            Log::FromCaller(caller, "RegisterSettings(\"" + groupName + "\") skipped \"" + name +
                                         "\" - unknown setting type " +
                                         std::to_string(static_cast<int>(declared.defaultValue.type)) +
                                         ", this FCSE build only knows Checkbox");
            continue;
        }
        if (GroupHasSetting(group, name)) {
            Log::FromCaller(caller, "RegisterSettings(\"" + groupName + "\") skipped \"" + name +
                                         "\" - already registered under this plugin name");
            continue;
        }

        auto setting = std::make_unique<Setting>();
        setting->groupName = groupName;
        setting->name = name;
        setting->value = declared.defaultValue;
        setting->onChanged = declared.onChanged;
        setting->userdata = declared.userdata;

        // The file is the source of truth where it has an answer; the plugin's default is the
        // fallback, and gets written back so the player can see the setting exists.
        const std::string* stored = g_ini.Find(groupName, name);
        bool wroteDefault = true;
        if (stored != nullptr) {
            FCSE_SettingValue parsed{};
            if (ParseValue(declared.defaultValue.type, *stored, &parsed)) {
                setting->value = parsed;
                wroteDefault = false;
            } else {
                Log::FromCaller(caller, "settings: [" + groupName + "] " + name + " = \"" + *stored +
                                             "\" is not a value this setting's type understands - "
                                             "falling back to the plugin's default");
            }
        }
        if (wroteDefault) {
            g_ini.Set(groupName, name, FormatValue(setting->value));
            g_dirty = true;
        }

        Setting* entry = setting.get();
        group.settings.push_back(std::move(setting));
        ++accepted;

        // Immediately, and synchronously - a plugin registering during FCSE_Load has its settings
        // applied before any Dunia.dll engine code runs.
        Notify(*entry);
    }

    Log::FromCaller(caller, "RegisterSettings(\"" + groupName + "\") registered " +
                                 std::to_string(accepted) + " of " + std::to_string(settingCount) +
                                 " setting(s)");
    return accepted > 0;
}

const std::vector<SettingsRegistry::Group>& SettingsRegistry::Groups() { return g_groups; }

const SettingsRegistry::Group* SettingsRegistry::FindGroup(const std::string& pluginName) {
    for (const Group& group : g_groups) {
        if (group.pluginName == pluginName) {
            return &group;
        }
    }
    return nullptr;
}

void SettingsRegistry::ToggleCheckbox(Setting* setting) {
    if (setting == nullptr) {
        return;
    }
    if (setting->value.type != FCSE_SettingType_Checkbox) {
        Log::Loader("settings: ignoring a toggle on [" + setting->groupName + "] " + setting->name +
                    " - not a Checkbox");
        return;
    }

    setting->value.asCheckbox = !setting->value.asCheckbox;
    g_ini.Set(setting->groupName, setting->name, FormatValue(setting->value));
    g_dirty = true;

    Log::Loader("settings: [" + setting->groupName + "] " + setting->name + " toggled to " +
                (setting->value.asCheckbox ? "true" : "false"));

    Notify(*setting);
    Flush();
}

void SettingsRegistry::Flush() {
    if (!g_dirty) {
        return;
    }
    if (g_configPath.empty()) {
        return; // Init disabled persistence after a failed read - don't clobber the file
    }

    if (g_ini.Save(g_configPath)) {
        g_dirty = false;
        Log::Loader("settings: fcse.ini written");
    } else {
        Log::Loader("settings: failed to write fcse.ini - changes are live this session but will "
                    "not persist");
    }
}

} // namespace FCSE
