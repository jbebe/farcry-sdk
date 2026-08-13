#include "ui/page_internal.h"

#include "log.h"
#include "ui/fcse_page.h"
#include "ui/menu_item_handler.h"
#include "util/seh.h"

#include <cstdint>
#include <cstdio>
#include <string>

namespace FCSE {
namespace page {

namespace {

    // Kept the same width so the rows line up in a proportional-ish menu font.
    constexpr wchar_t kOnSuffix[] = L"[ON] ";
    constexpr wchar_t kOffSuffix[] = L"[OFF]";

    // Both accessors take and return a pointer to the value, so the width is part of the call:
    // Value is bool for a Checkbox row's CValueListSetting and uint32_t for the 4-byte-valued ones
    // (CValueListSetting<unsigned> for a Choice, CSliderSetting for a Slider). Reading four bytes
    // through the bool form would run off the end of the engine's value array.
    //
    // Signedness does not matter: a Choice index is never negative and a slider's range is
    // whatever the plugin declared, so the caller decides which way to read the same four bytes.
    template <typename Value>
    bool SafeSetSettingValue(void* settingObject, Value value, DWORD* outCode) {
        __try {
            void** vtable = *reinterpret_cast<void***>(settingObject);
            auto setValue = reinterpret_cast<void(__thiscall*)(void*, const Value*)>(
                vtable[kSettingSetValueSlot]);
            setValue(settingObject, &value);
            return true;
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            *outCode = GetExceptionCode();
            return false;
        }
    }

    template <typename Value>
    bool SafeGetSettingValue(void* settingObject, Value* outValue, DWORD* outCode) {
        __try {
            void** vtable = *reinterpret_cast<void***>(settingObject);
            auto getValue =
                reinterpret_cast<const Value*(__thiscall*)(void*)>(vtable[kSettingGetValueSlot]);
            const Value* value = getValue(settingObject);
            if (value == nullptr) {
                return false;
            }
            *outValue = *value;
            return true;
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            *outCode = GetExceptionCode();
            return false;
        }
    }

}

    // The click handler for toggle rows: flip the value, then rebuild the page so the row's
    // [ON]/[OFF] reflects it immediately.
    //
    // Only reachable on the "Plain label rows" fallback path - a native checkbox row carries a
    // CValueListSetting and no button handler at all.
    //
    // The rebuild re-enters the engine's row list from inside its own click dispatch, which is a
    // known hazard - the row being dispatched is destroyed while the engine may still hold it. It
    // is used anyway because it is the only thing that actually refreshes the label, and because
    // the evidence exonerates it: the crash this page hit earlier came from native AddBoolSetting
    // rows, where FCSE had no code in the click path at all, and toggling with a rebuild is the
    // mechanism the shipped Mod Configuration Menu has always used. Two alternatives were tried and
    // rejected - deferring the refresh to the next display (correct, but the label visibly lags a
    // click behind) and rewriting the label buffer in place (no engine re-entry at all, but nothing
    // redraws, so the engine copies the text at AddButton time rather than storing the pointer).
    struct TogglePayload {
        SettingsRegistry::Setting* setting;

        void OnActivate() {
            if (setting == nullptr) {
                return; // caption rows exist to be read, not clicked
            }
            DWORD code = 0;
            if (!SehCall(&code, &SettingsRegistry::ToggleCheckbox, setting)) {
                LogFailed("SettingsRegistry::ToggleCheckbox", code);
                return;
            }
            RebuildRows(g_page);
        }
    };

    // Activating a Text row hands input focus to its EditBox, which is the only way in: the field is
    // authored beside the row list with empty NEIGHBORS, so no amount of arrow-key navigation
    // reaches it. This is the same move the engine makes for the SetFocusNomad action - resolve the
    // target element and call magma::Page::SetSelected on the owning page.
    struct FocusPayload {
        size_t row;

        void OnActivate() {
            void* element = SlotCellElement(row, CellKind::Edit);
            DWORD code = 0;
            void* magmaPage = nullptr;
            if (element == nullptr || g_page == nullptr ||
                !SehReadPointer(g_page, kBoundMagmaPageOffset, &magmaPage, &code) ||
                magmaPage == nullptr) {
                return;
            }
            if (!SafeSetSelected(magmaPage, element, &code)) {
                LogFailed("magma::Page::SetSelected", code);
            }
        }
    };

