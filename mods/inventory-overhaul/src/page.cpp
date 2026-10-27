// What the Inventory page shows, read from the player's inventory, Items and wallet, and written into
// the elements build-ui.ps1 exports as IO_*. The page is written again only when that changes.
#include "page.h"

#include "engine/economy.h"
#include "engine/inventory.h"
#include "engine/magma.h"
#include "fcse_api.h"
#include "items.h"
#include "notebook.h"
#include "two_guns.h"

#include <algorithm>
#include <cctype>
#include <cmath>
#include <string>
#include <vector>

namespace InventoryOverhaul::Page {

namespace {
    // A gun as the shop and the multiplayer loadout name it: the picture it has as
    // gun_icon_<picture>.png, its names, class and slot, and its five loadout ratings from 0 to 5,
    // none below zero.
    struct Gun {
        const char* archetype;
        const char* picture;
        const wchar_t* name;
        const wchar_t* shortName;
        const wchar_t* kind;
        const wchar_t* slot;
        float ratings[5];
    };

    // Matched against the start of the archetype's last part, in lower case without underscores.
    constexpr Gun kGuns[] = {
        {"ak47", "ak47", L"AK-47", L"AK-47", L"Assault", L"primary", {3, 4, 3, 5, 4}},
        {"as50", "as50", L"AS50", L"AS50", L"Sniper", L"primary", {5, 5, 3, 3, 3}},
        {"dragunov", "dragunov", L"Dragunov SVD", L"Dragunov", L"Sniper", L"primary", {3, 4, 4, 4, 4}},
        {"fnfal", "fnfal", L"FAL Paratrooper", L"FAL", L"Assault", L"primary", {3, 4, 4, 4, 4}},
        {"g3ka4", "g3ka4", L"G3-KA4", L"G3-KA4", L"Assault", L"primary", {3, 3, 4, 4, 3}},
        {"ithaca", "ithaca", L"Homeland 37", L"Homeland 37", L"Shotgun", L"primary", {4, 3, 3, 2.5f, 2}},
        {"m16", "m16", L"AR-16", L"AR-16", L"Assault", L"primary", {4, 4, 4, 3.5f, 3}},
        {"m1903", "m1903", L"M1903 Sniper Rifle", L"M1903", L"Sniper", L"special", {4, 4, 5, 2.5f, 1}},
        {"mgl140", "mgl40", L"MGL-140", L"MGL-140", L"G-Launcher", L"primary", {4, 4, 3, 2.5f, 3}},
        {"mp5", "mp5sd", L"Silent MP-5", L"MP-5", L"Assault", L"primary", {2, 3, 3, 4, 4}},
        {"spas12", "spas12", L"SPAS-12", L"SPAS-12", L"Shotgun", L"primary", {5, 2, 1, 3.5f, 3}},
        {"usas12", "usas", L"USAS-12", L"USAS-12", L"Shotgun", L"primary", {4, 2, 2, 4, 5}},
        {"silencedmakarov", "6p9", L"Silent Makarov 6P9", L"6P9", L"Pistol", L"secondary", {2, 2, 3, 3, 4}},
        {"deserteagle", "deagle", L"Eagle.50", L"Eagle.50", L"Pistol", L"secondary", {4, 3, 3, 2.5f, 2}},
        {"flaregun", "flare", L"Flare Pistol", L"Flare Pistol", L"Flare", L"secondary", {3, 5, 1, 1.5f, 1}},
        {"m79", "m79", L"M-79 Grenade Launcher", L"M-79", L"G-Launcher", L"secondary", {4, 3, 2, 2, 2}},
        {"mac10", "mac10", L"MAC-10", L"MAC-10", L"SMG", L"secondary", {2, 2, 2, 2.5f, 5}},
        {"makarov", "makarov", L"Makarov", L"Makarov", L"Pistol", L"secondary", {2, 2, 3, 2.5f, 4}},
        {"star45", "p45", L"Star .45", L"Star .45", L"Pistol", L"secondary", {3, 2, 3, 3.5f, 3}},
        {"uzi", "uzi", L"Uzi", L"Uzi", L"SMG", L"secondary", {2, 2, 3, 3, 4}},
        {"carlgustaf", "carlgust", L"Carl G Rocket Launcher", L"Carl G", L"R-Launcher", L"special", {5, 4, 5, 3.5f, 1}},
        {"dartrifle", "sniperdart", L"Dart Rifle", L"Dart Rifle", L"Sniper", L"special", {5, 4, 5, 3.5f, 1}},
        {"lpo50", "lpo50", L"LPO-50 Flamethrower", L"LPO-50", L"Flamethrower", L"special", {3, 1, 5, 3.5f, 5}},
        {"m249", "m249", L"M-249 SAW", L"M-249", L"LMG", L"special", {4, 4, 4, 3, 4}},
        {"mortar", "mortar", L"Type 63 Mortar", L"Mortar", L"Mortar", L"special", {-1, -1, -1, -1, -1}},
        {"pkm", "rpk", L"PKM", L"PKM", L"LMG", L"special", {3, 4, 3, 3.5f, 3}},
        {"rpg7", "rpg7", L"RPG-7 Rocket Launcher", L"RPG-7", L"R-Launcher", L"special", {5, 4, 3, 4, 1}},
        {"ied", "ied", L"IEDs", L"IED", L"Explosive", L"special", {5, 0, 0, 5, 0}},
    };

