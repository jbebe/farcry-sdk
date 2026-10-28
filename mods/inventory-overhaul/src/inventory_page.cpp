// The Inventory page: the guns in hand, Items, the belt and the wallet, read from the player's
// inventory, Items and wallet.
#include "pages.h"

#include "engine/economy.h"
#include "engine/inventory.h"
#include "items.h"
#include "two_guns.h"

#include <algorithm>
#include <cctype>
#include <cmath>
#include <iterator>

namespace InventoryOverhaul::Page {

namespace {
    // A gun as the shop and the multiplayer loadout know it: its archetype, its picture
    // gun_icon_<picture>.png, its names and type, its five loadout ratings from 0 to 5 (none below
    // zero), its name in the shop, its ammo type and the bandolier that carries more of it.
    struct Gun {
        const char* archetype;
        const char* picture;
        const wchar_t* name;
        const wchar_t* shortName;
        const wchar_t* kind;
        float ratings[5];
        const char* shop;
        const char* ammo;
        const char* bandolier;
    };

    // Matched against the start of the archetype's last part, in lower case without underscores.
    constexpr Gun kGuns[] = {
        {"ak47", "ak47", L"AK-47", L"AK-47", L"Assault", {3, 4, 3, 5, 4}, "ak47", "assaultrifle", "assault webbing"},
        {"as50", "as50", L"AS50", L"AS50", L"Sniper", {5, 5, 3, 3, 3}, "as50", "sniperrifle", "marksmans bandolier"},
        {"dragunov", "dragunov", L"Dragunov SVD", L"Dragunov", L"Sniper", {3, 4, 4, 4, 4}, "dragunov", "sniperrifle", "marksmans bandolier"},
        {"fnfal", "fnfal", L"FAL Paratrooper", L"FAL", L"Assault", {3, 4, 4, 4, 4}, "fnfal", "assaultrifle", "assault webbing"},
        {"g3ka4", "g3ka4", L"G3-KA4", L"G3-KA4", L"Assault", {3, 3, 4, 4, 3}, "g3ka4", "assaultrifle", "assault webbing"},
        {"ithaca", "ithaca", L"Homeland 37", L"Homeland 37", L"Shotgun", {4, 3, 3, 2.5f, 2}, "ithaca", "shotgun", "shotgun bandolier"},
        {"m16", "m16", L"AR-16", L"AR-16", L"Assault", {4, 4, 4, 3.5f, 3}, "m16", "assaultrifle", "assault webbing"},
        {"m1903", "m1903", L"M1903 Sniper Rifle", L"M1903", L"Sniper", {4, 4, 5, 2.5f, 1}, "m1903", "sniperrifle", "marksmans bandolier"},
        {"mgl140", "mgl40", L"MGL-140", L"MGL-140", L"G-Launcher", {4, 4, 3, 2.5f, 3}, "mgl40", "mgl140", "grenadier webbing"},
        {"mp5", "mp5sd", L"Silent MP-5", L"MP-5", L"Assault", {2, 3, 3, 4, 4}, "mp5", "assaultrifle", "assault webbing"},
        {"spas12", "spas12", L"SPAS-12", L"SPAS-12", L"Shotgun", {5, 2, 1, 3.5f, 3}, "spas12", "shotgun", "shotgun bandolier"},
        {"usas12", "usas", L"USAS-12", L"USAS-12", L"Shotgun", {4, 2, 2, 4, 5}, "usas12", "shotgun", "shotgun bandolier"},
        {"silencedmakarov", "6p9", L"Silent Makarov 6P9", L"6P9", L"Pistol", {2, 2, 3, 3, 4}, "6p9", "deserteagle", "pistol belt"},
        {"deserteagle", "deagle", L"Eagle.50", L"Eagle.50", L"Pistol", {4, 3, 3, 2.5f, 2}, "de", "deserteagle", "pistol belt"},
        {"flaregun", "flare", L"Flare Pistol", L"Flare Pistol", L"Flare", {3, 5, 1, 1.5f, 1}, "flare", "flare", "pyrotechnic satchel"},
        {"m79", "m79", L"M-79 Grenade Launcher", L"M-79", L"G-Launcher", {4, 3, 2, 2, 2}, "m79", "m79", "grenadier webbing"},
        {"mac10", "mac10", L"MAC-10", L"MAC-10", L"SMG", {2, 2, 2, 2.5f, 5}, "mac10", "smg", "light assault webbing"},
        {"makarov", "makarov", L"Makarov", L"Makarov", L"Pistol", {2, 2, 3, 2.5f, 4}, "makarov", "deserteagle", "pistol belt"},
        {"star45", "p45", L"Star .45", L"Star .45", L"Pistol", {3, 2, 3, 3.5f, 3}, "star45", "deserteagle", "pistol belt"},
        {"uzi", "uzi", L"Uzi", L"Uzi", L"SMG", {2, 2, 3, 3, 4}, "uzi", "smg", "light assault webbing"},
        {"carlgustaf", "carlgust", L"Carl G Rocket Launcher", L"Carl G", L"R-Launcher", {5, 4, 5, 3.5f, 1}, "carlgustaf", "rocket", "rocketeer satchel"},
        {"dartrifle", "sniperdart", L"Dart Rifle", L"Dart Rifle", L"Sniper", {5, 4, 5, 3.5f, 1}, "dart rifle", "darts", "marksmans bandolier"},
        {"lpo50", "lpo50", L"LPO-50 Flamethrower", L"LPO-50", L"Flamethrower", {3, 1, 5, 3.5f, 5}, "lpo50", "fuel", "pyrotechnic satchel"},
        {"m249", "m249", L"M-249 SAW", L"M-249", L"LMG", {4, 4, 4, 3, 4}, "m249", "lmg", "gunner pack"},
        {"mortar", "mortar", L"Type 63 Mortar", L"Mortar", L"Mortar", {-1, -1, -1, -1, -1}, "mortar", "mortar", "rocketeer satchel"},
        {"pkm", "rpk", L"PKM", L"PKM", L"LMG", {3, 4, 3, 3.5f, 3}, "pkm", "lmg", "gunner pack"},
        {"rpg7", "rpg7", L"RPG-7 Rocket Launcher", L"RPG-7", L"R-Launcher", {5, 4, 3, 4, 1}, "rpg7", "rocket", "rocketeer satchel"},
        {"ied", "ied", L"IEDs", L"IED", L"Explosive", {5, 0, 0, 5, 0}, "ied", "ied", "grenadier webbing"},
    };

