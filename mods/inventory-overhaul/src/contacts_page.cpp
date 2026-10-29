// The Contacts page: the buddies met, the player's primary and secondary buddy first, then the rest,
// those at hand, then the missing, then the dead, as the pause menu's Buddies page knows them.
#include "pages.h"

#include "engine/magma.h"
#include "engine/story.h"

#include <algorithm>
#include <string_view>

namespace InventoryOverhaul::Page {

namespace {
    // The portraits build-ui.ps1 makes, IO_PORTRAIT_<NAME>, are named by the buddy's first name but
    // for two named by the last.
    constexpr std::pair<std::string_view, std::string_view> kLastNamed[] = {{"QUARBANI", "SINGH"},
                                                                            {"XIANYONG", "BAI"}};

    constexpr const char* kBuddies = "Buddies";
    constexpr size_t kActiveLines = 2;
    constexpr size_t kOtherLines = 6;
    size_t g_firstActive = 0;
    size_t g_firstOther = 0;

    std::string FirstName(const std::string& name) { return name.substr(0, name.find('_')); }

    // The palette image whose picture is the buddy's portrait, or empty when there is none.
    std::string PortraitOf(const std::string& name) {
        std::string picture = Upper(FirstName(name));
        for (const auto& [first, last] : kLastNamed) {
            if (picture == first) {
                picture = last;
            }
        }
        const std::string portrait = "IO_PORTRAIT_" + picture;
        return Magma::Find(portrait.c_str()) != nullptr ? portrait : "";
    }

    // A buddy string's value, without its label: "Nationality: Northern Irish" is Northern Irish.
    std::wstring Value(const std::string& name, const char* field) {
        const std::wstring& text = Story::Localize(kBuddies, Upper(name) + field);
        const size_t colon = text.find(L": ");
        return colon != std::wstring::npos ? text.substr(colon + 2) : text;
    }

    // How available a buddy is, in the order the other buddies are listed, and the suffix of the twin
    // texts a row is written in: missing ones dim, dead ones faded italic.
    enum Availability { kAtHand, kMissing, kDead };
    constexpr const char* kStyles[] = {"", "_DIM", "_FADED"};

    Availability AvailabilityOf(const Story::Buddy& buddy) {
        return buddy.status == Story::kDead ? kDead : buddy.status == Story::kMissing ? kMissing : kAtHand;
    }

    // A missing buddy's portrait is its dim twin too.
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
            const Availability style = shown ? AvailabilityOf(*buddies[index]) : kAtHand;
            for (int s = kAtHand; s <= kDead; ++s) {
                model.Text(row + kStyles[s], shown && s == style ? names[index] : L"");
                model.Text(row + "_SIDE" + kStyles[s], shown && s == style ? buddies[index]->statusText : L"");
            }
            const std::string portrait = shown ? PortraitOf(buddies[index]->name) : "";
            model.Show(row + "_PIC", false);
            model.Show(row + "_PIC_DIM", false);
            if (!portrait.empty()) {
                model.Picture(row + (style == kMissing ? "_PIC_DIM" : "_PIC"), portrait);
            }
        }
    }

    void BuddyPostIt(Model& model, const Story::Buddy& buddy) {
        if (const std::string portrait = PortraitOf(buddy.name); !portrait.empty()) {
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
    std::stable_sort(others.begin(), others.end(),
                     [](const auto* a, const auto* b) { return AvailabilityOf(*a) < AvailabilityOf(*b); });
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
