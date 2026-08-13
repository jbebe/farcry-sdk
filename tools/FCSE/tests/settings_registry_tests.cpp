// Tests for SettingsRegistry, the store behind FCSE_PluginAPI::RegisterSettings and bin\fcse.ini.
//
// Pure declaration-validation, parsing and serialization over IniFile; the only engine dependency
// is Log, which no-ops when Log::Init was never called. Run with `ctest` from the build directory,
// or just launch the exe.
//
// The failure modes this covers are all quiet ones: a stored value that stops round-tripping
// orphans a player's setting on the next launch, and a rejected value that gets stored anyway
// hides a disagreement between a plugin and the registry.
#include "api/settings_registry.h"

#include <gtest/gtest.h>

#include <cstring>
#include <string>
#include <vector>
#include <windows.h>

namespace {

using FCSE::SettingsRegistry;

// What a setting's onChanged was told, so the callback contract - fired once at registration, then
// on every accepted change - can be asserted without a plugin.
struct CallbackLog {
    int calls = 0;
    FCSE_SettingType type = FCSE_SettingType_Checkbox;
    int32_t number = 0;
    std::string text;
};

void RecordChange(const FCSE_SettingValue* value, void* userdata) {
    CallbackLog* log = static_cast<CallbackLog*>(userdata);
    ++log->calls;
    log->type = value->type;
    log->number = value->asNumber;
    log->text = value->type == FCSE_SettingType_Text && value->asText != nullptr ? value->asText
                                                                                : std::string();
}

const char* kDifficulties[] = {"Easy", "Normal", "Hardcore"};

// Each test gets its own scratch fcse.ini named after itself, and a registry reset on both sides -
// the registry is process-global, so a leaked group would otherwise reach the next case.
class SettingsRegistryTest : public ::testing::Test {
protected:
    void SetUp() override {
        const ::testing::TestInfo* info = ::testing::UnitTest::GetInstance()->current_test_info();
        std::string name = std::string("settings_test_") + info->name() + ".ini";
        path_.assign(name.begin(), name.end()); // ASCII test names only
        DeleteFileW(path_.c_str());
        SettingsRegistry::ResetForTesting();
    }

    void TearDown() override {
        SettingsRegistry::ResetForTesting();
        DeleteFileW(path_.c_str());
    }

    void WriteAll(const char* text) const {
        HANDLE f = CreateFileW(path_.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS,
                               FILE_ATTRIBUTE_NORMAL, nullptr);
        ASSERT_NE(f, INVALID_HANDLE_VALUE);
        DWORD written = 0;
        WriteFile(f, text, static_cast<DWORD>(std::strlen(text)), &written, nullptr);
        CloseHandle(f);
    }

    std::string ReadAll() const {
        HANDLE f = CreateFileW(path_.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING,
                               FILE_ATTRIBUTE_NORMAL, nullptr);
        if (f == INVALID_HANDLE_VALUE) {
            return "<missing>";
        }
        char buf[8192];
        DWORD read = 0;
        ReadFile(f, buf, sizeof(buf), &read, nullptr);
        CloseHandle(f);
        return std::string(buf, read);
    }

    void Init() const { SettingsRegistry::Init(path_); }

    bool Register(const char* plugin, const std::vector<FCSE_Setting>& settings) const {
        return SettingsRegistry::RegisterSettings(plugin, settings.data(), settings.size());
    }

    // The registry hands out non-const Setting* through a const Group - a const unique_ptr still
    // gets() a mutable pointee, which is what SetValue takes.
    SettingsRegistry::Setting* Find(const char* plugin, const char* name) const {
        const SettingsRegistry::Group* group = SettingsRegistry::FindGroup(plugin);
        if (group == nullptr) {
            return nullptr;
        }
        for (const std::unique_ptr<SettingsRegistry::Setting>& setting : group->settings) {
            if (setting->name == name) {
                return setting.get();
            }
        }
        return nullptr;
    }

