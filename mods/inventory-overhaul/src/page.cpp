// The notebook's pages and what they share, written into the elements build-ui.ps1 exports as IO_*
// only when what they show changes.
#include "page.h"

#include "engine/jobs.h"
#include "engine/magma.h"
#include "engine/paper.h"
#include "engine/sound.h"
#include "engine/story.h"
#include "fcse_api.h"
#include "notebook.h"
#include "pages.h"

#include <algorithm>
#include <atomic>
#include <cctype>
#include <chrono>
#include <iterator>
#include <utility>

namespace InventoryOverhaul::Page {

namespace {
    // The pages, in the order R turns to them, by the name of their area and index tab.
    enum Tab { kInventory, kPapers, kJournal, kContacts, kTabs };
    constexpr const char* kTabNames[] = {"INVENTORY", "PAPERS", "JOURNAL", "CONTACTS"};

    // The post-it's pictures besides the guns', as build-ui.ps1 makes them.
    constexpr const char* kOtherPictures[] = {"machete", "diamond", "seringe", "pills", "m67", "molotov",
                                              "jackal_photo", "jackal_definition", "apr_01", "ufll_01", "photo05",
                                              "tape"};
    // The upgrade icon rows: a gun's three on the third line, a belt item's two on the second.
    constexpr std::pair<const char*, int> kUpgradeRows[] = {{"IO_POSTIT_UP", 3}, {"IO_POSTIT_BELT_UP", 2}};
    constexpr int kHalfSteps = 10;

    // The page open, R's turns, the wheel's steps and whether it was clicked or X pressed since the
    // last write, and each page's selected row; the Inventory page opens on the gun in hand. The sheet
    // of the paper read and the action underlined in a stowed gun's menu, -1 while neither is open.
    int g_tab = kInventory;
    std::atomic<int> g_turns{0};
    std::atomic<int> g_steps{0};
    std::atomic<bool> g_used{false};
    std::atomic<bool> g_dropped{false};
    int g_selected[kTabs] = {-1, 0, 0, 0};
    std::atomic<int> g_sheet{-1};
    std::atomic<int> g_menu{-1};
    std::atomic<bool> g_heals{false};

    // The page's elements as last found, and what was last written into them.
    void* g_notebook = nullptr;
    bool g_open = false;
    std::wstring g_shown;

    // The pause menu's own page flip, common.mgb's area, shared with the pause menu: its time is set
    // from here at twice its speed up to the frame its images have faded out by, without its
    // keyframes' actions, so its sound, as its SoundEvent names it, is played here and faded out to
    // end at half its length.
    constexpr float kFlipSpeed = 2.0f;
    constexpr int kFlipLastFrame = 16;
    constexpr uint32_t kFlipSound = 0x004EBEBC;
    constexpr int32_t kMenuSound = 20;
    constexpr float kFlipSoundPart = 0.5f;
    constexpr float kFlipSoundFade = 0.03f;
    std::chrono::steady_clock::time_point g_flipStart;
    bool g_flipping = false;
    uint32_t g_flipSound = Sound::kNone;

    void StartFlip() {
        g_flipStart = std::chrono::steady_clock::now();
        g_flipping = true;
        Sound::Stop(g_flipSound, kFlipSoundFade);
        g_flipSound = Sound::Play(kFlipSound, kMenuSound);
    }

    void Flip() {
        const float seconds = std::chrono::duration<float>(std::chrono::steady_clock::now() - g_flipStart).count();
        if (g_flipSound != Sound::kNone && seconds >= Sound::Length(kFlipSound) * kFlipSoundPart - kFlipSoundFade) {
            Sound::Stop(std::exchange(g_flipSound, Sound::kNone), kFlipSoundFade);
        }
        void* flip = g_flipping ? Magma::Find("IO_FLIP") : nullptr;
        void* area = flip != nullptr ? Magma::Find("IO_FLIP_AREA") : nullptr;
        if (area == nullptr) {
            g_flipping = false;
            return;
        }
        const uint32_t last = Magma::TimePerFrame(area) * kFlipLastFrame;
        const auto time = static_cast<uint32_t>(seconds * 1000.0f * kFlipSpeed);
        g_flipping = time < last;
        Magma::SetTime(area, std::min(time, last));
        Magma::SetVisible(flip, g_flipping);
    }

