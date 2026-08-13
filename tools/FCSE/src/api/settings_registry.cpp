#include "api/settings_registry.h"

#include "caller_identity.h"
#include "ini_file.h"
#include "log.h"

#include <cstdlib>
#include <cstring>
#include <intrin.h>

namespace FCSE {

namespace {
    IniFile g_ini;
    std::wstring g_configPath;
    std::vector<SettingsRegistry::Group> g_groups;
    bool g_dirty = false;

    // FCSE's own group, for the diagnostic flags that are not registered settings.
    constexpr char kOwnGroup[] = "fcse";

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

    // FCSE's own ceiling on a Text setting, applied when a plugin does not name one. Sized to fit
    // comfortably in a menu row rather than to any engine limit.
    constexpr size_t kDefaultMaxTextLength = 64;

    bool IsKnownType(FCSE_SettingType type) {
        switch (type) {
        case FCSE_SettingType_Checkbox:
        case FCSE_SettingType_Choice:
        case FCSE_SettingType_Slider:
        case FCSE_SettingType_Text:
            return true;
        }
        return false;
    }

    // A Text value is delivered as a pointer into the setting's own storage, so it has to be
    // re-pointed whenever that string is assigned - std::string may have reallocated.
    void RefreshTextPointer(SettingsRegistry::Setting& setting) {
        if (setting.value.type == FCSE_SettingType_Text) {
            setting.value.asText = setting.text.c_str();
        }
    }