    // The pictures a gun's row and the post-it have an image for, as build-ui.ps1 makes them; the
    // post-it has the machete's too.
    constexpr const char* kPictures[] = {
        "6p9", "ak47", "as50", "carlgust", "deagle", "dragunov", "flare", "fnfal", "g3ka4", "ied",
        "ithaca", "lpo50", "m16", "m1903", "m249", "m79", "mac10", "makarov", "mgl40", "mp5sd",
        "p45", "rpg7", "rpk", "sniperdart", "spas12", "usas", "uzi", "mortar"};
    constexpr const char* kMachete = "machete";

    constexpr size_t kItemRows = 8;
    constexpr int kRatings = 5;
    constexpr int kHalfSteps = 10;

    // A gun's condition is only ever a word, the HUD element IO_..._<WORD> in its colour: the engine
    // counts wear from 1 to 0.
    constexpr const char* kConditions[] = {"NEW", "USED", "WORN", "FAILING"};

    const char* ConditionOf(const Inventory::Weapon* gun) {
        if (gun == nullptr) {
            return nullptr;
        }
        const float reliability = Inventory::Reliability(gun->weapon);
        return reliability >= 0.75f ? kConditions[0]
               : reliability >= 0.5f ? kConditions[1]
               : reliability >= 0.25f ? kConditions[2]
                                      : kConditions[3];
    }

    // "weapons.Special.M249_Saw" is m249saw.
    std::string Key(const std::string& archetype) {
        std::string key;
        for (char c : archetype.substr(archetype.find_last_of('.') + 1)) {
            if (c != '_') {
                key += static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
            }
        }
        return key;
    }

    const Gun* GunOf(const std::string& archetype) {
        const std::string key = Key(archetype);
        for (const Gun& gun : kGuns) {
            if (key.rfind(gun.archetype, 0) == 0) {
                return &gun;
            }
        }
        return nullptr;
    }

    // The archetype's last part with spaces for underscores, for a gun the book does not know.
    std::wstring PlainName(const std::string& archetype) {
        std::string name = archetype.substr(archetype.find_last_of('.') + 1);
        std::replace(name.begin(), name.end(), '_', ' ');
        return std::wstring(name.begin(), name.end());
    }

    std::string Upper(const char* text) {
        std::string upper;
        for (; *text != '\0'; ++text) {
            upper += static_cast<char>(std::toupper(static_cast<unsigned char>(*text)));
        }
        return upper;
    }

    // Each element's text, and whether each element is shown, as the page wants them.
    struct Model {
        std::vector<std::pair<std::string, std::wstring>> texts;
        std::vector<std::pair<std::string, bool>> shown;

        void Text(const std::string& name, std::wstring text) { texts.emplace_back(name, std::move(text)); }
        void Show(const std::string& name, bool visible) { shown.emplace_back(name, visible); }

        // Everything the page shows, so that it is written again only when this changes.
        std::wstring Key() const {
            std::wstring key;
            for (const auto& [name, text] : texts) {
                key += text + L'\n';
            }
            for (const auto& [name, visible] : shown) {
                key += visible ? L'1' : L'0';
            }
            return key;
        }
    };

    void Condition(Model& model, const std::string& prefix, const char* condition) {
        for (const char* each : kConditions) {
            model.Show(prefix + each, each == condition);
        }
    }

    void Picture(Model& model, const std::string& prefix, const char* each, const char* picture) {
        model.Show(prefix + Upper(each), picture != nullptr && std::string(each) == picture);
    }

    void Pictures(Model& model, const std::string& prefix, const char* picture) {
        for (const char* each : kPictures) {
            Picture(model, prefix, each, picture);
        }
    }

    std::string Name(const char* prefix, size_t index, const char* suffix = "") {
        return prefix + std::to_string(index) + suffix;
    }

    void Hand(Model& model, int slot, const Inventory::Weapon* gun, bool inHand) {
        const std::string row = Name("IO_HAND", slot);
        const std::string archetype = gun != nullptr ? Inventory::Name(gun->weapon) : "";
        const Gun* known = gun != nullptr ? GunOf(archetype) : nullptr;
        Pictures(model, row + "_PIC_", known != nullptr ? known->picture : nullptr);
        model.Text(row + "_NAME", gun == nullptr ? L"" : known != nullptr ? known->shortName : PlainName(archetype));
        model.Show(row + "_EMPTY", gun == nullptr);
        Condition(model, row + "_", ConditionOf(gun));
        model.Show(row + "_SEL", inHand);
    }

