// The notebook's pages, each filling a Model from the game: the parts they share, and each page's
// builder.
#pragma once

#include <cstddef>
#include <map>
#include <string>
#include <vector>

namespace InventoryOverhaul::Page {

// Each element's text, whether it is shown, and the element whose picture it takes, as a page wants
// them; a later write to an element replaces an earlier one.
struct Model {
    std::map<std::string, std::wstring> texts;
    std::map<std::string, bool> shown;
    std::map<std::string, std::string> pictures;

    void Text(const std::string& name, std::wstring text) { texts[name] = std::move(text); }
    void Show(const std::string& name, bool visible) { shown[name] = visible; }
    void Picture(const std::string& name, std::string from) { pictures[name] = std::move(from); }

    // Everything the page shows, so that it is written again only when this changes.
    std::wstring Key() const;
};

// Every gun picture a hand row and the post-it have, as build-ui.ps1 makes them.
inline constexpr const char* kGunPictures[] = {
    "6p9", "ak47", "as50", "carlgust", "deagle", "dragunov", "flare", "fnfal", "g3ka4", "ied",
    "ithaca", "lpo50", "m16", "m1903", "m249", "m79", "mac10", "makarov", "mgl40", "mp5sd",
    "p45", "rpg7", "rpk", "sniperdart", "spas12", "usas", "uzi", "mortar"};
inline constexpr int kRatings = 5;

std::string Name(const std::string& prefix, size_t index, const char* suffix = "");
std::string Upper(const std::string& text);
std::wstring Wide(const std::string& text);
// An id's last part with spaces for underscores: a gun the tables do not know, or an item.
std::wstring PlainName(const std::string& id);

// A gun's wear, 1 to 0, as one condition word shown, IO_<prefix><WORD> in its colour; none below 0.
void Condition(Model& model, const std::string& prefix, float reliability);

// Fills IO_<LIST>1... with a window of `lines` of `rows` from `first`, kept on row `selected` if it
// is one of them, and IO_<LIST>S_ABOVE and _MORE with the rows outside it.
void List(Model& model, const std::string& list, size_t lines, size_t& first, const std::vector<std::wstring>& rows,
          int selected);

// Everything on the post-it hidden or emptied, for the selected row to fill in.
void ClearPostIt(Model& model);

// Each page's rows and the post-it of row `selected`, kept within them; each returns its row count.
int InventoryPage(Model& model, int& selected, const void* view, void* inventory);
int PapersPage(Model& model, int& selected);
void JournalPage(Model& model);
int ContactsPage(Model& model, int& selected);

// Whether an Items record is a paper, which the Papers page lists instead of Items.
bool IsPaper(const std::string& id);

}
