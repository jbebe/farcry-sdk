// Items as one string of entity data, a record a line: "gun <archetype> <wear> <rounds>" or
// "item <id> <count>".
#include "items.h"

#include "engine/inventory.h"
#include "fcse_api.h"
#include "two_guns.h"

#include <algorithm>
#include <optional>
#include <sstream>

namespace InventoryOverhaul::Items {

namespace {
    constexpr const char* kItemsKey = "InventoryOverhaul.Items";
    constexpr size_t kMostText = 16384;
    constexpr const char* kWeaponPrefix = "weapons.";

    Record Keep(const void* inventory, void* weapon) {
        return {Inventory::Name(weapon), true, Inventory::Reliability(weapon),
                Inventory::Loaded(inventory, weapon)};
    }

    // A new gun made from a stowed one, with its wear and rounds, beside the guns carried `before`;
    // none when it could not be made.
    std::optional<Inventory::Weapon> Make(void* inventory, const Record& stowed,
                                          const std::vector<Inventory::Weapon>& before) {
        if (!Inventory::AddByName(inventory, stowed.id.c_str())) {
            FCSE::Logf("no %s could be made: it stays in Items", stowed.id.c_str());
            return std::nullopt;
        }
        for (const Inventory::Weapon& gun : Inventory::Guns(inventory)) {
            const bool added = std::none_of(before.begin(), before.end(), [&](const Inventory::Weapon& old) {
                return old.weapon == gun.weapon;
            });
            if (added) {
                if (stowed.reliability >= 0.0f) {
                    Inventory::SetReliability(gun.weapon, stowed.reliability);
                }
                if (stowed.loaded >= 0) {
                    Inventory::SetLoaded(gun.weapon, stowed.loaded);
                }
                return gun;
            }
        }
        return std::nullopt;
    }
}

std::vector<Record> Load() {
    void* player = Inventory::PlayerEntity();
    std::string text(kMostText, '\0');
    if (player == nullptr ||
        !FCSE::ApiPointer()->EntityData->GetString(player, kItemsKey, text.data(), text.size())) {
        return {};
    }
    std::vector<Record> items;
    std::istringstream lines(text.c_str());
    std::string line;
    while (std::getline(lines, line)) {
        std::istringstream fields(line);
        std::string kind;
        Record item;
        fields >> kind >> item.id;
        item.gun = kind == "gun";
        if (item.gun) {
            fields >> item.reliability >> item.loaded;
        } else {
            fields >> item.count;
        }
        if (!item.id.empty()) {
            items.push_back(item);
        }
    }
    return items;
}

void Save(const std::vector<Record>& items) {
    void* player = Inventory::PlayerEntity();
    if (player == nullptr) {
        return;
    }
    std::ostringstream text;
    for (const Record& item : items) {
        if (item.gun) {
            text << "gun " << item.id << ' ' << item.reliability << ' ' << item.loaded << '\n';
        } else {
            text << "item " << item.id << ' ' << item.count << '\n';
        }
    }
    FCSE::ApiPointer()->EntityData->SetString(player, kItemsKey, text.str().c_str());
}

size_t GunCount(const std::vector<Record>& items) {
    return std::count_if(items.begin(), items.end(), [](const Record& item) { return item.gun; });
}

bool Stow(void* inventory, int slot) {
    std::vector<Record> items = Load();
    const std::vector<Inventory::Weapon> guns = Inventory::Guns(inventory);
    const Inventory::Weapon* gun = TwoGuns::InSlot(guns, slot);
    if (gun == nullptr || GunCount(items) >= kMostGuns) {
        return false;
    }
    items.push_back(Keep(inventory, gun->weapon));
    FCSE::Logf("stowed gun %d, the %s with %d loaded", slot, items.back().id.c_str(),
               items.back().loaded);
    TwoGuns::Release(inventory, *gun, false);
    Save(items);
    return true;
}

bool Take(void* inventory, size_t index, int slot) {
    std::vector<Record> items = Load();
    if (index >= items.size() || !items[index].gun) {
        return false;
    }
    const Record taken = items[index];
    const std::vector<Inventory::Weapon> before = Inventory::Guns(inventory);
    if (slot == 0 && before.size() >= 2) {
        slot = TwoGuns::SlotOf(TwoGuns::ToReplace(inventory, before).weapon);
    }
    const Inventory::Weapon* out = slot != 0 ? TwoGuns::InSlot(before, slot) : nullptr;
    const std::optional<Inventory::Weapon> made = Make(inventory, taken, before);
    if (!made) {
        return false;
    }
    items.erase(items.begin() + index);
    if (out != nullptr) {
        items.push_back(Keep(inventory, out->weapon));
        FCSE::Logf("the %s goes into Items in its place with %d loaded", items.back().id.c_str(),
                   items.back().loaded);
        Inventory::Remove(inventory, out->weapon);
    }
    if (slot != 0) {
        TwoGuns::SetSlot(made->weapon, slot);
    }
    Save(items);
    TwoGuns::Settle(inventory);
    TwoGuns::Draw(inventory, *made);
    FCSE::Logf("took the %s out of Items with %d loaded: gun %d", taken.id.c_str(), taken.loaded,
               TwoGuns::SlotOf(made->weapon));
    return true;
}

bool Drop(void* inventory, size_t index) {
    std::vector<Record> items = Load();
    if (index >= items.size()) {
        return false;
    }
    Record& dropped = items[index];
    if (dropped.gun) {
        const std::optional<Inventory::Weapon> made = Make(inventory, dropped, Inventory::Guns(inventory));
        if (!made) {
            return false;
        }
        FCSE::Logf("dropped the %s from Items with %d loaded", dropped.id.c_str(), dropped.loaded);
        Inventory::Drop(inventory, made->weapon);
    }
    if (dropped.gun || --dropped.count <= 0) {
        items.erase(items.begin() + index);
    }
    Save(items);
    return true;
}

bool Give(const std::string& id) {
    std::vector<Record> items = Load();
    if (id.rfind(kWeaponPrefix, 0) == 0) {
        if (GunCount(items) >= kMostGuns) {
            return false;
        }
        items.push_back({id, true});
    } else {
        const auto same = std::find_if(items.begin(), items.end(), [&](const Record& item) {
            return !item.gun && item.id == id;
        });
        if (same != items.end()) {
            ++same->count;
        } else {
            items.push_back({id});
        }
    }
    Save(items);
    return true;
}

void Discard(size_t index) {
    std::vector<Record> items = Load();
    if (index < items.size()) {
        items.erase(items.begin() + index);
        Save(items);
    }
}

}
