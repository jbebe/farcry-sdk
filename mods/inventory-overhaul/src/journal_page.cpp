// The Journal page: the player's reputation in the words of the pause menu's Journal. Jobs are not
// read from the game yet.
#include "pages.h"

#include "engine/inventory.h"
#include "engine/story.h"

#include <algorithm>

namespace InventoryOverhaul::Page {

void JournalPage(Model& model) {
    const int level = std::max(Inventory::Reputation(), 0);
    std::wstring heading = L"REPUTATION LEVEL " + std::to_wstring(level);
    std::wstring text = Story::Localize("PauseMenu", "REPUTATION_INFAMY_DESCRIPTION_" + std::to_string(level));
    // Above level 0 the game's text opens with its own heading line.
    if (const size_t newline = text.find(L'\n'); newline != std::wstring::npos) {
        heading = text.substr(0, newline);
        text.erase(0, newline + 1);
    }
    model.Text("IO_REPUTATION", std::move(heading));
    model.Text("IO_REPUTATION_PROSE", std::move(text));
}

}