    // A Text row: a label, and an EditBox cell the player types into.
    //
    // The row itself carries no handler and no CUISettingBase - there is no "string setting" in
    // CSettingsPage - so the label is a plain button and the editing happens entirely in the cell.
    // This mirrors the stock Options > Network page, which authors bare EditBox elements at its row
    // positions rather than routing text through a dialog.
    bool AppendTextRow(void* page, SettingsRegistry::Setting* setting, size_t row) {
        LabelStorage().push_back(L"   " + WidenAscii(setting->name));
        DWORD code = 0;
        if (!SafeAddButton(page, LabelStorage().back().c_str(), MenuItemHandler<FocusPayload>::Create({row}), &code)) {
            LogFailed("AddButton (text row)", code);
            return false;
        }
        BindEditCell(setting, row);
        return true;
    }

    // NOTE: the native AddBoolSetting path below has no handler of its own. An earlier version made
    // one here and passed it to AddBoolSetting; clicking a row then crashed the game to desktop,
    // before any FCSE code ran - the instrumented log showed the handler was never entered. Every
    // boolean row on the stock Game tab passes handler = 0, because a settings row is driven by
    // its CValueListSetting and by widget events rather than by a button handler. Values are read
    // back from the control on the next rebuild instead; see SyncValuesFromControls.

    std::vector<LiveRow>& LiveRows() {
        static std::vector<LiveRow> rows;
        return rows;
    }
    // Persistence, without a click handler. The engine owns the control and changes it in place;
    // FCSE reads it back and writes fcse.ini for anything that moved.
    //
    // Called from two places, which is why it does not clear LiveRows() itself: from the apply slot
    // (+0x50), where the rows are still on screen and the player may change another one a moment
    // later, and from the start of a rebuild, which clears the list itself once the old controls
    // have been read. Clearing here would make the apply slot a one-shot per display.
    //
    // Idempotent by construction - a row whose control still matches the registry is skipped - so
    // being called twice for the same change costs nothing.
    void SyncValuesFromControls() {
        for (const LiveRow& row : LiveRows()) {
            DWORD code = 0;
            FCSE_SettingValue shown{};
            shown.type = row.setting->value.type;

            switch (row.setting->value.type) {
            case FCSE_SettingType_Checkbox: {
                bool value = false;
                if (!SafeGetSettingValue(row.settingObject, &value, &code)) {
                    if (code != 0) {
                        LogFailed("CValueListSetting<bool>::GetValue", code);
                    }
                    continue;
                }
                shown.asNumber = value ? 1 : 0;
                break;
            }
            case FCSE_SettingType_Choice: {
                uint32_t value = 0;
                if (!SafeGetSettingValue(row.settingObject, &value, &code)) {
                    if (code != 0) {
                        LogFailed("CValueListSetting<unsigned>::GetValue", code);
                    }
                    continue;
                }
                shown.asChoice = value;
                break;
            }
            case FCSE_SettingType_Slider: {
                uint32_t value = 0;
                if (!SafeGetSettingValue(row.settingObject, &value, &code)) {
                    if (code != 0) {
                        LogFailed("CSliderSetting::GetValue", code);
                    }
                    continue;
                }
                shown.asSlider = static_cast<int32_t>(value);
                break;
            }
            case FCSE_SettingType_Text: {
                // For a Text row the "setting object" is the EditBox widget itself: a string has no
                // CUISettingBase, so BindEditCell records the widget here instead.
                wchar_t typed[kEditTextMax];
                if (!SafeReadEditText(row.settingObject, typed, &code)) {
                    if (code != 0) {
                        LogFailed("reading an EditBox's text", code);
                    }
                    continue;
                }
                // Into a stack buffer rather than a std::string: this runs for every Text row on
                // every frame the page is open, and the reader already bounds the text to fit.
                char narrowed[kEditTextMax];
                size_t length = 0;
                for (const wchar_t* c = typed; *c != L'\0'; ++c) {
                    narrowed[length++] = *c < 0x100 ? static_cast<char>(*c) : '?';
                }
                narrowed[length] = '\0';
                shown.asText = narrowed;
                if (!SehCall(&code, &SettingsRegistry::SetValue, row.setting, shown)) {
                    LogFailed("SettingsRegistry::SetValue", code);
                }

                // Enter: the engine has copied the live text into the committed string, which it
                // does at no other time. The stock Network page answers that by rebuilding itself,
                // so this does the same - deferred by a frame rather than done here, because
                // rebuilding tears down the very rows this loop is walking.
                wchar_t committed[kEditTextMax] = {};
                if (SafeReadStringAt(row.settingObject, kEditBoxCommittedTextOffset, committed,
                                     kEditTextMax, &code) &&
                    committed != row.committed) {
                    Log::Loader("FcsePage: \"" + row.setting->name +
                                "\" committed with Enter - refreshing the page");
                    g_rebuildRequested = true;
                }
                continue; // asText points at a local, so it must not fall through to the shared call
            }
            default:
                continue; // a type with no control to read back
            }

            // SetValue drops a no-op change itself, which is the common case here - most rows have
            // not moved - so there is nothing to compare against first.
            if (!SehCall(&code, &SettingsRegistry::SetValue, row.setting, shown)) {
                LogFailed("SettingsRegistry::SetValue", code);
            }
        }
    }

