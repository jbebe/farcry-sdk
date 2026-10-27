// What the Inventory page shows, read from the player's inventory, Items, belt and wallet, and
// written into the elements build-ui.ps1 exports as IO_*. The page is written again only when that
// changes.
#include "page.h"

#include "engine/economy.h"
#include "engine/inventory.h"
#include "engine/magma.h"
#include "fcse_api.h"
#include "items.h"
#include "notebook.h"
#include "two_guns.h"

#include <algorithm>
#include <atomic>
#include <cctype>
#include <cmath>
#include <iterator>
#include <map>
#include <string>
#include <vector>

namespace InventoryOverhaul::Page {

namespace {
    // A gun as the shop and the multiplayer loadout know it: its archetype, its picture
    // gun_icon_<picture>.png, its names, class and slot, its five loadout ratings from 0 to 5 (none
    // below zero), its name in the shop, its ammo type and the bandolier that carries more of it.
    struct Gun {
        const char* archetype;
        const char* picture;
        const wchar_t* name;
        const wchar_t* shortName;
        const wchar_t* kind;
        const wchar_t* slot;
        float ratings[5];
        const char* shop;
        const char* ammo;
        const char* bandolier;
    };

    // Matched against the start of the archetype's last part, in lower case without underscores.
    constexpr Gun kGuns[] = {
        {"ak47", "ak47", L"AK-47", L"AK-47", L"Assault", L"primary", {3, 4, 3, 5, 4}, "ak47", "assaultrifle", "assault webbing"},
        {"as50", "as50", L"AS50", L"AS50", L"Sniper", L"primary", {5, 5, 3, 3, 3}, "as50", "sniperrifle", "marksmans bandolier"},
        {"dragunov", "dragunov", L"Dragunov SVD", L"Dragunov", L"Sniper", L"primary", {3, 4, 4, 4, 4}, "dragunov", "sniperrifle", "marksmans bandolier"},
        {"fnfal", "fnfal", L"FAL Paratrooper", L"FAL", L"Assault", L"primary", {3, 4, 4, 4, 4}, "fnfal", "assaultrifle", "assault webbing"},
        {"g3ka4", "g3ka4", L"G3-KA4", L"G3-KA4", L"Assault", L"primary", {3, 3, 4, 4, 3}, "g3ka4", "assaultrifle", "assault webbing"},
        {"ithaca", "ithaca", L"Homeland 37", L"Homeland 37", L"Shotgun", L"primary", {4, 3, 3, 2.5f, 2}, "ithaca", "shotgun", "shotgun bandolier"},
        {"m16", "m16", L"AR-16", L"AR-16", L"Assault", L"primary", {4, 4, 4, 3.5f, 3}, "m16", "assaultrifle", "assault webbing"},
        {"m1903", "m1903", L"M1903 Sniper Rifle", L"M1903", L"Sniper", L"special", {4, 4, 5, 2.5f, 1}, "m1903", "sniperrifle", "marksmans bandolier"},
        {"mgl140", "mgl40", L"MGL-140", L"MGL-140", L"G-Launcher", L"primary", {4, 4, 3, 2.5f, 3}, "mgl40", "mgl140", "grenadier webbing"},
        {"mp5", "mp5sd", L"Silent MP-5", L"MP-5", L"Assault", L"primary", {2, 3, 3, 4, 4}, "mp5", "assaultrifle", "assault webbing"},
        {"spas12", "spas12", L"SPAS-12", L"SPAS-12", L"Shotgun", L"primary", {5, 2, 1, 3.5f, 3}, "spas12", "shotgun", "shotgun bandolier"},
        {"usas12", "usas", L"USAS-12", L"USAS-12", L"Shotgun", L"primary", {4, 2, 2, 4, 5}, "usas12", "shotgun", "shotgun bandolier"},
        {"silencedmakarov", "6p9", L"Silent Makarov 6P9", L"6P9", L"Pistol", L"secondary", {2, 2, 3, 3, 4}, "6p9", "deserteagle", "pistol belt"},
        {"deserteagle", "deagle", L"Eagle.50", L"Eagle.50", L"Pistol", L"secondary", {4, 3, 3, 2.5f, 2}, "de", "deserteagle", "pistol belt"},
        {"flaregun", "flare", L"Flare Pistol", L"Flare Pistol", L"Flare", L"secondary", {3, 5, 1, 1.5f, 1}, "flare", "flare", "pyrotechnic satchel"},
        {"m79", "m79", L"M-79 Grenade Launcher", L"M-79", L"G-Launcher", L"secondary", {4, 3, 2, 2, 2}, "m79", "m79", "grenadier webbing"},
        {"mac10", "mac10", L"MAC-10", L"MAC-10", L"SMG", L"secondary", {2, 2, 2, 2.5f, 5}, "mac10", "smg", "light assault webbing"},
        {"makarov", "makarov", L"Makarov", L"Makarov", L"Pistol", L"secondary", {2, 2, 3, 2.5f, 4}, "makarov", "deserteagle", "pistol belt"},
        {"star45", "p45", L"Star .45", L"Star .45", L"Pistol", L"secondary", {3, 2, 3, 3.5f, 3}, "star45", "deserteagle", "pistol belt"},
        {"uzi", "uzi", L"Uzi", L"Uzi", L"SMG", L"secondary", {2, 2, 3, 3, 4}, "uzi", "smg", "light assault webbing"},
        {"carlgustaf", "carlgust", L"Carl G Rocket Launcher", L"Carl G", L"R-Launcher", L"special", {5, 4, 5, 3.5f, 1}, "carlgustaf", "rocket", "rocketeer satchel"},
        {"dartrifle", "sniperdart", L"Dart Rifle", L"Dart Rifle", L"Sniper", L"special", {5, 4, 5, 3.5f, 1}, "dart rifle", "darts", "marksmans bandolier"},
        {"lpo50", "lpo50", L"LPO-50 Flamethrower", L"LPO-50", L"Flamethrower", L"special", {3, 1, 5, 3.5f, 5}, "lpo50", "fuel", "pyrotechnic satchel"},
        {"m249", "m249", L"M-249 SAW", L"M-249", L"LMG", L"special", {4, 4, 4, 3, 4}, "m249", "lmg", "gunner pack"},
        {"mortar", "mortar", L"Type 63 Mortar", L"Mortar", L"Mortar", L"special", {-1, -1, -1, -1, -1}, "mortar", "mortar", "rocketeer satchel"},
        {"pkm", "rpk", L"PKM", L"PKM", L"LMG", L"special", {3, 4, 3, 3.5f, 3}, "pkm", "lmg", "gunner pack"},
        {"rpg7", "rpg7", L"RPG-7 Rocket Launcher", L"RPG-7", L"R-Launcher", L"special", {5, 4, 3, 4, 1}, "rpg7", "rocket", "rocketeer satchel"},
        {"ied", "ied", L"IEDs", L"IED", L"Explosive", L"special", {5, 0, 0, 5, 0}, "ied", "ied", "grenadier webbing"},
    };