    // The belt, in its rows' order: what each is, its picture, and the upgrades the shop sells for
    // it, empty where it sells none.
    struct BeltItem {
        const wchar_t* name;
        const char* picture;
        const char* upgrades[2];
    };
    constexpr BeltItem kBelt[] = {
        {L"Syrettes", "seringe", {"medium first aid manual", "large first aid manual"}},
        {L"Malaria pills", "pills", {"", ""}},
        {L"M67 grenades", "m67", {"grenadier webbing", ""}},
        {L"Molotovs", "molotov", {"pyrotechnic satchel", ""}},
    };

    // The weapons and the machete, the rows above Items.
    constexpr int kHandRows = 3;
    constexpr size_t kItemRows = 10;

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

    // A row of the page, in reading order: the weapons, Items, then the belt.
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

    // The first Items row shown.
    size_t g_firstItem = 0;

    std::vector<Row> Rows(const Carried& carried) {
        std::vector<Row> rows;
        for (int slot = 1; slot <= 2; ++slot) {
            rows.push_back({Row::kGun, slot, TwoGuns::InSlot(carried.guns, slot)});
        }
        rows.push_back({Row::kMachete});
        // The Papers page lists the papers; a row keeps its record's place in Items.
        for (size_t i = 0; i < carried.items.size(); ++i) {
            if (carried.items[i].gun || !IsPaper(carried.items[i].id)) {
                rows.push_back({Row::kItem, 0, nullptr, &carried.items[i], i});
            }
        }
        if (carried.diamonds > 0) {
            rows.push_back({Row::kDiamonds, 0, nullptr, nullptr, carried.items.size()});
        }
        for (size_t i = 0; i < std::size(kBelt); ++i) {
            rows.push_back({Row::kBelt, 0, nullptr, nullptr, i});
        }
        return rows;
    }

