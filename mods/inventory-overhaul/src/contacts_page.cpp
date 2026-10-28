// The Contacts page: the buddies met, the player's primary and secondary buddy first, then the rest,
// living before dead, as the pause menu's Buddies page knows them.
#include "pages.h"

#include "engine/story.h"

#include <algorithm>
#include <string_view>

namespace InventoryOverhaul::Page {

namespace {
    // The portraits build-ui.ps1 makes, IO_PORTRAIT_<NAME>, named by the buddy's first name but for
    // two named by the last.
    constexpr std::string_view kPortraits[] = {"ANDRE", "FLORA", "FRANK", "HAKIM", "JOSIP", "MARTY",
                                               "MICHELE", "NASREEN", "PAUL", "SINGH", "WARREN", "BAI"};
    constexpr std::pair<std::string_view, std::string_view> kLastNamed[] = {{"QUARBANI", "SINGH"},
                                                                            {"XIANYONG", "BAI"}};

    constexpr const char* kBuddies = "Buddies";
    constexpr size_t kActiveLines = 2;
    constexpr size_t kOtherLines = 6;
    size_t g_firstActive = 0;
    size_t g_firstOther = 0;

    std::string FirstName(const std::string& name) { return name.substr(0, name.find('_')); }

    // The palette image whose picture is the buddy's portrait, or empty.
    std::string PortraitOf(const std::string& name) {
        std::string picture = Upper(FirstName(name));
        for (const auto& [first, last] : kLastNamed) {
            if (picture == first) {
                picture = last;
            }
        }
        const bool made = std::find(std::begin(kPortraits), std::end(kPortraits), picture) != std::end(kPortraits);
        return made ? "IO_PORTRAIT_" + picture : "";
    }

    // A buddy string's value, without its label: "Nationality: Northern Irish" is Northern Irish.
    std::wstring Value(const std::string& name, const char* field) {
        const std::wstring& text = Story::Localize(kBuddies, Upper(name) + field);
        const size_t colon = text.find(L": ");
        return colon != std::wstring::npos ? text.substr(colon + 2) : text;
    }

    // A dead buddy's row is written in its faded texts instead.
    void Buddies(Model& model, const std::string& list, size_t lines, size_t& first,
                 const std::vector<const Story::Buddy*>& buddies, int selected) {
        std::vector<std::wstring> names;
        for (const Story::Buddy* buddy : buddies) {
            names.push_back(Wide(FirstName(buddy->name)));
        }
        List(model, list, lines, first, names, selected);
        for (size_t line = 0; line < lines; ++line) {
            const size_t index = first + line;
            const std::string row = Name("IO_" + list, line + 1);
            const bool shown = index < buddies.size();
            const bool dead = shown && buddies[index]->status == Story::kDead;
            const std::wstring status = shown ? buddies[index]->statusText : L"";
            model.Text(row, shown && !dead ? names[index] : L"");
            model.Text(row + "_FADED", dead ? names[index] : L"");
            model.Text(row + "_SIDE", dead ? L"" : status);
            model.Text(row + "_SIDE_FADED", dead ? status : L"");
            const std::string portrait = shown ? PortraitOf(buddies[index]->name) : "";
            model.Show(row + "_PIC", !portrait.empty());
            if (!portrait.empty()) {
                model.Picture(row + "_PIC", portrait);
            }
        }
    }

    void BuddyPostIt(Model& model, const Story::Buddy& buddy) {
        if (const std::string portrait = PortraitOf(buddy.name); !portrait.empty()) {
            model.Show("IO_POSTIT_PORTRAIT", true);
            model.Picture("IO_POSTIT_PORTRAIT", portrait);
        }
        model.Text("IO_POSTIT_NAME", PlainName(buddy.name));
        model.Text("IO_POSTIT_SUB", Value(buddy.name, "_NATIONALITY") + L", " + Value(buddy.name, "_AGE"));
        model.Text("IO_POSTIT_LINE1", L"Status:");
        model.Text("IO_POSTIT_STATUS", buddy.statusText);
        model.Text("IO_POSTIT_QUOTE", Story::Localize(kBuddies, Upper(buddy.name) + "_DESCRIPTION_1"));
    }
}

int ContactsPage(Model& model, int& selected) {
    const std::vector<Story::Buddy> buddies = Story::Buddies();
    std::vector<const Story::Buddy*> active;
    std::vector<const Story::Buddy*> others;
    for (const Story::Buddy& buddy : buddies) {
        (buddy.active != 0 ? active : others).push_back(&buddy);
    }
    std::sort(active.begin(), active.end(), [](const auto* a, const auto* b) { return a->active < b->active; });
    std::stable_partition(others.begin(), others.end(), [](const auto* buddy) { return buddy->status != Story::kDead; });
    const int rows = static_cast<int>(buddies.size());
    selected = std::clamp(selected, 0, std::max(rows - 1, 0));

    const int other = selected - static_cast<int>(active.size());
    Buddies(model, "ACTIVE", kActiveLines, g_firstActive, active, selected);
    Buddies(model, "OTHER", kOtherLines, g_firstOther, others, other);

    if (other < 0) {
        BuddyPostIt(model, *active[selected]);
    } else if (other < static_cast<int>(others.size())) {
        BuddyPostIt(model, *others[other]);
    }
    return rows;
}

}
