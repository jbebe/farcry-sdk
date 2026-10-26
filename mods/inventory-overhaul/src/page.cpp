// What the Inventory page shows, read from the player's inventory, Items and wallet. The page is
// drawn again only when that changes.
#include "page.h"

#include "engine/economy.h"
#include "engine/inventory.h"
#include "engine/paper.h"
#include "items.h"
#include "page_layout.h"
#include "two_guns.h"

#include <algorithm>
#include <string>
#include <vector>

namespace InventoryOverhaul::Page {

namespace {
    using PageLayout::Line;

    std::wstring g_shown;

    // "weapons.Special.M249_Saw" is the M249 Saw.
    std::wstring DisplayName(const std::string& archetype) {
        std::string name = archetype.substr(archetype.find_last_of('.') + 1);
        std::replace(name.begin(), name.end(), '_', ' ');
        return std::wstring(name.begin(), name.end());
    }

    PageLayout::Model Build(const void* view, void* inventory) {
        PageLayout::Model model;
        std::vector<Inventory::Weapon> guns = Inventory::Guns(inventory);
        std::sort(guns.begin(), guns.end(), [](const Inventory::Weapon& a, const Inventory::Weapon& b) {
            return TwoGuns::SlotOf(a.weapon) < TwoGuns::SlotOf(b.weapon);
        });
        const Inventory::Weapon* shown = nullptr;
        for (const Inventory::Weapon& gun : guns) {
            const bool inHand = Inventory::Id(gun.item) == Inventory::InHandId(inventory);
            Line line = PageLayout::Condition(Inventory::Reliability(gun.weapon));
            line.text = L"(" + std::to_wstring(TwoGuns::SlotOf(gun.weapon)) + L")  " +
                        DisplayName(Inventory::Name(gun.weapon));
            line.marked = inHand;
            model.hands.push_back(line);
            if (shown == nullptr || inHand) {
                shown = &gun;
            }
        }
        for (const Items::Record& item : Items::Load()) {
            std::wstring text = item.gun ? DisplayName(item.id)
                                         : std::wstring(item.id.begin(), item.id.end());
            if (!item.gun && item.count > 1) {
                text += L"  x" + std::to_wstring(item.count);
            }
            model.items.push_back({text});
        }
        const int diamonds = Economy::Diamonds();
        if (diamonds > 0) {
            model.items.push_back({std::to_wstring(diamonds) + L" diamonds"});
        }
        if (shown != nullptr) {
            model.postIt = PageLayout::Condition(Inventory::Reliability(shown->weapon));
            model.postIt.text = DisplayName(Inventory::Name(shown->weapon));
            model.postItAmmo = L"Ammo " + std::to_wstring(Inventory::Loaded(inventory, shown->weapon)) +
                               L" / " + std::to_wstring(Inventory::SpareRounds(view, shown->weapon));
        }
        return model;
    }
}

void Update(const void* view, void* inventory) {
    const PageLayout::Model model = Build(view, inventory);
    std::wstring key = model.Key();
    if (key == g_shown) {
        return;
    }
    if (const uint32_t* pixels = PageLayout::Draw(model)) {
        g_shown = std::move(key);
        Paper::Show(pixels, PageLayout::kSize);
    }
}

}