    // The upgrade icons <icons>0... on the post-it's line `line`, each faint until the shop item
    // named beside it is bought.
    template <size_t N>
    void Upgrades(Model& model, const Carried& carried, const std::string& icons, int line,
                  const std::string (&names)[N]) {
        bool any = false;
        for (size_t i = 0; i < N; ++i) {
            if (names[i].empty()) {
                continue;
            }
            const bool bought = carried.Bought(names[i]);
            model.Show(Name(icons, i, "_FAINT"), !bought);
            model.Show(Name(icons, i, "_BOUGHT"), bought);
            any = true;
        }
        if (any) {
            model.Text(Name("IO_POSTIT_LINE", line), L"Upgrades");
        }
    }

    // A gun's post-it, whether it is carried as `weapon` or stowed in Items as `item`.
    void GunPostIt(Model& model, const Carried& carried, const std::string& archetype, float reliability,
                   int loaded, int spare) {
        const Gun* known = GunOf(archetype);
        if (known != nullptr) {
            model.Show("IO_POSTIT_PIC_" + Upper(known->picture), true);
            model.Text("IO_POSTIT_SUB", known->kind);
        }
        model.Text("IO_POSTIT_NAME", known != nullptr ? known->name : PlainName(archetype));
        if (reliability >= 0) {
            model.Text("IO_POSTIT_LINE1", L"Condition");
            model.Text("IO_POSTIT_VALUE1", Condition(reliability));
        }
        model.Text("IO_POSTIT_LINE2", L"Ammo");
        model.Text("IO_POSTIT_VALUE2", (loaded >= 0 ? std::to_wstring(loaded) : L"-") + L" / " + std::to_wstring(spare));
        if (known != nullptr) {
            const std::string shop = known->shop;
            const std::string names[] = {shop + " operation manual", shop + " repair manual", known->bandolier};
            Upgrades(model, carried, "IO_POSTIT_UP", 3, names);
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
        switch (row.kind) {
        case Row::kGun: {
            if (row.gun == nullptr) {
                model.Text("IO_POSTIT_NAME", L"gun " + std::to_wstring(row.slot));
                model.Text("IO_POSTIT_SUB", L"empty");
                model.Text("IO_POSTIT_NOTE", L"Nothing here. Pick a gun up, or take one from Items.");
                return;
            }
            GunPostIt(model, carried, Inventory::Name(row.gun->weapon), Inventory::Reliability(row.gun->weapon),
                      Inventory::Loaded(carried.inventory, row.gun->weapon),
                      Inventory::SpareRounds(carried.view, row.gun->weapon));
            return;
        }
        case Row::kMachete:
            model.Show("IO_POSTIT_PIC_MACHETE", true);
            model.Text("IO_POSTIT_NAME", L"Machete");
            model.Text("IO_POSTIT_SUB", L"Melee");
            model.Text("IO_POSTIT_NOTE", L"A rusty old companion");
            return;
        case Row::kItem: {
            const Items::Record& item = *row.item;
            if (item.gun) {
                const Gun* known = GunOf(item.id);
                GunPostIt(model, carried, item.id, item.reliability, item.loaded,
                          known != nullptr ? Inventory::Ammo(carried.view, known->ammo) : 0);
                return;
            }
            model.Text("IO_POSTIT_NAME", PlainName(item.id));
            model.Text("IO_POSTIT_SUB", L"stowed");
            model.Text("IO_POSTIT_LINE1", L"Carried");
            model.Text("IO_POSTIT_VALUE1", L"\x00D7" + std::to_wstring(item.count));
            return;
        }
        case Row::kDiamonds:
            model.Show("IO_POSTIT_PIC_DIAMOND", true);
            model.Text("IO_POSTIT_NAME", std::to_wstring(carried.diamonds) + L" diamonds");
            model.Text("IO_POSTIT_ABOUT",
                       L"Octahedral crystals, near-colorless, a few flaws. Mostly the moral kind.");
            return;
        case Row::kBelt: {
            const BeltItem& belt = kBelt[row.index];
            model.Show("IO_POSTIT_PIC_" + Upper(belt.picture), true);
            model.Text("IO_POSTIT_NAME", belt.name);
            model.Text("IO_POSTIT_LINE1", L"Carried");
            model.Text("IO_POSTIT_VALUE1", L"\x00D7" + std::to_wstring(std::max(carried.belt[row.index], 0)));
            const std::string names[] = {belt.upgrades[0], belt.upgrades[1]};
            Upgrades(model, carried, "IO_POSTIT_BELT_UP", 2, names);
            return;
        }
        }
    }

    void Hand(Model& model, int slot, const Inventory::Weapon* gun, bool selected) {
        const std::string row = Name("IO_HAND", slot);
        const std::string archetype = gun != nullptr ? Inventory::Name(gun->weapon) : "";
        const Gun* known = gun != nullptr ? GunOf(archetype) : nullptr;
        model.Text(row + "_NAME", gun == nullptr ? L"" : known != nullptr ? known->shortName : PlainName(archetype));
        model.Show(row + "_EMPTY", gun == nullptr);
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
        std::wstring text = PlainName(item.id);
        if (item.count > 1) {
            text += L"  \x00D7" + std::to_wstring(item.count);
        }
        return text;
    }
}

int InventoryPage(Model& model, int& selected, const void* view, void* inventory, bool dropped) {
    Carried carried{view, inventory, Inventory::Guns(inventory), Items::Load(), Economy::Bought(),
                    Economy::Diamonds(),
                    {Inventory::Ammo(view, "syringe"), Inventory::Pills(),
                     Inventory::Throwables(view, "grenade"), Inventory::Throwables(view, "molotov")}};
    const std::vector<Row> rows = Rows(carried);

    // The gun in hand is selected when the page is first opened.
    if (selected < 0) {
        selected = 0;
        for (size_t i = 0; i < rows.size(); ++i) {
            if (rows[i].gun != nullptr && Inventory::Id(rows[i].gun->item) == Inventory::InHandId(inventory)) {
                selected = static_cast<int>(i);
            }
        }
    }
    selected = std::clamp(selected, 0, static_cast<int>(rows.size()) - 1);
    const Row& row = rows[selected];
    // X drops the selected gun, or one of the selected item, and the page is read again.
    const bool droppable = row.gun != nullptr || row.kind == Row::kItem;
    if (dropped && droppable) {
        if (row.gun != nullptr) {
            TwoGuns::Release(inventory, *row.gun, true);
        } else {
            Items::Drop(inventory, row.index);
        }
        return InventoryPage(model, selected, view, inventory, false);
    }
    if (droppable) {
        model.Prompt(L"drop_item", L"Drop");
    }

    Hand(model, 1, rows[0].gun, selected == 0);
    Hand(model, 2, rows[1].gun, selected == 1);
    model.Show("IO_MACHETE_SEL", row.kind == Row::kMachete);

    std::vector<std::wstring> texts;
    for (const Row& each : rows) {
        if (each.kind == Row::kItem || each.kind == Row::kDiamonds) {
            texts.push_back(ItemText(carried, each));
        }
    }
    List(model, "ITEM", kItemRows, g_firstItem, texts, selected - kHandRows);

    for (size_t i = 0; i < std::size(kBelt); ++i) {
        const bool isSelected = row.kind == Row::kBelt && row.index == i;
        model.Text(Name("IO_BELT", i + 1, "_COUNT"), L"\x00D7" + std::to_wstring(std::max(carried.belt[i], 0)));
        model.Show(Name("IO_BELT", i + 1, "_SEL"), isSelected);
    }

    PostIt(model, carried, row);
    return static_cast<int>(rows.size());
}

}