    // The belt, in its rows' order: what each is, how much of it the player carries, its picture,
    // its note, and the upgrades the shop sells for it, empty where it sells none.
    struct BeltItem {
        const wchar_t* name;
        const char* picture;
        const wchar_t* note;
        const char* upgrades[2];
    };
    constexpr BeltItem kBelt[] = {
        {L"Syrettes", "seringe", L"Heals fully, as H does.", {"medium first aid manual", "large first aid manual"}},
        {L"Malaria pills", "pills", L"Taken when an attack comes.", {"", ""}},
        {L"M67 grenades", "m67", L"Thrown with Q.", {"grenadier webbing", ""}},
        {L"Molotovs", "molotov", L"Picked with F, thrown with Q.", {"pyrotechnic satchel", ""}},
    };

    // Every picture the post-it has, as build-ui.ps1 makes them; a gun's row has the guns' own.
    constexpr const char* kGunPictures[] = {
        "6p9", "ak47", "as50", "carlgust", "deagle", "dragunov", "flare", "fnfal", "g3ka4", "ied",
        "ithaca", "lpo50", "m16", "m1903", "m249", "m79", "mac10", "makarov", "mgl40", "mp5sd",
        "p45", "rpg7", "rpk", "sniperdart", "spas12", "usas", "uzi", "mortar"};
    constexpr const char* kOtherPictures[] = {"machete", "diamond", "seringe", "pills", "m67", "molotov"};

