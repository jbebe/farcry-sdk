// Items as one string of entity data, a record a line: "gun <archetype> <wear> <rounds>" or
// "item <id> <count>".
#include "items.h"

#include "engine/inventory.h"
#include "fcse_api.h"
#include "two_guns.h"

#include <algorithm>
#include <sstream>

namespace InventoryOverhaul::Items {

namespace {
    constexpr const char* kItemsKey = "InventoryOverhaul.Items";
    constexpr size_t kMostText = 16384;
    constexpr const char* kWeaponPrefix = "weapons.";

    Record Keep(void* weapon) {
        return {Inventory::Name(weapon), true, Inventory::Reliability(weapon),
                Inventory::Loaded(weapon)};
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
    const bool inHand = Inventory::Id(gun->item) == Inventory::InHandId(inventory);
    items.push_back(Keep(gun->weapon));
    FCSE::Logf("stowed gun %d, the %s", slot, items.back().id.c_str());
    Inventory::Remove(inventory, gun->weapon);
    Save(items);
    const std::vector<Inventory::Weapon> rest = Inventory::Guns(inventory);
    if (inHand && !rest.empty()) {
        TwoGuns::Draw(inventory, rest.front());
    }
    return true;
}

bool Take(void* inventory, size_t index) {
    std::vector<Record> items = Load();
    if (index >= items.size() || !items[index].gun) {
        return false;
    }
    const Record taken = items[index];
    const std::vector<Inventory::Weapon> before = Inventory::Guns(inventory);
    if (!Inventory::AddByName(inventory, taken.id.c_str())) {
        FCSE::Logf("no %s could be made: it stays in Items", taken.id.c_str());
        return false;
    }
    items.erase(items.begin() + index);
    if (before.size() >= 2) {
        const Inventory::Weapon& out = TwoGuns::ToReplace(inventory, before);
        items.push_back(Keep(out.weapon));
        FCSE::Logf("the %s goes into Items in its place", items.back().id.c_str());
        Inventory::Remove(inventory, out.weapon);
    }
    Save(items);
    TwoGuns::Settle(inventory);
    for (const Inventory::Weapon& gun : Inventory::Guns(inventory)) {
        const bool added = std::none_of(before.begin(), before.end(),
                                        [&](const Inventory::Weapon& old) {
                                            return old.weapon == gun.weapon;
                                        });
        if (!added) {
            continue;
        }
        if (taken.reliability >= 0.0f) {
            Inventory::SetReliability(gun.weapon, taken.reliability);
        }
        if (taken.loaded >= 0) {
            Inventory::SetLoaded(gun.weapon, taken.loaded);
        }
        TwoGuns::Draw(inventory, gun);
        FCSE::Logf("took the %s out of Items: gun %d", taken.id.c_str(),
                   TwoGuns::SlotOf(gun.weapon));
    }
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