    void PostIt(Model& model, const void* view, void* inventory, const Inventory::Weapon* gun) {
        const std::string archetype = gun != nullptr ? Inventory::Name(gun->weapon) : "";
        const Gun* known = gun != nullptr ? GunOf(archetype) : nullptr;
        Pictures(model, "IO_POSTIT_PIC_", known != nullptr ? known->picture : nullptr);
        Picture(model, "IO_POSTIT_PIC_", kMachete, nullptr);
        for (int rating = 0; rating < kRatings; ++rating) {
            const float value = known != nullptr ? known->ratings[rating] : -1.0f;
            const int shown = value > 0 ? static_cast<int>(std::lround(value * 2)) : 0;
            for (int half = 1; half <= kHalfSteps; ++half) {
                model.Show(Name("IO_POSTIT_RATING", rating + 1, "_") + std::to_string(half), half == shown);
            }
        }
        Condition(model, "IO_POSTIT_", ConditionOf(gun));
        if (gun == nullptr) {
            for (const char* name : {"IO_POSTIT_NAME", "IO_POSTIT_SUB", "IO_POSTIT_AMMO"}) {
                model.Text(name, L"");
            }
            return;
        }
        const int slot = TwoGuns::SlotOf(gun->weapon);
        std::wstring where = L"gun " + std::to_wstring(slot);
        if (Inventory::Id(gun->item) == Inventory::InHandId(inventory)) {
            where += L", in hand";
        }
        model.Text("IO_POSTIT_NAME", known != nullptr ? known->name : PlainName(archetype));
        model.Text("IO_POSTIT_SUB", known != nullptr ? std::wstring(known->kind) + L" \x2022 " + known->slot +
                                                           L" \x2022 " + where
                                                     : where);
        model.Text("IO_POSTIT_AMMO", std::to_wstring(Inventory::Loaded(inventory, gun->weapon)) + L" / " +
                                         std::to_wstring(Inventory::SpareRounds(view, gun->weapon)));
    }

    Model Build(const void* view, void* inventory) {
        Model model;
        const std::vector<Inventory::Weapon> guns = Inventory::Guns(inventory);
        const Inventory::Weapon* inHand = nullptr;
        for (const Inventory::Weapon& gun : guns) {
            if (Inventory::Id(gun.item) == Inventory::InHandId(inventory)) {
                inHand = &gun;
            }
        }
        for (int slot = 1; slot <= 2; ++slot) {
            const Inventory::Weapon* gun = TwoGuns::InSlot(guns, slot);
            Hand(model, slot, gun, gun != nullptr && gun == inHand);
        }
        model.Show("IO_MACHETE_SEL", false);
        model.Show("IO_MACHETE_MARK", false);

        std::vector<std::wstring> items;
        for (const Items::Record& item : Items::Load()) {
            const Gun* known = item.gun ? GunOf(item.id) : nullptr;
            std::wstring text = known != nullptr ? known->shortName
                                : item.gun        ? PlainName(item.id)
                                                  : std::wstring(item.id.begin(), item.id.end());
            if (!item.gun && item.count > 1) {
                text += L"  \x00D7" + std::to_wstring(item.count);
            }
            items.push_back(std::move(text));
        }
        if (const int diamonds = Economy::Diamonds(); diamonds > 0) {
            items.push_back(std::to_wstring(diamonds) + L" diamonds");
        }
        for (size_t row = 0; row < kItemRows; ++row) {
            model.Text(Name("IO_ITEM", row + 1), row < items.size() ? items[row] : L"");
        }
        model.Text("IO_ITEMS_MORE", items.size() > kItemRows
                                        ? L"+ " + std::to_wstring(items.size() - kItemRows) + L" more"
                                        : L"");

        PostIt(model, view, inventory, inHand != nullptr ? inHand : guns.empty() ? nullptr : &guns.front());
        return model;
    }

    // The page's elements as last found, and what was last written into them.
    void* g_notebook = nullptr;
    bool g_open = false;
    std::wstring g_shown;
}

bool Install() {
    const bool found = Magma::Install();
    FCSE::Logf("page: %s", found ? "on the HUD" : "off, the HUD's Magma calls were not found");
    return found;
}

void Update(const void* view, void* inventory) {
    const Model model = Build(view, inventory);
    std::wstring key = model.Key();
    if (key == g_shown) {
        return;
    }
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
    if (g_shown.empty()) {
        FCSE::Logf("page: written, %zu texts and %zu shown or hidden, %zu of them not on the HUD",
                   model.texts.size(), model.shown.size(), missing);
    }
    g_shown = std::move(key);
}

void Frame() {
    void* notebook = Magma::Find("IO_NOTEBOOK");
    const bool open = Notebook::IsOpen();
    if (notebook == g_notebook && open == g_open) {
        return;
    }
    if (notebook != g_notebook) {
        FCSE::Logf("page: the HUD's notebook is %p", notebook);
        // A HUD loaded since holds what its package says, not what was written.
        g_shown.clear();
    }
    if (notebook != nullptr) {
        Magma::SetVisible(notebook, open);
    }
    g_notebook = notebook;
    g_open = open;
}

}