    void AppendCaption(void* page, const std::wstring& text, size_t* row) {
        if (*row >= kSlotCount) {
            return;
        }
        LabelStorage().push_back(text);
        DWORD code = 0;
        if (!SafeAddButton(page, LabelStorage().back().c_str(), nullptr, &code)) {
            LogFailed("AddButton (caption row)", code);
            return;
        }
        ++*row;
    }

    // Whether the row's FCSE_SLOT_nn actually resolved to a widget. Add*Setting binds the value
    // widget into setting+0x44 and guards every one of its item-adds on that field being non-null -
    // so an unresolved slot leaves the value array empty, and everything that later walks it finds
    // nothing. Logged rather than assumed, because a row with no control looks identical to a
    // working one until it is used.
    bool NativeControlBound(void* settingObject, const char* slotParam) {
        DWORD code = 0;
        SettingFields fields{};
        if (!SafeReadSettingFields(settingObject, &fields, &code)) {
            LogFailed("reading the CValueListSetting's fields", code);
            return false;
        }
        char detail[192];
        std::snprintf(detail, sizeof(detail),
                      "FcsePage: %s -> setting=0x%08X widget=0x%08X values=0x%08X len=%u", slotParam,
                      reinterpret_cast<unsigned>(settingObject), fields.widget, fields.values,
                      fields.valuesLength);
        Log::Loader(detail);

        if (fields.widget == 0 || fields.values == 0 || fields.valuesLength == 0) {
            Log::Loader(std::string("FcsePage: ") + slotParam +
                        " did not bind a value widget - the row has no control, so it is left "
                        "unseeded and unread");
            return false;
        }
        return true;
    }

    // Every native row below passes handler = 0, deliberately. Every settings row on the stock Game
    // tab does the same, because such a row is driven by the CUISettingBase attached to it and by
    // widget events - not by a button handler. Passing FCSE's hand-rolled handler put the engine
    // down a path that fake vtable cannot satisfy and crashed the game on the click, before any FCSE
    // code ran. Changes are picked up by reading the control back instead, in SyncValuesFromControls.
    //
    // Each returns whether a row was added, which is what the caller counts - a row that was added
    // but failed to bind its control still occupies a slot.

