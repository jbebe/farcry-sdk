// The window draws on the thread that presents the frame, which must not touch the inventory: its
// buttons queue requests, and it shows what Run last saw on the game thread.
#include "test_window.h"

#include "devtools_api.h"
#include "engine/economy.h"
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
    enum class Action { Stow, Take, Give, Discard, Story, Lua };

    struct Request {
        Action action;
        size_t index;
        std::string id;
        int slot;
    };

    struct Gun {
        int slot;
        std::string name;
        float reliability;
        int loaded;
        bool inHand;
    };

    constexpr const char* kPresets[] = {"weapons.Special.Carl_Gustaf", "weapons.Special.RPG7",
                                        "weapons.Special.PKM", "weapons.Primary.AK47",
                                        "weapons.Secondary.Makarov"};
    // The mockup's Fill items: two stowed guns, nine items, then Tales of Africa's note and photo.
    constexpr const char* kFill[] = {"weapons.Special.Carl_Gustaf", "weapons.Special.PKM", "Courier_pouch",
                                     "Sealed_envelope", "Vaccine_case", "Radio_valve", "Roll_of_film",
                                     "Map_case", "Dog_tags", "Ledger", "Camo_Suit", "Note.Torn_notebook_page",
                                     "Photo.Cliff_village"};
    // The mockup's buddies, met through the game's own Lua (whose bools take 1), Frank the primary
    // buddy, Hakim the secondary (who must be alive) and Warren dead, and the Jackal tapes found the
    // same way.
    constexpr const char* kBuddies[] = {"Flora_Guillen", "Frank_Bilders", "Marty_Alencar", "Josip_Idromeno",
                                        "Warren_Clyde"};
    constexpr int kTapes = 5;
    // Set in the game's environment for a scripted test: the first fills Items as the game starts,
    // the second holds console lines, split by |, run in DevTools' console then, # starting Lua.
    constexpr const char* kFillVariable = "INVENTORY_OVERHAUL_FILL";
    constexpr const char* kLuaVariable = "INVENTORY_OVERHAUL_LUA";

    std::mutex g_lock;
    std::vector<Request> g_requests;
    std::vector<Gun> g_hands;
    std::vector<Items::Record> g_items;
    // The player's own diamonds, which Items shows but cannot drop.
    int g_diamonds = -1;
    std::atomic<bool> g_asked{false};
    std::atomic<bool> g_shown{false};
    char g_giveId[128] = "weapons.Special.Carl_Gustaf";

    void Ask(Action action, size_t index = 0, std::string id = {}, int slot = 0) {
        g_requests.push_back({action, index, std::move(id), slot});
        g_asked = true;
    }

    void Fill() {
        for (const char* id : kFill) {
            Ask(Action::Give, 0, id);
        }
        Ask(Action::Story);
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
        if (g_diamonds >= 0) {
            ImGui::Text("diamonds x%d", g_diamonds);
            ImGui::SameLine();
            if (ImGui::SmallButton("+1")) {
                DevTools::Overlay::PostLine("#Game:AddDiamonds(1)");
            }
        }
        if (g_items.empty()) {
            ImGui::TextDisabled("nothing else");
        }
        for (size_t i = 0; i < g_items.size(); ++i) {
            const Items::Record& item = g_items[i];
            ImGui::PushID(static_cast<int>(i));
            if (item.gun) {
                ImGui::Text("%s", item.id.c_str());
                ImGui::TextDisabled("    wear %.3f, %d loaded", item.reliability, item.loaded);
                for (int slot = 1; slot <= 2; ++slot) {
                    ImGui::SameLine();
                    if (ImGui::SmallButton(slot == 1 ? "To A" : "To B")) {
                        Ask(Action::Take, i, {}, slot);
                    }
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
        ImGui::SameLine();
        if (ImGui::Button("Fill")) {
            Fill();
        }
        for (const char* preset : kPresets) {
            if (ImGui::SmallButton(preset)) {
                std::snprintf(g_giveId, sizeof(g_giveId), "%s", preset);
            }
        }
    }
}

void Add() {
    DevTools::Overlay::AddWindow("Inventory Overhaul", 420.0f, 480.0f, &Draw);
    std::lock_guard<std::mutex> held(g_lock);
    if (GetEnvironmentVariableA(kFillVariable, nullptr, 0) > 0) {
        Fill();
    }
    char lua[1024];
    if (const DWORD length = GetEnvironmentVariableA(kLuaVariable, lua, sizeof(lua)); length > 0 && length < sizeof(lua)) {
        Ask(Action::Lua, 0, lua);
    }
}

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
            Items::Take(inventory, request.index, request.slot);
            break;
        case Action::Give:
            Items::Give(request.id);
            break;
        case Action::Discard:
            Items::Discard(request.index);
            break;
        case Action::Story:
            for (const char* buddy : kBuddies) {
                const std::string name = buddy;
                DevTools::Overlay::PostLine(("#GetBuddiesManager():BypassSetBuddyUnlock(\"" + name + "\", 1)").c_str());
            }
            DevTools::Overlay::PostLine("#GetBuddiesManager():BypassSetPrimaryBuddy(\"Frank_Bilders\")");
            DevTools::Overlay::PostLine("#GetBuddiesManager():BypassSetSecondaryBuddy(\"Hakim_Echebbi\")");
            DevTools::Overlay::PostLine("#GetBuddiesManager():BypassSetBuddyLifeState(\"Warren_Clyde\", 2)");
            for (int tape = 0; tape < kTapes; ++tape) {
                DevTools::Overlay::PostLine("#GetFCXMissionManager():NewJackalTapeFound()");
            }
            DevTools::Overlay::PostLine("#GetFCXMissionManager():PartnerTapeFound(\"W1V1M1\")");
            break;
        case Action::Lua:
            for (size_t start = 0; start < request.id.size();) {
                size_t end = request.id.find('|', start);
                end = end == std::string::npos ? request.id.size() : end;
                DevTools::Overlay::PostLine(request.id.substr(start, end - start).c_str());
                start = end + 1;
            }
            break;
        }
    }

    if (!g_shown.exchange(false)) {
        return;
    }
    std::vector<Gun> hands;
    for (const Inventory::Weapon& gun : Inventory::Guns(inventory)) {
        hands.push_back({TwoGuns::SlotOf(gun.weapon), Inventory::Name(gun.weapon),
                         Inventory::Reliability(gun.weapon), Inventory::Loaded(inventory, gun.weapon),
                         Inventory::Id(gun.item) == Inventory::InHandId(inventory)});
    }
    std::sort(hands.begin(), hands.end(), [](const Gun& a, const Gun& b) { return a.slot < b.slot; });
    std::vector<Items::Record> items = Items::Load();
    const int diamonds = Economy::Diamonds();
    std::lock_guard<std::mutex> held(g_lock);
    g_hands = std::move(hands);
    g_items = std::move(items);
    g_diamonds = diamonds;
}

}