    constexpr size_t kItemRows = 8;
    constexpr int kRatings = 5;
    constexpr int kHalfSteps = 10;
    constexpr int kUpgradeSlots = 4;
    // The upgrade icons' slots: a gun's operation manual, repair manual and bandolier, and the
    // syrettes' two first aid manuals in the last two.
    constexpr int kGunUpgradeSlots[] = {0, 1, 2};
    constexpr int kBeltUpgradeSlots[] = {2, 3};

    // A gun's condition is only ever a word, the HUD element IO_..._<WORD> in its colour: the engine
    // counts wear from 1 to 0.
    constexpr const char* kConditions[] = {"NEW", "USED", "WORN", "FAILING"};

    const char* ConditionOf(float reliability) {
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

    // The archetype's last part with spaces for underscores, for a gun the table does not know.
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

    std::string Name(const char* prefix, size_t index, const char* suffix = "") {
        return prefix + std::to_string(index) + suffix;
    }

    // Each element's text and whether it is shown, as the page wants them; a later write to an
    // element replaces an earlier one.
    struct Model {
        std::map<std::string, std::wstring> texts;
        std::map<std::string, bool> shown;

        void Text(const std::string& name, std::wstring text) { texts[name] = std::move(text); }
        void Show(const std::string& name, bool visible) { shown[name] = visible; }

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

    // A row of the page, in reading order: the hands, Items, then the belt.
    struct Row {
        enum Kind { kGun, kMachete, kItem, kDiamonds, kBelt } kind;
        int slot = 0;
        const Inventory::Weapon* gun = nullptr;
        const Items::Record* item = nullptr;
        size_t index = 0;
    };

    // What the page reads from the game for one write.
    struct Carried {
        const void* view;
        void* inventory;
        std::vector<Inventory::Weapon> guns;
        std::vector<Items::Record> items;
        std::vector<std::string> bought;
        int diamonds;
        int belt[std::size(kBelt)];

        bool Bought(const std::string& name) const {
            return std::find(bought.begin(), bought.end(), name) != bought.end();
        }
    };

    // The row selected, and the steps the wheel has moved it since the page was last written.
    int g_row = -1;
    std::atomic<int> g_steps{0};
    // The first Items row shown.
    size_t g_firstItem = 0;

    std::vector<Row> Rows(const Carried& carried) {
        std::vector<Row> rows;
        for (int slot = 1; slot <= 2; ++slot) {
            rows.push_back({Row::kGun, slot, TwoGuns::InSlot(carried.guns, slot)});
        }
        rows.push_back({Row::kMachete});
        for (size_t i = 0; i < carried.items.size(); ++i) {
            rows.push_back({Row::kItem, 0, nullptr, &carried.items[i], i});
        }
        if (carried.diamonds > 0) {
            rows.push_back({Row::kDiamonds, 0, nullptr, nullptr, carried.items.size()});
        }
        for (size_t i = 0; i < std::size(kBelt); ++i) {
            rows.push_back({Row::kBelt, 0, nullptr, nullptr, i});
        }
        return rows;
    }

    // Everything on the post-it hidden or emptied, for the selected row to fill in.
    void ClearPostIt(Model& model) {
        for (const char* picture : kGunPictures) {
            model.Show("IO_POSTIT_PIC_" + Upper(picture), false);
        }
        for (const char* picture : kOtherPictures) {
            model.Show("IO_POSTIT_PIC_" + Upper(picture), false);
        }
        for (const char* name : {"IO_POSTIT_NAME", "IO_POSTIT_SUB", "IO_POSTIT_LINE1", "IO_POSTIT_LINE2",
                                 "IO_POSTIT_LINE3", "IO_POSTIT_VALUE1", "IO_POSTIT_VALUE2", "IO_POSTIT_NOTE",
                                 "IO_POSTIT_WARN"}) {
            model.Text(name, L"");
        }
        Condition(model, "IO_POSTIT_", nullptr);
        for (int slot = 0; slot < kUpgradeSlots; ++slot) {
            model.Show(Name("IO_POSTIT_UP", slot, "_FAINT"), false);
            model.Show(Name("IO_POSTIT_UP", slot, "_BOUGHT"), false);
        }
        for (int rating = 1; rating <= kRatings; ++rating) {
            model.Show(Name("IO_POSTIT_RATING", rating), false);
            model.Show(Name("IO_POSTIT_RATING", rating, "_TRACK"), false);
            for (int half = 1; half <= kHalfSteps; ++half) {
                model.Show(Name("IO_POSTIT_RATING", rating, "_") + std::to_string(half), false);
            }
        }
    }

    // The upgrade icons in `slots`, each faint until the shop item named beside it is bought.
    template <size_t N>
    void Upgrades(Model& model, const Carried& carried, const int (&slots)[N], const std::string (&names)[N]) {
        bool any = false;
        for (size_t i = 0; i < N; ++i) {
            if (names[i].empty()) {
                continue;
            }
            const bool bought = carried.Bought(names[i]);
            model.Show(Name("IO_POSTIT_UP", slots[i], "_FAINT"), !bought);
            model.Show(Name("IO_POSTIT_UP", slots[i], "_BOUGHT"), bought);
            any = true;
        }
        if (any) {
            model.Text("IO_POSTIT_LINE3", L"Upgrades");
        }
    }

    // A gun's post-it, whether it is carried as `weapon` or stowed in Items as `item`.
    void GunPostIt(Model& model, const Carried& carried, const std::string& archetype, float reliability,
                   int loaded, int spare, const std::wstring& where) {
        const Gun* known = GunOf(archetype);
        if (known != nullptr) {
            model.Show("IO_POSTIT_PIC_" + Upper(known->picture), true);
        }
        model.Text("IO_POSTIT_NAME", known != nullptr ? known->name : PlainName(archetype));
        model.Text("IO_POSTIT_SUB", known != nullptr ? std::wstring(known->kind) + L" \x2022 " + known->slot +
                                                           L" \x2022 " + where
                                                     : where);
        if (reliability >= 0) {
            model.Text("IO_POSTIT_LINE1", L"Condition");
            Condition(model, "IO_POSTIT_", ConditionOf(reliability));
        }
        model.Text("IO_POSTIT_LINE2", L"Ammo");
        model.Text("IO_POSTIT_VALUE2", (loaded >= 0 ? std::to_wstring(loaded) : L"-") + L" / " + std::to_wstring(spare));
        if (known != nullptr) {
            const std::string shop = known->shop;
            const std::string names[] = {shop + " operation manual", shop + " repair manual", known->bandolier};
            Upgrades(model, carried, kGunUpgradeSlots, names);
            for (int rating = 0; rating < kRatings; ++rating) {
                const float value = known->ratings[rating];
                if (value < 0) {
                    continue;
                }
                model.Show(Name("IO_POSTIT_RATING", rating + 1), true);
                model.Show(Name("IO_POSTIT_RATING", rating + 1, "_TRACK"), true);
                const int half = static_cast<int>(std::lround(value * 2));
                if (half > 0) {
                    model.Show(Name("IO_POSTIT_RATING", rating + 1, "_") + std::to_string(half), true);
                }
            }
        }
    }

    void PostIt(Model& model, const Carried& carried, const Row& row) {
        ClearPostIt(model);
        switch (row.kind) {
        case Row::kGun: {
            if (row.gun == nullptr) {
                model.Text("IO_POSTIT_NAME", L"gun " + std::to_wstring(row.slot));
                model.Text("IO_POSTIT_SUB", L"empty");
                model.Text("IO_POSTIT_NOTE", L"Nothing here. Pick a gun up, or take one from Items.");
                return;
            }
            std::wstring where = L"gun " + std::to_wstring(row.slot);
            if (Inventory::Id(row.gun->item) == Inventory::InHandId(carried.inventory)) {
                where += L", in hand";
            }
            GunPostIt(model, carried, Inventory::Name(row.gun->weapon), Inventory::Reliability(row.gun->weapon),
                      Inventory::Loaded(carried.inventory, row.gun->weapon),
                      Inventory::SpareRounds(carried.view, row.gun->weapon), where);
            return;
        }
        case Row::kMachete:
            model.Show("IO_POSTIT_PIC_MACHETE", true);
            model.Text("IO_POSTIT_NAME", L"Machete");
            model.Text("IO_POSTIT_SUB", L"always carried \x2022 key 1");
            model.Text("IO_POSTIT_NOTE", L"Never dropped, never stored.");
            return;
        case Row::kItem: {
            const Items::Record& item = *row.item;
            if (item.gun) {
                const Gun* known = GunOf(item.id);
                GunPostIt(model, carried, item.id, item.reliability, item.loaded,
                          known != nullptr ? Inventory::Ammo(carried.view, known->ammo) : 0, L"stowed");
                return;
            }
            model.Text("IO_POSTIT_NAME", std::wstring(item.id.begin(), item.id.end()));
            model.Text("IO_POSTIT_SUB", L"stowed");
            model.Text("IO_POSTIT_LINE1", L"Carried");
            model.Text("IO_POSTIT_VALUE1", L"\x00D7" + std::to_wstring(item.count));
            return;
        }
        case Row::kDiamonds:
            model.Show("IO_POSTIT_PIC_DIAMOND", true);
            model.Text("IO_POSTIT_NAME", std::to_wstring(carried.diamonds) + L" diamonds");
            model.Text("IO_POSTIT_SUB", L"in the wallet");
            model.Text("IO_POSTIT_NOTE", L"Spent at the arms dealer.");
            model.Text("IO_POSTIT_WARN", L"Can't be dropped.");
            return;
        case Row::kBelt: {
            const BeltItem& belt = kBelt[row.index];
            model.Show("IO_POSTIT_PIC_" + Upper(belt.picture), true);
            model.Text("IO_POSTIT_NAME", belt.name);
            model.Text("IO_POSTIT_SUB", L"on the belt");
            model.Text("IO_POSTIT_LINE1", L"Carried");
            model.Text("IO_POSTIT_VALUE1", L"\x00D7" + std::to_wstring(std::max(carried.belt[row.index], 0)));
            const std::string names[] = {belt.upgrades[0], belt.upgrades[1]};
            Upgrades(model, carried, kBeltUpgradeSlots, names);
            model.Text("IO_POSTIT_NOTE", belt.note);
            return;
        }
        }
    }

    void Hand(Model& model, int slot, const Inventory::Weapon* gun, bool selected) {
        const std::string row = Name("IO_HAND", slot);
        const std::string archetype = gun != nullptr ? Inventory::Name(gun->weapon) : "";
        const Gun* known = gun != nullptr ? GunOf(archetype) : nullptr;
        for (const char* picture : kGunPictures) {
            model.Show(row + "_PIC_" + Upper(picture), known != nullptr && std::string(known->picture) == picture);
        }
        model.Text(row + "_NAME", gun == nullptr ? L"" : known != nullptr ? known->shortName : PlainName(archetype));
        model.Show(row + "_EMPTY", gun == nullptr);
        Condition(model, row + "_", gun != nullptr ? ConditionOf(Inventory::Reliability(gun->weapon)) : nullptr);
        model.Show(row + "_SEL", selected);
    }

    std::wstring ItemText(const Carried& carried, const Row& row) {
        if (row.kind == Row::kDiamonds) {
            return std::to_wstring(carried.diamonds) + L" diamonds";
        }
        const Items::Record& item = *row.item;
        if (item.gun) {
            const Gun* known = GunOf(item.id);
            return known != nullptr ? known->shortName : PlainName(item.id);
        }
        std::wstring text(item.id.begin(), item.id.end());
        if (item.count > 1) {
            text += L"  \x00D7" + std::to_wstring(item.count);
        }
        return text;
    }

    Model Build(const void* view, void* inventory) {
        Carried carried{view, inventory, Inventory::Guns(inventory), Items::Load(), Economy::Bought(),
                        Economy::Diamonds(),
                        {Inventory::Ammo(view, "syringe"), Inventory::Pills(),
                         Inventory::Throwables(view, "grenade"), Inventory::Throwables(view, "molotov")}};
        const std::vector<Row> rows = Rows(carried);

        // The gun in hand is selected when the page is first opened.
        if (g_row < 0) {
            g_row = 0;
            for (size_t i = 0; i < rows.size(); ++i) {
                if (rows[i].gun != nullptr && Inventory::Id(rows[i].gun->item) == Inventory::InHandId(inventory)) {
                    g_row = static_cast<int>(i);
                }
            }
        }
        g_row = std::clamp(g_row + g_steps.exchange(0), 0, static_cast<int>(rows.size()) - 1);
        const Row& selected = rows[g_row];

        Model model;
        Hand(model, 1, rows[0].gun, g_row == 0);
        Hand(model, 2, rows[1].gun, g_row == 1);
        model.Show("IO_MACHETE_SEL", selected.kind == Row::kMachete);
        model.Show("IO_MACHETE_MARK", selected.kind == Row::kMachete);

        // Items: a window of rows that follows the selection.
        std::vector<const Row*> items;
        for (const Row& row : rows) {
            if (row.kind == Row::kItem || row.kind == Row::kDiamonds) {
                items.push_back(&row);
            }
        }
        if (selected.kind == Row::kItem || selected.kind == Row::kDiamonds) {
            g_firstItem = std::clamp(g_firstItem, selected.index + 1 >= kItemRows ? selected.index + 1 - kItemRows : 0,
                                     selected.index);
        }
        g_firstItem = std::min(g_firstItem, items.size() > kItemRows ? items.size() - kItemRows : 0);
        for (size_t line = 0; line < kItemRows; ++line) {
            const size_t index = g_firstItem + line;
            const bool shown = index < items.size();
            const bool isSelected = shown && items[index] == &selected;
            model.Text(Name("IO_ITEM", line + 1), shown ? ItemText(carried, *items[index]) : L"");
            model.Show(Name("IO_ITEM", line + 1, "_SEL"), isSelected);
            model.Show(Name("IO_ITEM", line + 1, "_MARK"), isSelected);
        }
        const size_t below = items.size() > g_firstItem + kItemRows ? items.size() - g_firstItem - kItemRows : 0;
        model.Text("IO_ITEMS_ABOVE", g_firstItem > 0 ? L"+ " + std::to_wstring(g_firstItem) + L" above" : L"");
        model.Text("IO_ITEMS_MORE", below > 0 ? L"+ " + std::to_wstring(below) + L" more" : L"");

        for (size_t i = 0; i < std::size(kBelt); ++i) {
            const bool isSelected = selected.kind == Row::kBelt && selected.index == i;
            model.Text(Name("IO_BELT", i + 1, "_COUNT"), L"\x00D7" + std::to_wstring(std::max(carried.belt[i], 0)));
            model.Show(Name("IO_BELT", i + 1, "_SEL"), isSelected);
            model.Show(Name("IO_BELT", i + 1, "_MARK"), isSelected);
        }

        PostIt(model, carried, selected);
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

void Scroll(int steps) { g_steps += steps; }

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