    bool AppendCheckboxRow(void* page, SettingsRegistry::Setting* setting, const char* slotParam,
                           size_t row) {
        LabelStorage().push_back(L"   " + WidenAscii(setting->name));

        void* settingObject = nullptr;
        DWORD code = 0;
        if (!SafeAddBoolSetting(page, LabelStorage().back().c_str(), slotParam, YesText(), NoText(),
                                &settingObject, &code)) {
            LogFailed("CSettingsPage::AddBoolSetting", code);
            return false;
        }
        if (settingObject == nullptr || !NativeControlBound(settingObject, slotParam)) {
            return true;
        }

        // Seed the control from the registry, or the row would show whatever the list happens to
        // start on rather than the value in fcse.ini.
        if (!SafeSetSettingValue(settingObject, setting->value.asCheckbox != 0, &code)) {
            LogFailed("CValueListSetting<bool>::SetValue", code);
            return true;
        }
        ShowSlotCell(row, CellKind::Value);
        LiveRows().push_back({setting, settingObject});
        return true;
    }

    bool AppendChoiceRow(void* page, SettingsRegistry::Setting* setting, const char* slotParam,
                           size_t row) {
        LabelStorage().push_back(L"   " + WidenAscii(setting->name));
        const wchar_t* label = LabelStorage().back().c_str();

        // The item labels go into the same permanent storage as the row label. The engine's own
        // caller hands AddBoolSetting two process-lifetime globals, so nothing proves it copies the
        // strings - and keeping them alive costs a deque entry each.
        //
        // The two arrays are locals because those it does demonstrably copy: SetItems appends each
        // value into the setting's own vector as it walks them.
        std::vector<const wchar_t*> itemLabels;
        std::vector<unsigned> itemValues;
        itemLabels.reserve(setting->choices.size());
        itemValues.reserve(setting->choices.size());
        for (size_t i = 0; i < setting->choices.size(); ++i) {
            LabelStorage().push_back(WidenAscii(setting->choices[i]));
            itemLabels.push_back(LabelStorage().back().c_str());
            itemValues.push_back(static_cast<unsigned>(i));
        }

        void* settingObject = nullptr;
        DWORD code = 0;
        if (!SafeAddValueListSetting(page, label, slotParam,
                                     static_cast<unsigned>(itemLabels.size()), itemLabels.data(),
                                     itemValues.data(), &settingObject, &code)) {
            LogFailed("CSettingsPage::AddValueListSetting", code);
            return false;
        }
        if (settingObject == nullptr || !NativeControlBound(settingObject, slotParam)) {
            return true;
        }

        if (!SafeSetSettingValue(settingObject, setting->value.asChoice, &code)) {
            LogFailed("CValueListSetting<unsigned>::SetValue", code);
            return true;
        }
        ShowSlotCell(row, CellKind::Value);
        LiveRows().push_back({setting, settingObject});
        return true;
    }

    bool AppendSliderRow(void* page, SettingsRegistry::Setting* setting, const char* slotParam,
                           size_t row) {
        LabelStorage().push_back(L"   " + WidenAscii(setting->name));

        void* settingObject = nullptr;
        DWORD code = 0;
        if (!SafeAddSliderSetting(page, LabelStorage().back().c_str(), slotParam, setting->minValue,
                                  setting->maxValue, &settingObject, &code)) {
            LogFailed("CSettingsPage::AddSliderSetting", code);
            return false;
        }
        if (settingObject == nullptr) {
            return true;
        }

        SliderFields fields{};
        if (!SafeReadSliderFields(settingObject, &fields, &code)) {
            LogFailed("reading the CSliderSetting's fields", code);
            return true;
        }
        char detail[192];
        std::snprintf(detail, sizeof(detail),
                      "FcsePage: %s -> setting=0x%08X widget=0x%08X element=0x%08X", slotParam,
                      reinterpret_cast<unsigned>(settingObject), fields.widget, fields.element);
        Log::Loader(detail);

        if (fields.widget == 0 || fields.element == 0) {
            Log::Loader(std::string("FcsePage: ") + slotParam +
                        " did not bind a slider widget - the row has no control, so it is left "
                        "unseeded, unread and hidden");
            return true;
        }

        if (!SafeSetSettingValue(settingObject, static_cast<uint32_t>(setting->value.asSlider),
                                 &code)) {
            LogFailed("CSliderSetting::SetValue", code);
            return true;
        }
        ShowSlotCell(row, CellKind::Slider);
        LiveRows().push_back({setting, settingObject});
        return true;
    }