    // A Choice is written as its label, not its index: the point of the file is that a player can
    // read and edit it, and `Difficulty = Hardcore` says something that `Difficulty = 2` does not.
    // The index is the fallback for a label FCSE cannot render as a distinct token.
    std::string FormatValue(const SettingsRegistry::Setting& setting) {
        switch (setting.value.type) {
        case FCSE_SettingType_Checkbox:
            return setting.value.asCheckbox ? "true" : "false";
        case FCSE_SettingType_Choice:
            return setting.value.asChoice < setting.choices.size()
                       ? setting.choices[setting.value.asChoice]
                       : std::to_string(setting.value.asChoice);
        case FCSE_SettingType_Slider:
            return std::to_string(setting.value.asSlider);
        case FCSE_SettingType_Text:
            return setting.text;
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

    // Whole-string integer parse - "3x" and "" are failures, not 3 and 0.
    bool ParseInt(const std::string& text, long* out) {
        if (text.empty()) {
            return false;
        }
        char* end = nullptr;
        long parsed = std::strtol(text.c_str(), &end, 10);
        if (end == nullptr || *end != '\0') {
            return false;
        }
        *out = parsed;
        return true;
    }

    // Accepts the spellings a player might reasonably hand-write, and writes straight into the
    // setting so the per-type storage (a Choice's index, a Text's string) stays consistent with the
    // value. Anything else is a parse failure, which the caller reports and recovers from by falling
    // back to the plugin's default - never by guessing. `setting` is left untouched on failure.
    bool ParseInto(SettingsRegistry::Setting& setting, const std::string& text) {
        switch (setting.value.type) {
        case FCSE_SettingType_Checkbox: {
            std::string lowered = ToLowerAscii(text);
            bool isTrue = lowered == "true" || lowered == "1" || lowered == "yes" || lowered == "on";
            bool isFalse =
                lowered == "false" || lowered == "0" || lowered == "no" || lowered == "off";
            if (!isTrue && !isFalse) {
                return false;
            }
            setting.value.asCheckbox = isTrue;
            return true;
        }
        case FCSE_SettingType_Choice: {
            std::string lowered = ToLowerAscii(text);
            for (size_t i = 0; i < setting.choices.size(); ++i) {
                if (ToLowerAscii(setting.choices[i]) == lowered) {
                    setting.value.asChoice = static_cast<uint32_t>(i);
                    return true;
                }
            }
            long index = 0;
            if (!ParseInt(text, &index) || index < 0 ||
                static_cast<size_t>(index) >= setting.choices.size()) {
                return false;
            }
            setting.value.asChoice = static_cast<uint32_t>(index);
            return true;
        }
        case FCSE_SettingType_Slider: {
            long parsed = 0;
            if (!ParseInt(text, &parsed)) {
                return false;
            }
            // Clamped rather than rejected: a plugin narrowing its range in a later version would
            // otherwise throw away a value the player deliberately chose.
            if (parsed < setting.minValue) {
                parsed = setting.minValue;
            }
            if (parsed > setting.maxValue) {
                parsed = setting.maxValue;
            }
            setting.value.asSlider = static_cast<int32_t>(parsed);
            return true;
        }
        case FCSE_SettingType_Text: {
            if (text.size() > setting.maxTextLength) {
                return false; // reported as unusable, same as any other value that does not fit
            }
            setting.text = text;
            RefreshTextPointer(setting);
            return true;
        }
        }
        return false;
    }

    // Structural problems with a declaration - the ones that would produce a row the player cannot
    // use. Returns an empty string when the declaration is fine. A default value that is merely out
    // of range is not here: that is clamped on the way in rather than costing the plugin its row.
    std::string DeclarationProblem(const FCSE_Setting& declared) {
        switch (declared.defaultValue.type) {
        case FCSE_SettingType_Choice:
            if (declared.choices == nullptr || declared.choiceCount < 2) {
                return "a Choice needs at least two labels in `choices`";
            }
            for (uint32_t i = 0; i < declared.choiceCount; ++i) {
                if (declared.choices[i] == nullptr || declared.choices[i][0] == '\0') {
                    return "a Choice label is null or empty";
                }
            }
            return std::string();
        case FCSE_SettingType_Slider:
            if (declared.minValue >= declared.maxValue) {
                return "a Slider needs minValue < maxValue";
            }
            return std::string();
        default:
            return std::string();
        }
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
                                         "; this FCSE build knows Checkbox, Choice, Slider and Text");
            continue;
        }
        std::string problem = DeclarationProblem(declared);
        if (!problem.empty()) {
            Log::FromCaller(caller, "RegisterSettings(\"" + groupName + "\") skipped \"" + name +
                                         "\" - " + problem);
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
        setting->minValue = declared.minValue;
        setting->maxValue = declared.maxValue;
        setting->maxTextLength =
            declared.maxTextLength != 0 ? declared.maxTextLength : kDefaultMaxTextLength;
        for (uint32_t choice = 0; choice < declared.choiceCount; ++choice) {
            setting->choices.push_back(declared.choices[choice]);
        }
        if (declared.defaultValue.type == FCSE_SettingType_Text) {
            setting->text = declared.defaultText != nullptr ? declared.defaultText : std::string();
            if (setting->text.size() > setting->maxTextLength) {
                setting->text.resize(setting->maxTextLength);
            }
            RefreshTextPointer(*setting);
        }

        // A default the plugin got wrong is clamped rather than fatal - the row is still usable, and
        // the corrected value is written back where the author will see it.
        if (declared.defaultValue.type == FCSE_SettingType_Choice &&
            setting->value.asChoice >= setting->choices.size()) {
            Log::FromCaller(caller, "RegisterSettings(\"" + groupName + "\") \"" + name +
                                         "\" has a default choice index past the end of its label "
                                         "list - using the first label");
            setting->value.asChoice = 0;
        }
        if (declared.defaultValue.type == FCSE_SettingType_Slider &&
            (setting->value.asSlider < setting->minValue ||
             setting->value.asSlider > setting->maxValue)) {
            Log::FromCaller(caller, "RegisterSettings(\"" + groupName + "\") \"" + name +
                                         "\" has a default outside its own slider range - clamping");
            setting->value.asSlider = setting->value.asSlider < setting->minValue
                                          ? setting->minValue
                                          : setting->maxValue;
        }

        // The file is the source of truth where it has an answer; the plugin's default is the
        // fallback, and gets written back so the player can see the setting exists.
        const std::string* stored = g_ini.Find(groupName, name);
        bool wroteDefault = true;
        if (stored != nullptr) {
            if (ParseInto(*setting, *stored)) {
                wroteDefault = false;
            } else {
                Log::FromCaller(caller, "settings: [" + groupName + "] " + name + " = \"" + *stored +
                                             "\" is not a value this setting's type understands - "
                                             "falling back to the plugin's default");
            }
        }
        if (wroteDefault) {
            g_ini.Set(groupName, name, FormatValue(*setting));
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

bool SettingsRegistry::SetValue(Setting* setting, const FCSE_SettingValue& next) {
    if (setting == nullptr) {
        return false;
    }
    if (next.type != setting->value.type) {
        Log::Loader("settings: ignoring a value of type " +
                    std::to_string(static_cast<int>(next.type)) + " for [" + setting->groupName +
                    "] " + setting->name + ", which is type " +
                    std::to_string(static_cast<int>(setting->value.type)));
        return false;
    }

    // Each branch validates, then returns early if the value is the one already stored. The
    // settings page reads every control back on every frame it is open, so the unchanged case is
    // the one that runs constantly and it does no work and allocates nothing.
    switch (next.type) {
    case FCSE_SettingType_Checkbox:
        if ((setting->value.asCheckbox != 0) == (next.asCheckbox != 0)) {
            return false;
        }
        // Through asNumber, not asCheckbox: a bool write only touches the low byte of the union, so
        // assigning the named member would leave whatever the other three bytes previously held.
        setting->value.asNumber = next.asCheckbox ? 1 : 0;
        break;
    case FCSE_SettingType_Choice:
        if (next.asChoice >= setting->choices.size()) {
            Log::Loader("settings: ignoring choice index " + std::to_string(next.asChoice) +
                        " for [" + setting->groupName + "] " + setting->name + " - it has only " +
                        std::to_string(setting->choices.size()) + " option(s)");
            return false;
        }
        if (setting->value.asChoice == next.asChoice) {
            return false;
        }
        setting->value.asChoice = next.asChoice;
        break;
    case FCSE_SettingType_Slider:
        if (next.asSlider < setting->minValue || next.asSlider > setting->maxValue) {
            Log::Loader("settings: ignoring slider value " + std::to_string(next.asSlider) +
                        " for [" + setting->groupName + "] " + setting->name + " - its range is " +
                        std::to_string(setting->minValue) + ".." +
                        std::to_string(setting->maxValue));
            return false;
        }
        if (setting->value.asSlider == next.asSlider) {
            return false;
        }
        setting->value.asSlider = next.asSlider;
        break;
    case FCSE_SettingType_Text: {
        const char* incoming = next.asText != nullptr ? next.asText : "";
        size_t length = std::strlen(incoming);
        const bool truncated = length > setting->maxTextLength;
        if (truncated) {
            length = setting->maxTextLength;
        }
        if (setting->text.size() == length &&
            std::memcmp(setting->text.data(), incoming, length) == 0) {
            return false;
        }
        if (truncated) {
            Log::Loader("settings: truncating [" + setting->groupName + "] " + setting->name +
                        " to its " + std::to_string(setting->maxTextLength) + "-character limit");
        }
        setting->text.assign(incoming, length);
        RefreshTextPointer(*setting);
        break;
    }
    default:
        return false;
    }

    const std::string stored = FormatValue(*setting);
    g_ini.Set(setting->groupName, setting->name, stored);
    g_dirty = true;
    Log::Loader("settings: [" + setting->groupName + "] " + setting->name + " = " + stored);

    Notify(*setting);
    Flush();
    return true;
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

    FCSE_SettingValue next{};
    next.type = FCSE_SettingType_Checkbox;
    next.asNumber = setting->value.asCheckbox ? 0 : 1;
    SetValue(setting, next);
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

const std::string* SettingsRegistry::RawValue(const char* key) {
    if (const std::string* value = g_ini.Find(kOwnGroup, key)) {
        return value;
    }
    // Section names match case-sensitively, and FCSE's own flags have been written both ways.
    return g_ini.Find("FCSE", key);
}

void SettingsRegistry::ResetForTesting() {
    g_groups.clear();
    g_ini = IniFile();
    g_configPath.clear();
    g_dirty = false;
}

} // namespace FCSE
