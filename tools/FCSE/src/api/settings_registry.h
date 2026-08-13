#pragma once

#include "fcse_api.h"

#include <memory>
#include <string>
#include <vector>

// Backs FCSE_PluginAPI::RegisterSettings (tier 4) and owns bin\fcse.ini.
//
// FCSE holds the value for every registered setting; plugins never do. A plugin declares what it
// has and how to reach it (a name, a type, a default, a callback) and is told the resolved value -
// once at registration, then again on every change. That inversion is what makes persistence
// possible at all: the old bool*-based API had no way to write a value back, because FCSE only
// ever knew where the bool lived, never what it meant or what to call it in a file.
//
// Registration order is display order, both in the file and in the in-game menu. A plugin that
// registers nothing has no group in the file - see the header comment on RegisterSettings.
namespace FCSE {

class SettingsRegistry {
public:
    // One registered setting, plus FCSE's own copy of its current value. Allocated individually and
    // never moved or freed, so a pointer handed to a menu row stays valid for the whole session
    // even as later plugins register more settings (see fcse_page.cpp's ToggleHandler).
    struct Setting {
        std::string groupName; // the owning plugin's name - the [group] this writes back into
        std::string name;
        FCSE_SettingValue value; // FCSE-owned and authoritative; the file is its serialization
        FCSE_SettingChangedFn onChanged;
        void* userdata;

        // Per-type configuration, copied out of the FCSE_Setting the plugin declared so nothing
        // here points into the plugin's memory. Only the fields matching `value.type` are set.
        std::vector<std::string> choices; // Choice - the option labels, in cycle order
        int minValue;                     // Slider
        int maxValue;                     // Slider
        std::string text;                 // Text - the value itself; value.asText points at this
        size_t maxTextLength;             // Text
    };

    struct Group {
        std::string pluginName;
        std::vector<std::unique_ptr<Setting>> settings;
    };

    // Reads `configPath` into memory. Call once, before any plugin can register - registration
    // resolves each setting against what this loaded. A missing file is normal (first run) and
    // leaves an empty document that Flush() creates.
    static void Init(const std::wstring& configPath);

    // Backs FCSE_PluginAPI::RegisterSettings - see fcse_api.h for the contract. Captures caller
    // identity itself via _ReturnAddress(), same convention as FunctionRegistry::Register and
    // HookManager::Hook.
    static bool RegisterSettings(const char* pluginName, const FCSE_Setting* settings,
                                  size_t settingCount);

    // Every group, in registration order. Read by fcse_page.cpp each time the menu is rebuilt.
    static const std::vector<Group>& Groups();

    // The group registered under `pluginName`, or nullptr if that plugin registered no settings.
    // Note this matches on the name the plugin *chose*, which it is free to make something other
    // than its module name - see fcse_page.cpp's AppendRows for how the menu reconciles the two.
    static const Group* FindGroup(const std::string& pluginName);

    // Stores a new value, fires the setting's callback and persists the file. What the in-game page
    // calls when a control moves.
    //
    // `next.type` must match the setting's own type, and the value is validated against the
    // setting's configuration - a Choice index past the end of `choices`, or a Slider outside
    // [minValue, maxValue], is rejected rather than clamped, because either means the caller and
    // the registry disagree about the setting and quietly storing something else would hide it.
    // A no-op change is dropped, so calling this on every display costs nothing.
    //
    // Returns whether the value actually changed. Rejections are logged.
    static bool SetValue(Setting* setting, const FCSE_SettingValue& next);

    // Flips a Checkbox. Shorthand for SetValue with the negated value; no-ops (and logs) for any
    // other setting type.
    static void ToggleCheckbox(Setting* setting);

    // Writes the file if anything changed since the last write. Call after plugin loading, so a
    // first run leaves a complete, hand-editable file even if the player never opens the menu.
    static void Flush();

    // A key from FCSE's own [fcse] group in the already-loaded document, or nullptr if unset.
    // For the loader's diagnostic flags, which are hand-edited rather than registered as settings
    // and so are never written back. Saves re-reading the file to answer one question.
    static const std::string* RawValue(const char* key);

    // Drops every registered group and the loaded document, so a test can start from nothing. The
    // registry is process-global state that the loader only ever builds once.
    static void ResetForTesting();
};

} // namespace FCSE
