// The notebook's pages, each filling a Model from the game: the parts they share, and each page's
// builder.
#pragma once

#include <cstddef>
#include <map>
#include <string>
#include <utility>
#include <vector>

namespace InventoryOverhaul::Page {

// Each element's text, whether it is shown, and the element whose picture it takes, as a page wants
// them; a later write to an element replaces an earlier one. `heals`: a click heals, as the heal key
// does, instead of coming to the page.
struct Model {
    std::map<std::string, std::wstring> texts;
    std::map<std::string, bool> shown;
    std::map<std::string, std::string> pictures;
    bool heals = false;

    void Text(const std::string& name, std::wstring text) { texts[name] = std::move(text); }
    void Show(const std::string& name, bool visible) { shown[name] = visible; }
    // Shows the image `name` with the picture of `from`.
    void Picture(const std::string& name, std::string from) {
        shown[name] = true;
        pictures[name] = std::move(from);
    }

    // Everything the page shows, so that it is written again only when this changes.
    std::wstring Key() const;
};

// Every gun picture the post-it has, as build-ui.ps1 makes them.
inline constexpr const char* kGunPictures[] = {
    "6p9", "ak47", "as50", "carlgust", "deagle", "dragunov", "flare", "fnfal", "g3ka4", "ied",
    "ithaca", "lpo50", "m16", "m1903", "m249", "m79", "mac10", "makarov", "mgl40", "mp5sd",
    "p45", "rpg7", "rpk", "sniperdart", "spas12", "usas", "uzi", "mortar"};
inline constexpr int kRatings = 5;

std::string Name(const std::string& prefix, size_t index, const char* suffix = "");
std::string Upper(const std::string& text);
// UTF-8 text as the HUD's.
std::wstring Wide(const std::string& text);
// An id's last part with spaces for underscores: a gun the tables do not know, or an item.
std::wstring PlainName(const std::string& id);

// A gun's wear, 1 to 0, as a condition word: new, used, worn or failing.
const wchar_t* Condition(float reliability);

// Fills IO_<LIST>1... with a window of `lines` of `rows` from `first`, kept on row `selected` if it
// is one of them, and IO_<LIST>S_ABOVE and _MORE with the rows outside it. Returns the selected row's
// line, from 1, or 0 when it is none of them.
size_t List(Model& model, const std::string& list, size_t lines, size_t& first, const std::vector<std::wstring>& rows,
            int selected);

// Everything on the post-it hidden or emptied, for the selected row to fill in.
void ClearPostIt(Model& model);

// Each page's rows and the post-it of row `selected`, kept within them; each returns its row count.
// `dropped`, X was pressed: the selected gun falls at the feet, a stowed one too, and one of the
// selected item is thrown away. `used`, the page was clicked: gun 1 or 2 is stowed in Items, and a
// stowed gun's menu opened, or its action run. `menu` is the menu's action underlined, -1 while it is
// closed.
int InventoryPage(Model& model, int& selected, const void* view, void* inventory, bool dropped, bool used,
                  int& menu);
// `used`, the page was clicked: the selected note or photo is read whole, or put back, and the selected
// tape or cassette played, or stopped while it plays. `sheet` is the sheet read, kept within the
// paper's, or -1 while none is.
int PapersPage(Model& model, int& selected, bool used, int& sheet);
void JournalPage(Model& model);
int ContactsPage(Model& model, int& selected);

// Whether an Items record is a note, photo or cassette, which the Papers page lists instead of Items.
bool IsPaper(const std::string& id);

}
