// The Contacts page: the buddies met, those who will pull the player out when they go down and the
// rest, as the pause menu's Buddies page knows them.
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
    constexpr size_t kRescuerLines = 7;
    constexpr size_t kAroundLines = 3;
    size_t g_firstRescuer = 0;
    size_t g_firstAround = 0;

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
            const std::string portrait = index < buddies.size() ? PortraitOf(buddies[index]->name) : "";
            model.Text(row + "_SIDE", index < buddies.size() ? buddies[index]->statusText : L"");
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
        model.Text("IO_POSTIT_LINE1", L"Status");
        model.Text("IO_POSTIT_STATE", buddy.statusText);
        model.Text("IO_POSTIT_QUOTE", Story::Localize(kBuddies, Upper(buddy.name) + "_DESCRIPTION_1"));
    }
}

int ContactsPage(Model& model, int& selected) {
    const std::vector<Story::Buddy> buddies = Story::Buddies();
    std::vector<const Story::Buddy*> rescuers;
    std::vector<const Story::Buddy*> around;
    for (const Story::Buddy& buddy : buddies) {
        (buddy.status == Story::kRescueReady ? rescuers : around).push_back(&buddy);
    }
    const int rows = static_cast<int>(buddies.size());
    selected = std::clamp(selected, 0, std::max(rows - 1, 0));

    // Those who will pull the player out, on the left page, come before the rest on the right.
    const int other = selected - static_cast<int>(rescuers.size());
    Buddies(model, "RESCUER", kRescuerLines, g_firstRescuer, rescuers, selected);
    Buddies(model, "AROUND", kAroundLines, g_firstAround, around, other);

    if (other < 0) {
        BuddyPostIt(model, *rescuers[selected]);
    } else if (other < static_cast<int>(around.size())) {
        BuddyPostIt(model, *around[other]);
    }
    return rows;
}

}