    // The escape hatch, off by default: a plain button whose label carries the value, which is what
    // the Mod Configuration Menu shipped with for months. It asks nothing of the engine beyond
    // AddButton, so it is the thing to fall back to if a native control ever misbehaves on a build
    // this was not tested against. Only a Checkbox is clickable here - cycling a Choice or dragging
    // a Slider is what the native controls are for, and a fallback that half-works would be worse
    // than one that plainly shows the value and sends the player to fcse.ini.
    bool AppendPlainRow(void* page, SettingsRegistry::Setting* setting) {
        std::wstring text = L"   " + WidenAscii(setting->name) + L"   ";
        void* handler = nullptr;
        switch (setting->value.type) {
        case FCSE_SettingType_Checkbox:
            text += setting->value.asCheckbox ? kOnSuffix : kOffSuffix;
            handler = MenuItemHandler<TogglePayload>::Create({setting});
            break;
        case FCSE_SettingType_Choice:
            text += L"[" +
                    WidenAscii(setting->value.asChoice < setting->choices.size()
                                   ? setting->choices[setting->value.asChoice]
                                   : std::string("?")) +
                    L"]";
            break;
        case FCSE_SettingType_Slider:
            text += L"[" + std::to_wstring(setting->value.asSlider) + L"]";
            break;
        case FCSE_SettingType_Text:
            text += L"[" + WidenAscii(setting->text) + L"]";
            break;
        }

        LabelStorage().push_back(text);
        DWORD code = 0;
        if (!SafeAddButton(page, LabelStorage().back().c_str(), handler, &code)) {
            LogFailed("AddButton (plain row)", code);
            return false;
        }
        return true;
    }

    void AppendPluginBlock(void* page, const std::string& displayName,
                           const SettingsRegistry::Group* group, size_t* row) {
        AppendCaption(page, L"Plugin: " + WidenAscii(displayName), row);

        if (group == nullptr || group->settings.empty()) {
            AppendCaption(page, L"   (no settings)", row);
            return;
        }

        for (const std::unique_ptr<SettingsRegistry::Setting>& setting : group->settings) {
            if (*row >= kSlotCount) {
                // The layout declares exactly kSlotCount value widgets. Past that the lookup would
                // miss and the row would appear with no control at all, which is worse than an
                // honest message.
                Log::Loader("FcsePage: out of value slots (" + std::to_string(kSlotCount) +
                            "), skipping \"" + setting->name + "\" and anything after it");
                return;
            }

            // Both slot banks are indexed by *row*, not by setting: their widgets are absolutely
            // positioned siblings at the nth row's y coordinate, so a caption row consumes an index
            // exactly like a settings row does. Every row has one cell of each kind authored at its
            // position, because a row's type is not known until a plugin registers; binding one
            // leaves the other unused, and unused is invisible for both.
            char slotParam[kSlotParamMax];
            SlotParamName(*row, CellKind::Value, slotParam);
            char sliderSlotParam[kSlotParamMax];
            SlotParamName(*row, CellKind::Slider, sliderSlotParam);

            bool added = false;
            if (g_plainRows) {
                added = AppendPlainRow(page, setting.get());
            } else {
                switch (setting->value.type) {
                case FCSE_SettingType_Checkbox:
                    added = AppendCheckboxRow(page, setting.get(), slotParam, *row);
                    break;
                case FCSE_SettingType_Choice:
                    added = AppendChoiceRow(page, setting.get(), slotParam, *row);
                    break;
                case FCSE_SettingType_Slider:
                    added = AppendSliderRow(page, setting.get(), sliderSlotParam, *row);
                    break;
                case FCSE_SettingType_Text:
                    // No slot cell: a string has no CUISettingBase behind it, so this is a plain
                    // button showing its value, and clicking it opens the game's own text prompt.
                    added = AppendTextRow(page, setting.get(), *row);
                    break;
                default:
                    AppendCaption(page, L"   " + WidenAscii(setting->name) + L" (unsupported type)",
                                  row);
                    continue;
                }
            }

            if (!added) {
                return; // the row list is in an unknown state; appending more would compound it
            }
            ++*row;
        }
    }

}
}