    // Writes everything; a picture is taken from an element of the same package, found by name.
    size_t Write(const Model& model) {
        size_t missing = 0;
        for (const auto& [name, text] : model.texts) {
            if (void* element = Magma::Find(name.c_str())) {
                Magma::SetText(element, text.c_str());
            } else {
                ++missing;
            }
        }
        for (const auto& [name, visible] : model.shown) {
            if (void* element = Magma::Find(name.c_str())) {
                Magma::SetVisible(element, visible);
            } else {
                ++missing;
            }
        }
        for (const auto& [name, from] : model.pictures) {
            void* element = Magma::Find(name.c_str());
            const void* source = Magma::Find(from.c_str());
            if (element != nullptr && source != nullptr) {
                Magma::SetPicture(element, source);
            } else {
                ++missing;
            }
        }
        return missing;
    }
}

std::wstring Model::Key() const {
    std::wstring key;
    for (const auto& [name, text] : texts) {
        key += text;
        key += L'\n';
    }
    for (const auto& [name, visible] : shown) {
        key += visible ? L'1' : L'0';
    }
    for (const auto& [name, from] : pictures) {
        key.append(from.begin(), from.end());
        key += L'\n';
    }
    return key;
}

std::string Name(const std::string& prefix, size_t index, const char* suffix) {
    return prefix + std::to_string(index) + suffix;
}

std::string Upper(const std::string& text) {
    std::string upper = text;
    for (char& c : upper) {
        c = static_cast<char>(std::toupper(static_cast<unsigned char>(c)));
    }
    return upper;
}

std::wstring Wide(const std::string& text) { return std::wstring(text.begin(), text.end()); }

std::wstring PlainName(const std::string& id) {
    std::string name = id.substr(id.find_last_of('.') + 1);
    std::replace(name.begin(), name.end(), '_', ' ');
    return Wide(name);
}

const wchar_t* Condition(float reliability) {
    return reliability >= 0.75f ? L"new" : reliability >= 0.5f ? L"used" : reliability >= 0.25f ? L"worn" : L"failing";
}

void List(Model& model, const std::string& list, size_t lines, size_t& first, const std::vector<std::wstring>& rows,
          int selected) {
    const std::string prefix = "IO_" + list;
    if (selected >= 0 && static_cast<size_t>(selected) < rows.size()) {
        const auto index = static_cast<size_t>(selected);
        first = std::clamp(first, index + 1 >= lines ? index + 1 - lines : 0, index);
    }
    first = std::min(first, rows.size() > lines ? rows.size() - lines : 0);
    for (size_t line = 0; line < lines; ++line) {
        const size_t index = first + line;
        const bool isSelected = index < rows.size() && static_cast<int>(index) == selected;
        model.Text(Name(prefix, line + 1), index < rows.size() ? rows[index] : L"");
        model.Show(Name(prefix, line + 1, "_SEL"), isSelected);
    }
    const size_t below = rows.size() > first + lines ? rows.size() - first - lines : 0;
    model.Text(prefix + "S_ABOVE", first > 0 ? std::to_wstring(first) + L" above" : L"");
    model.Show(prefix + "S_ABOVE_ARROW", first > 0);
    model.Text(prefix + "S_MORE", below > 0 ? std::to_wstring(below) + L" more" : L"");
    model.Show(prefix + "S_MORE_ARROW", below > 0);
}

void ClearPostIt(Model& model) {
    for (const char* picture : kGunPictures) {
        model.Show("IO_POSTIT_PIC_" + Upper(picture), false);
    }
    for (const char* picture : kOtherPictures) {
        model.Show("IO_POSTIT_PIC_" + Upper(picture), false);
    }
    model.Show("IO_POSTIT_PORTRAIT", false);
    for (const char* name : {"IO_POSTIT_NAME", "IO_POSTIT_SUB", "IO_POSTIT_LINE1", "IO_POSTIT_LINE2",
                             "IO_POSTIT_LINE3", "IO_POSTIT_VALUE1", "IO_POSTIT_VALUE2", "IO_POSTIT_STATE",
                             "IO_POSTIT_QUOTE", "IO_POSTIT_NOTE", "IO_POSTIT_ABOUT", "IO_POSTIT_STATUS"}) {
        model.Text(name, L"");
    }
    for (const auto& [icons, count] : kUpgradeRows) {
        for (int slot = 0; slot < count; ++slot) {
            model.Show(Name(icons, slot, "_FAINT"), false);
            model.Show(Name(icons, slot, "_BOUGHT"), false);
        }
    }
    for (int rating = 1; rating <= kRatings; ++rating) {
        model.Show(Name("IO_POSTIT_RATING", rating), false);
        model.Show(Name("IO_POSTIT_RATING", rating, "_TRACK"), false);
        for (int half = 1; half <= kHalfSteps; ++half) {
            model.Show(Name("IO_POSTIT_RATING", rating, "_") + std::to_string(half), false);
        }
    }
}

bool Install() {
    const bool magma = Magma::Install();
    const bool story = magma && Story::Install();
    FCSE::Logf("page: %s", !magma ? "off, the HUD's Magma calls were not found"
                           : story ? "on the HUD"
                                   : "on the HUD, but the story's services were not found: Papers, Journal and "
                                     "Contacts stay empty");
    if (magma && !Sound::Install()) {
        FCSE::Logf("page: the sound system calls were not found, so a turn is silent");
    }
    if (magma && !Jobs::Install()) {
        FCSE::Logf("page: the objective markers' calls were not found, so the Journal shows no job");
    }
    return magma;
}

void Update(const void* view, void* inventory) {
    Sound::Hold(kFlipSound);
    const int turns = g_turns.exchange(0);
    g_tab = (g_tab + turns) % kTabs;
    int sheet = turns > 0 ? -1 : g_sheet.load();
    int menu = turns > 0 ? -1 : g_menu.load();
    if (turns > 0) {
        StartFlip();
    }
    int& selected = g_selected[g_tab];
    const int steps = g_steps.exchange(0);
    const bool used = g_used.exchange(false);
    const bool dropped = g_dropped.exchange(false);
    if (sheet >= 0) {
        sheet = std::max(sheet + steps, 0);
    } else if (menu >= 0) {
        menu = std::clamp(menu + steps, 0, kMenuActions - 1);
    } else if (selected >= 0) {
        selected += steps;
    }

    Model model;
    for (int tab = 0; tab < kTabs; ++tab) {
        const std::string name = kTabNames[tab];
        model.Show("IO_" + name, tab == g_tab);
        // Each tab has an open look, pulled up, and a closed one.
        model.Show("IO_OPEN_" + name, tab == g_tab);
        model.Show("IO_OPEN_" + name + "_LABEL", tab == g_tab);
        model.Show("IO_TAB_" + name, tab != g_tab);
        model.Show("IO_TAB_" + name + "_LABEL", tab != g_tab);
    }
    // The portraits a buddy's picture is taken from.
    model.Show("IO_PORTRAITS", false);
    ClearPostIt(model);
    int rows = 0;
    switch (g_tab) {
    case kInventory:
        rows = InventoryPage(model, selected, view, inventory, dropped, used, menu);
        break;
    case kPapers:
        rows = PapersPage(model, selected, used, sheet);
        break;
    case kJournal:
        JournalPage(model);
        break;
    case kContacts:
        rows = ContactsPage(model, selected);
        break;
    }
    g_sheet = sheet;
    g_menu = menu;
    g_heals = model.heals;
    model.Show("IO_POSTIT", rows > 0 && sheet < 0);
    model.Show("IO_READER", sheet >= 0);

    std::wstring key = model.Key();
    if (key == g_shown) {
        return;
    }
    const size_t missing = Write(model);
    if (g_shown.empty()) {
        FCSE::Logf("page: written, %zu texts, %zu shown or hidden and %zu pictures, %zu of them not on the HUD",
                   model.texts.size(), model.shown.size(), model.pictures.size(), missing);
    }
    g_shown = std::move(key);
}

void Scroll(int steps) { g_steps += steps; }

void Turn() { ++g_turns; }

void Use() { g_used = true; }

void Drop() { g_dropped = true; }

bool Close() {
    const bool read = g_sheet.exchange(-1) >= 0;
    const bool menu = g_menu.exchange(-1) >= 0;
    return read || menu;
}

bool Heals() { return g_heals; }

void Frame() {
    Flip();
    void* notebook = Magma::Find("IO_NOTEBOOK");
    const bool open = Notebook::IsOpen();
    if (notebook == g_notebook && open == g_open) {
        return;
    }
    if (notebook != g_notebook) {
        FCSE::Logf("page: the HUD's notebook is %p", notebook);
        // A HUD loaded since holds what its package says, not what was written.
        g_shown.clear();
        Paper::NewHud();
    }
    if (notebook != nullptr) {
        Magma::SetVisible(notebook, open);
    }
    if (!open) {
        Close();
    }
    g_notebook = notebook;
    g_open = open;
}

}