    std::wstring path_;
};

FCSE_Setting Checkbox(const char* name, bool defaultValue) {
    return FCSE_Setting{name, FCSE_CHECKBOX(defaultValue), nullptr, nullptr, nullptr, 0, 0, 0,
                        nullptr, 0};
}

FCSE_Setting Choice(const char* name, uint32_t defaultIndex) {
    return FCSE_Setting{name, FCSE_CHOICE(defaultIndex), nullptr, nullptr, kDifficulties, 3, 0, 0,
                        nullptr, 0};
}

FCSE_Setting Slider(const char* name, int32_t defaultValue, int32_t min, int32_t max) {
    return FCSE_Setting{name, FCSE_SLIDER(defaultValue), nullptr, nullptr, nullptr, 0, min, max,
                        nullptr, 0};
}

FCSE_Setting Text(const char* name, const char* defaultText, uint32_t maxLength) {
    return FCSE_Setting{name,    FCSE_TEXT(), nullptr, nullptr,     nullptr,
                        0,       0,           0,       defaultText, maxLength};
}

TEST_F(SettingsRegistryTest, RegistersEveryTypeAndSeedsTheFileWithItsDefaults) {
    Init();
    ASSERT_TRUE(Register("demo", {Checkbox("Verbose", true), Choice("Difficulty", 2),
                                  Slider("Distance", 6, 1, 10), Text("Name", "kilimanjaro", 24)}));

    ASSERT_EQ(SettingsRegistry::Groups().size(), 1u);
    EXPECT_EQ(SettingsRegistry::Groups()[0].settings.size(), 4u);
    EXPECT_TRUE(Find("demo", "Verbose")->value.asCheckbox);
    EXPECT_EQ(Find("demo", "Difficulty")->value.asChoice, 2u);
    EXPECT_EQ(Find("demo", "Distance")->value.asSlider, 6);
    EXPECT_STREQ(Find("demo", "Name")->value.asText, "kilimanjaro");

    SettingsRegistry::Flush();
    std::string written = ReadAll();
    EXPECT_NE(written.find("[demo]"), std::string::npos) << written;
    EXPECT_NE(written.find("Verbose = true"), std::string::npos) << written;
    EXPECT_NE(written.find("Difficulty = Hardcore"), std::string::npos)
        << "a Choice serializes as its label, not its index: " << written;
    EXPECT_NE(written.find("Distance = 6"), std::string::npos) << written;
    EXPECT_NE(written.find("Name = kilimanjaro"), std::string::npos) << written;
}

TEST_F(SettingsRegistryTest, StoredValuesWinOverThePluginsDefaults) {
    WriteAll("[demo]\nVerbose = false\nDifficulty = Easy\nDistance = 9\nName = mike\n");
    Init();
    ASSERT_TRUE(Register("demo", {Checkbox("Verbose", true), Choice("Difficulty", 2),
                                  Slider("Distance", 6, 1, 10), Text("Name", "kilimanjaro", 24)}));

    EXPECT_FALSE(Find("demo", "Verbose")->value.asCheckbox);
    EXPECT_EQ(Find("demo", "Difficulty")->value.asChoice, 0u);
    EXPECT_EQ(Find("demo", "Distance")->value.asSlider, 9);
    EXPECT_STREQ(Find("demo", "Name")->value.asText, "mike");
}

TEST_F(SettingsRegistryTest, CheckboxAcceptsTheSpellingsAPlayerMightWrite) {
    const char* trueSpellings[] = {"true", "TRUE", "1", "yes", "On"};
    for (const char* spelling : trueSpellings) {
        SettingsRegistry::ResetForTesting();
        WriteAll((std::string("[demo]\nVerbose = ") + spelling + "\n").c_str());
        Init();
        ASSERT_TRUE(Register("demo", {Checkbox("Verbose", false)}));
        EXPECT_TRUE(Find("demo", "Verbose")->value.asCheckbox) << spelling;
    }

    const char* falseSpellings[] = {"false", "FALSE", "0", "no", "Off"};
    for (const char* spelling : falseSpellings) {
        SettingsRegistry::ResetForTesting();
        WriteAll((std::string("[demo]\nVerbose = ") + spelling + "\n").c_str());
        Init();
        ASSERT_TRUE(Register("demo", {Checkbox("Verbose", true)}));
        EXPECT_FALSE(Find("demo", "Verbose")->value.asCheckbox) << spelling;
    }
}

TEST_F(SettingsRegistryTest, AnUnreadableStoredValueFallsBackToTheDefaultAndIsRewritten) {
    WriteAll("[demo]\nVerbose = perhaps\n");
    Init();
    ASSERT_TRUE(Register("demo", {Checkbox("Verbose", true)}));

    EXPECT_TRUE(Find("demo", "Verbose")->value.asCheckbox) << "should fall back, never guess";
    SettingsRegistry::Flush();
    EXPECT_NE(ReadAll().find("Verbose = true"), std::string::npos)
        << "the corrected value should be written back";
}

TEST_F(SettingsRegistryTest, ChoiceAcceptsAnIndexWhenNoLabelMatches) {
    WriteAll("[demo]\nDifficulty = 1\n");
    Init();
    ASSERT_TRUE(Register("demo", {Choice("Difficulty", 0)}));
    EXPECT_EQ(Find("demo", "Difficulty")->value.asChoice, 1u);
}

TEST_F(SettingsRegistryTest, ChoiceMatchesItsLabelWithoutRegardToCase) {
    WriteAll("[demo]\nDifficulty = hardcore\n");
    Init();
    ASSERT_TRUE(Register("demo", {Choice("Difficulty", 0)}));
    EXPECT_EQ(Find("demo", "Difficulty")->value.asChoice, 2u);
}

TEST_F(SettingsRegistryTest, ADefaultOutsideItsOwnSliderRangeIsClamped) {
    Init();
    ASSERT_TRUE(Register("demo", {Slider("Distance", 99, 1, 10)}));
    EXPECT_EQ(Find("demo", "Distance")->value.asSlider, 10);
}

TEST_F(SettingsRegistryTest, AStoredSliderValueIsClampedRatherThanRejected) {
    WriteAll("[demo]\nDistance = 99\n");
    Init();
    ASSERT_TRUE(Register("demo", {Slider("Distance", 6, 1, 10)}));
    EXPECT_EQ(Find("demo", "Distance")->value.asSlider, 10)
        << "a plugin narrowing its range should not throw the player's choice away";
}

TEST_F(SettingsRegistryTest, TextIsTruncatedToItsDeclaredLimit) {
    Init();
    ASSERT_TRUE(Register("demo", {Text("Name", "kilimanjaro", 4)}));
    EXPECT_STREQ(Find("demo", "Name")->value.asText, "kili");
}

TEST_F(SettingsRegistryTest, RejectsDeclarationsThatWouldProduceAnUnusableRow) {
    Init();
    EXPECT_FALSE(Register("", {Checkbox("Verbose", true)})) << "empty group name";
    EXPECT_FALSE(Register("de[mo]", {Checkbox("Verbose", true)})) << "brackets in group name";
    EXPECT_FALSE(Register("demo", {Checkbox("", true)})) << "empty setting name";
    EXPECT_FALSE(Register("demo", {Checkbox("a=b", true)})) << "'=' in setting name";
    EXPECT_FALSE(Register("demo", {Checkbox(" padded ", true)})) << "whitespace edges";
    EXPECT_FALSE(Register("demo", {Slider("Distance", 1, 10, 10)})) << "minValue >= maxValue";

    FCSE_Setting oneLabel = Choice("Difficulty", 0);
    oneLabel.choiceCount = 1;
    EXPECT_FALSE(Register("demo", {oneLabel})) << "a Choice needs at least two labels";
}

TEST_F(SettingsRegistryTest, ASecondSettingOfTheSameNameIsSkipped) {
    Init();
    ASSERT_TRUE(Register("demo", {Checkbox("Verbose", true), Checkbox("Verbose", false)}));
    EXPECT_EQ(SettingsRegistry::Groups()[0].settings.size(), 1u);
    EXPECT_TRUE(Find("demo", "Verbose")->value.asCheckbox) << "the first registration should win";
}

TEST_F(SettingsRegistryTest, SetValueRejectsAMismatchedTypeAndAnOutOfRangeValue) {
    Init();
    ASSERT_TRUE(Register("demo", {Slider("Distance", 6, 1, 10)}));
    SettingsRegistry::Setting* distance = Find("demo", "Distance");

    FCSE_SettingValue wrongType{};
    wrongType.type = FCSE_SettingType_Checkbox;
    wrongType.asNumber = 1;
    EXPECT_FALSE(SettingsRegistry::SetValue(distance, wrongType));

    FCSE_SettingValue outOfRange{};
    outOfRange.type = FCSE_SettingType_Slider;
    outOfRange.asSlider = 99;
    EXPECT_FALSE(SettingsRegistry::SetValue(distance, outOfRange));

    EXPECT_EQ(distance->value.asSlider, 6) << "a rejected value must not be stored";
}

TEST_F(SettingsRegistryTest, SettingTheValueItAlreadyHasChangesNothing) {
    Init();
    ASSERT_TRUE(Register("demo", {Slider("Distance", 6, 1, 10)}));
    SettingsRegistry::Flush();
    std::string before = ReadAll();

    FCSE_SettingValue same{};
    same.type = FCSE_SettingType_Slider;
    same.asSlider = 6;
    EXPECT_FALSE(SettingsRegistry::SetValue(Find("demo", "Distance"), same))
        << "the page reads every control back on every frame - a no-op must stay a no-op";
    EXPECT_EQ(ReadAll(), before) << "a no-op must not rewrite the file";
}

TEST_F(SettingsRegistryTest, AnAcceptedChangeIsStoredAndPersistedImmediately) {
    Init();
    ASSERT_TRUE(Register("demo", {Slider("Distance", 6, 1, 10)}));
    SettingsRegistry::Flush();

    FCSE_SettingValue next{};
    next.type = FCSE_SettingType_Slider;
    next.asSlider = 7;
    EXPECT_TRUE(SettingsRegistry::SetValue(Find("demo", "Distance"), next));
    EXPECT_NE(ReadAll().find("Distance = 7"), std::string::npos)
        << "a change reaches the file without waiting for a Flush";
}

TEST_F(SettingsRegistryTest, ToggleFlipsACheckboxAndIgnoresEveryOtherType) {
    Init();
    ASSERT_TRUE(Register("demo", {Checkbox("Verbose", false), Slider("Distance", 6, 1, 10)}));

    SettingsRegistry::ToggleCheckbox(Find("demo", "Verbose"));
    EXPECT_TRUE(Find("demo", "Verbose")->value.asCheckbox);
    SettingsRegistry::ToggleCheckbox(Find("demo", "Verbose"));
    EXPECT_FALSE(Find("demo", "Verbose")->value.asCheckbox);

    SettingsRegistry::ToggleCheckbox(Find("demo", "Distance"));
    EXPECT_EQ(Find("demo", "Distance")->value.asSlider, 6);
}

TEST_F(SettingsRegistryTest, TheCallbackFiresAtRegistrationAndOnEveryAcceptedChange) {
    Init();
    CallbackLog log;
    FCSE_Setting declared = Slider("Distance", 6, 1, 10);
    declared.onChanged = &RecordChange;
    declared.userdata = &log;
    ASSERT_TRUE(Register("demo", {declared}));

    EXPECT_EQ(log.calls, 1) << "registration resolves the value and reports it";
    EXPECT_EQ(log.type, FCSE_SettingType_Slider);
    EXPECT_EQ(log.number, 6);

    FCSE_SettingValue next{};
    next.type = FCSE_SettingType_Slider;
    next.asSlider = 7;
    ASSERT_TRUE(SettingsRegistry::SetValue(Find("demo", "Distance"), next));
    EXPECT_EQ(log.calls, 2);
    EXPECT_EQ(log.number, 7);

    EXPECT_FALSE(SettingsRegistry::SetValue(Find("demo", "Distance"), next));
    EXPECT_EQ(log.calls, 2) << "a no-op must not fire the callback";
}

TEST_F(SettingsRegistryTest, TheCallbackReportsTheStoredValueNotThePluginsDefault) {
    WriteAll("[demo]\nDistance = 9\n");
    Init();
    CallbackLog log;
    FCSE_Setting declared = Slider("Distance", 6, 1, 10);
    declared.onChanged = &RecordChange;
    declared.userdata = &log;
    ASSERT_TRUE(Register("demo", {declared}));

    EXPECT_EQ(log.calls, 1);
    EXPECT_EQ(log.number, 9);
}

TEST_F(SettingsRegistryTest, AWriteKeepsCommentsAndTheGroupsFCSEDoesNotOwn) {
    WriteAll("# hand-written note\n"
             "[oldmod]\n"
             "Kept = yes\n"
             "\n"
             "[demo]\n"
             "Verbose = false\n");
    Init();
    ASSERT_TRUE(Register("demo", {Checkbox("Verbose", false), Slider("Distance", 6, 1, 10)}));
    SettingsRegistry::Flush();

    std::string written = ReadAll();
    EXPECT_NE(written.find("# hand-written note"), std::string::npos) << written;
    EXPECT_NE(written.find("[oldmod]"), std::string::npos)
        << "a group whose plugin is no longer installed is kept, not dropped: " << written;
    EXPECT_NE(written.find("Kept = yes"), std::string::npos) << written;
    EXPECT_NE(written.find("Distance = 6"), std::string::npos) << written;
}

// FCSE's own diagnostic flags are hand-edited, and section lookup is case-sensitive - so both
// spellings a player could reasonably have written have to resolve.
TEST_F(SettingsRegistryTest, RawValueReadsFcsesOwnGroupUnderEitherSpelling) {
    WriteAll("[fcse]\nTick self check frames = 120\n");
    Init();
    const std::string* frames = SettingsRegistry::RawValue("Tick self check frames");
    ASSERT_NE(frames, nullptr);
    EXPECT_EQ(*frames, "120");
    EXPECT_EQ(SettingsRegistry::RawValue("Missing key"), nullptr);

    SettingsRegistry::ResetForTesting();
    WriteAll("[FCSE]\nPlain label rows = true\n");
    Init();
    const std::string* rows = SettingsRegistry::RawValue("Plain label rows");
    ASSERT_NE(rows, nullptr);
    EXPECT_EQ(*rows, "true");
}

TEST_F(SettingsRegistryTest, RegistrationsFromTwoPluginsStayInTheirOwnGroups) {
    Init();
    ASSERT_TRUE(Register("alpha", {Checkbox("Verbose", true)}));
    ASSERT_TRUE(Register("beta", {Checkbox("Verbose", false)}));

    ASSERT_EQ(SettingsRegistry::Groups().size(), 2u);
    EXPECT_EQ(SettingsRegistry::Groups()[0].pluginName, "alpha") << "registration order is display order";
    EXPECT_TRUE(Find("alpha", "Verbose")->value.asCheckbox);
    EXPECT_FALSE(Find("beta", "Verbose")->value.asCheckbox);
    EXPECT_EQ(SettingsRegistry::FindGroup("gamma"), nullptr);
}

} // namespace
