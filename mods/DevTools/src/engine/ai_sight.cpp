#include "engine/ai_sight.h"

#include "fcse_api.h"

#include <atomic>
#include <chrono>
#include <cstdint>
#include <cstring>
#include <mutex>
#include <unordered_map>

namespace {
    using DevTools::AiSight::Sight;
    using Clock = std::chrono::steady_clock;

    // __thiscall with two stack arguments and with no arguments, which free functions spell
    // __fastcall.
    using AddTargetDataFn = void(__fastcall*)(void* senses, void* unused, const uint8_t* target,
                                              const uint8_t* context);
    using KnowledgeFn = bool(__fastcall*)(void* knowledge, void* unused, const uint32_t* id,
                                          const uint8_t** entry);
    using IsPlayerFn = bool(__fastcall*)(const void* agent, void* unused);
    // Returns its argument, the position written into it.
    using PositionFn = const float*(__fastcall*)(const void* agent, void* unused, float* out);

    // CSensorySystem::AddTargetDataToKB and CSensorySystemKB::GetKBEntryForEntity.
    FCSE::Relocation<AddTargetDataFn> g_addTargetData{FCSE::Uplay(0x00A03120)};
    FCSE::Relocation<KnowledgeFn> g_knowledge{FCSE::Uplay(0x00A235C0)};
    AddTargetDataFn g_originalAddTargetData = nullptr;

    // The visual target's AI object, whose id keys the knowledge base, and its agent; an agent's
    // GetPosition and IsAPlayer slots; the sensory system's knowledge base.
    constexpr ptrdiff_t kTargetObject = 0x04;
    constexpr ptrdiff_t kObjectId = 0x0C;
    constexpr ptrdiff_t kTargetAgent = 0x08;
    constexpr size_t kPositionSlot = 0x78 / sizeof(void*);
    constexpr size_t kIsPlayerSlot = 0xBC / sizeof(void*);
    constexpr ptrdiff_t kSensesKnowledge = 0x24;
    // CVisibilityContext: its cones, observer, eye, target position and view direction.
    constexpr ptrdiff_t kContextCones = 0x04;
    constexpr ptrdiff_t kContextObserver = 0x08;
    constexpr ptrdiff_t kContextEye = 0x10;
    constexpr ptrdiff_t kContextTarget = 0x1C;
    constexpr ptrdiff_t kContextView = 0x28;
    // SPawnFOVPerRegion: the focus cone's length and angle as adjusted, the archetype's focus
    // angle, and the peripheral cone's length and angle as adjusted.
    constexpr ptrdiff_t kFocusLength = 0x08;
    constexpr ptrdiff_t kFocusAngle = 0x0C;
    constexpr ptrdiff_t kBaseFocusAngle = 0x14;
    constexpr ptrdiff_t kPeripheralLength = 0x1C;
    constexpr ptrdiff_t kPeripheralAngle = 0x20;
    // CSensorySystemKBEntry: flags (bits 0 to 2 set when inside both cones), where the target was
    // last perceived, and its smoothed visibility.
    constexpr ptrdiff_t kEntryFlags = 0x00;
    constexpr ptrdiff_t kEntryPosition = 0x28;
    constexpr ptrdiff_t kEntryVisibility = 0x44;
    constexpr uint32_t kPlainlySeen = 0x7;

    // A soldier whose vision has not updated for this long is dead, gone, or too far to matter.
    constexpr auto kStale = std::chrono::seconds(1);

    struct Copy {
        Sight sight;
        Clock::time_point at;
    };

    std::atomic<bool> g_enabled{false};
    std::mutex g_mutex;
    std::unordered_map<const void*, Copy> g_sights;

    template <class T>
    T Read(const void* object, ptrdiff_t offset) {
        T value;
        std::memcpy(&value, static_cast<const uint8_t*>(object) + offset, sizeof(T));
        return value;
    }

    void Read3(const uint8_t* object, ptrdiff_t offset, float out[3]) {
        std::memcpy(out, object + offset, 3 * sizeof(float));
    }

    void* const* Vtable(const void* agent) { return Read<void* const*>(agent, 0); }

    bool IsPlayer(const void* agent) {
        return reinterpret_cast<IsPlayerFn>(Vtable(agent)[kIsPlayerSlot])(agent, nullptr);
    }

    void Capture(void* senses, const uint8_t* target, const uint8_t* context) {
        Sight sight = {};
        const void* observer = Read<const void*>(context, kContextObserver);
        reinterpret_cast<PositionFn>(Vtable(observer)[kPositionSlot])(observer, nullptr, sight.feet);
        Read3(context, kContextEye, sight.eye);
        Read3(context, kContextView, sight.view);
        Read3(context, kContextTarget, sight.target);
        const auto* cones = Read<const uint8_t*>(context, kContextCones);
        sight.focusLength = Read<float>(cones, kFocusLength);
        sight.focusAngle = Read<float>(cones, kFocusAngle);
        sight.baseFocusAngle = Read<float>(cones, kBaseFocusAngle);
        sight.peripheralLength = Read<float>(cones, kPeripheralLength);
        sight.peripheralAngle = Read<float>(cones, kPeripheralAngle);

        const uint32_t id = Read<uint32_t>(Read<const void*>(target, kTargetObject), kObjectId);
        const uint8_t* entry = nullptr;
        sight.known = g_knowledge(Read<void*>(senses, kSensesKnowledge), nullptr, &id, &entry) &&
                      entry != nullptr;
        if (sight.known) {
            sight.visibility = Read<float>(entry, kEntryVisibility);
            sight.plainlySeen = (Read<uint32_t>(entry, kEntryFlags) & kPlainlySeen) == kPlainlySeen;
            Read3(entry, kEntryPosition, sight.believed);
        }

        const std::lock_guard lock(g_mutex);
        g_sights[observer] = {sight, Clock::now()};
    }

    void __fastcall AddTargetDataDetour(void* senses, void* unused, const uint8_t* target,
                                        const uint8_t* context) {
        g_originalAddTargetData(senses, unused, target, context);
        if (!g_enabled.load(std::memory_order_relaxed)) {
            return;
        }
        const void* agent = Read<const void*>(target, kTargetAgent);
        if (agent != nullptr && IsPlayer(agent)) {
            Capture(senses, target, context);
        }
    }
}

namespace DevTools::AiSight {

void SetEnabled(bool enabled) {
    g_enabled.store(enabled, std::memory_order_relaxed);
    if (!enabled) {
        const std::lock_guard lock(g_mutex);
        g_sights.clear();
    }
}

std::vector<Sight> Snapshot() {
    std::vector<Sight> sights;
    const auto now = Clock::now();
    const std::lock_guard lock(g_mutex);
    for (auto it = g_sights.begin(); it != g_sights.end();) {
        if (now - it->second.at > kStale) {
            it = g_sights.erase(it);
        } else {
            sights.push_back(it->second.sight);
            ++it;
        }
    }
    return sights;
}

void Install() {
    if (!g_addTargetData || !g_knowledge ||
        !FCSE::ApiPointer()->Hook(reinterpret_cast<void*>(g_addTargetData.address()),
                                  reinterpret_cast<void*>(&AddTargetDataDetour),
                                  reinterpret_cast<void**>(&g_originalAddTargetData))) {
        FCSE::ApiPointer()->Log("ai sight: the soldiers' vision update was not found in this build");
    }
}

}
