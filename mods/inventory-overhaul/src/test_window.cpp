// The window draws on the thread that presents the frame, which must not touch the inventory: its
// buttons queue requests, and it shows what Run last saw on the game thread.
#include "test_window.h"

#include "devtools_api.h"
#include "engine/inventory.h"
#include "items.h"
#include "two_guns.h"

#include <algorithm>
#include <atomic>
#include <cstdio>
#include <mutex>
#include <string>
#include <utility>
#include <vector>

namespace InventoryOverhaul::TestWindow {

namespace {
    enum class Action { Stow, Take, Give, Discard };

    struct Request {
        Action action;
        size_t index;
        std::string id;
    };

    struct Gun {
        int slot;
        std::string name;
        float reliability;
        int loaded;
        bool inHand;
    };

    constexpr const char* kPresets[] = {"weapons.Special.CarlGustaf", "weapons.Special.RPG7",
                                        "weapons.Special.PKM", "weapons.Primary.AK47",
                                        "weapons.Secondary.Makarov", "diamond"};

    std::mutex g_lock;
    std::vector<Request> g_requests;
    std::vector<Gun> g_hands;
    std::vector<Items::Record> g_items;
    std::atomic<bool> g_asked{false};
    std::atomic<bool> g_shown{false};
    char g_giveId[128] = "weapons.Special.CarlGustaf";

    void Ask(Action action, size_t index = 0, std::string id = {}) {
        g_requests.push_back({action, index, std::move(id)});
        g_asked = true;
    }

    void Draw(void*) {
        g_shown = true;
        std::lock_guard<std::mutex> held(g_lock);
        ImGui::SeparatorText("Hands");
        if (g_hands.empty()) {
            ImGui::TextDisabled("no guns");
        }
        for (const Gun& gun : g_hands) {
            ImGui::PushID(gun.slot);
            ImGui::Text("(%d) %s%s", gun.slot, gun.name.c_str(), gun.inHand ? "  [in hand]" : "");
            ImGui::TextDisabled("    wear %.3f, %d loaded", gun.reliability, gun.loaded);
            ImGui::SameLine();
            if (ImGui::SmallButton("Stow")) {
                Ask(Action::Stow, static_cast<size_t>(gun.slot));
            }
            ImGui::PopID();
        }

        ImGui::SeparatorText("Items");
        if (g_items.empty()) {
            ImGui::TextDisabled("empty");
        }
        for (size_t i = 0; i < g_items.size(); ++i) {
            const Items::Record& item = g_items[i];
            ImGui::PushID(static_cast<int>(i));
            if (item.gun) {
                ImGui::Text("%s", item.id.c_str());
                ImGui::TextDisabled("    wear %.3f, %d loaded", item.reliability, item.loaded);
                ImGui::SameLine();
                if (ImGui::SmallButton("Take")) {
                    Ask(Action::Take, i);
                }
            } else {
                ImGui::Text("%s x%d", item.id.c_str(), item.count);
            }
            ImGui::SameLine();
            if (ImGui::SmallButton("Discard")) {
                Ask(Action::Discard, i);
            }
            ImGui::PopID();
        }

        ImGui::SeparatorText("Give");
        ImGui::SetNextItemWidth(-90.0f);
        ImGui::InputText("##id", g_giveId, sizeof(g_giveId));
        ImGui::SameLine();
        if (ImGui::Button("Give")) {
            Ask(Action::Give, 0, g_giveId);
        }
        for (const char* preset : kPresets) {
            if (ImGui::SmallButton(preset)) {
                std::snprintf(g_giveId, sizeof(g_giveId), "%s", preset);
            }
        }
    }
}

void Add() { DevTools::Overlay::AddWindow("Inventory Overhaul", 420.0f, 480.0f, &Draw); }

bool Pending() { return g_asked || g_shown; }

void Run(void* inventory) {
    std::vector<Request> requests;
    {
        std::lock_guard<std::mutex> held(g_lock);
        requests.swap(g_requests);
        g_asked = false;
    }
    for (const Request& request : requests) {
        switch (request.action) {
        case Action::Stow:
            Items::Stow(inventory, static_cast<int>(request.index));
            break;
        case Action::Take:
            Items::Take(inventory, request.index);
            break;
        case Action::Give:
            Items::Give(request.id);
            break;
        case Action::Discard:
            Items::Discard(request.index);
            break;
        }
    }

    if (!g_shown.exchange(false)) {
        return;
    }
    std::vector<Gun> hands;
    for (const Inventory::Weapon& gun : Inventory::Guns(inventory)) {
        hands.push_back({TwoGuns::SlotOf(gun.weapon), Inventory::Name(gun.weapon),
                         Inventory::Reliability(gun.weapon), Inventory::Loaded(gun.weapon),
                         Inventory::Id(gun.item) == Inventory::InHandId(inventory)});
    }
    std::sort(hands.begin(), hands.end(), [](const Gun& a, const Gun& b) { return a.slot < b.slot; });
    std::vector<Items::Record> items = Items::Load();
    std::lock_guard<std::mutex> held(g_lock);
    g_hands = std::move(hands);
    g_items = std::move(items);
}

}
