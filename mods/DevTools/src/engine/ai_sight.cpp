#include "engine/ai_sight.h"

#include "fcse_api.h"

#include <atomic>
#include <chrono>
#include <cstdint>
#include <cstring>
#include <mutex>
#include <unordered_map>

namespace {
    using DevTools::AiSight::Point;
    using DevTools::AiSight::Sight;
    using Clock = std::chrono::steady_clock;

    // __thiscall methods, which free functions spell __fastcall with an unused edx.
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
    // GetPosition and IsAPlayer slots and the visibility it currently senses and sees from; the
    // sensory system's knowledge base.
    constexpr ptrdiff_t kTargetObject = 0x04;
    constexpr ptrdiff_t kObjectId = 0x0C;
    constexpr ptrdiff_t kTargetAgent = 0x08;
    constexpr size_t kPositionSlot = 0x78 / sizeof(void*);
    constexpr size_t kIsPlayerSlot = 0xBC / sizeof(void*);
    constexpr ptrdiff_t kAgentFuzzy = 0x868;
    constexpr ptrdiff_t kAgentClear = 0x86C;
    constexpr ptrdiff_t kSensesKnowledge = 0x24;
    // CVisibilityContext: its cones, observer, eye, target position and view direction.
    constexpr ptrdiff_t kContextCones = 0x04;
    constexpr ptrdiff_t kContextObserver = 0x08;
    constexpr ptrdiff_t kContextEye = 0x10;
    constexpr ptrdiff_t kContextTarget = 0x1C;
    constexpr ptrdiff_t kContextView = 0x28;
    // SPawnFOVPerRegion: the focus cone's length as adjusted, the archetype's focus angle, and the
    // peripheral cone's length and angle as adjusted.
    constexpr ptrdiff_t kFocusLength = 0x08;
    constexpr ptrdiff_t kBaseFocusAngle = 0x14;
    constexpr ptrdiff_t kPeripheralLength = 0x1C;
    constexpr ptrdiff_t kPeripheralAngle = 0x20;
    // CSensorySystemKBEntry: flags (bits 0 to 2 set when inside both cones), where the target was
    // last perceived, and its smoothed visibility.
    constexpr ptrdiff_t kEntryFlags = 0x00;
    constexpr ptrdiff_t kEntryPosition = 0x28;
    constexpr ptrdiff_t kEntryVisibility = 0x44;
    constexpr uint32_t kPlainlySeen = 0x7;

    // A soldier whose vision has not updated for this long is dead, gone, or too far to matter, and
    // nothing is copied once nobody has asked for this long.
    constexpr auto kStale = std::chrono::seconds(1);

    struct Copy {
        Sight sight;
        Clock::time_point at;
    };

    std::atomic<Clock::rep> g_askedUntil{0};
    std::mutex g_mutex;
    std::unordered_map<const void*, Copy> g_sights;

    template <class T>
    T Read(const void* object, ptrdiff_t offset) {
        T value;
        std::memcpy(&value, static_cast<const uint8_t*>(object) + offset, sizeof(T));
        return value;
    }

    // The virtual method at `slot` of `object`.
    template <class Fn>
    Fn Method(const void* object, size_t slot) {
        return reinterpret_cast<Fn>(Read<void* const*>(object, 0)[slot]);
    }

    void Capture(void* senses, const uint8_t* target, const uint8_t* context, Clock::time_point now) {
        Sight sight = {};
        const void* observer = Read<const void*>(context, kContextObserver);
        Method<PositionFn>(observer, kPositionSlot)(observer, nullptr, sight.feet.data());
        sight.eye = Read<Point>(context, kContextEye);
        sight.view = Read<Point>(context, kContextView);
        sight.target = Read<Point>(context, kContextTarget);
        const auto* cones = Read<const uint8_t*>(context, kContextCones);
        sight.focusLength = Read<float>(cones, kFocusLength);
        sight.baseFocusAngle = Read<float>(cones, kBaseFocusAngle);
        sight.peripheralLength = Read<float>(cones, kPeripheralLength);
        sight.peripheralAngle = Read<float>(cones, kPeripheralAngle);
        sight.fuzzy = Read<float>(observer, kAgentFuzzy);
        sight.clear = Read<float>(observer, kAgentClear);

        const uint32_t id = Read<uint32_t>(Read<const void*>(target, kTargetObject), kObjectId);
        const uint8_t* entry = nullptr;
        sight.known = g_knowledge(Read<void*>(senses, kSensesKnowledge), nullptr, &id, &entry) &&
                      entry != nullptr;
        if (sight.known) {
            sight.visibility = Read<float>(entry, kEntryVisibility);
            sight.plainlySeen = (Read<uint32_t>(entry, kEntryFlags) & kPlainlySeen) == kPlainlySeen;
            sight.believed = Read<Point>(entry, kEntryPosition);
        }

        const std::lock_guard lock(g_mutex);
        g_sights[observer] = {sight, now};
    }

    void __fastcall AddTargetDataDetour(void* senses, void* unused, const uint8_t* target,
                                        const uint8_t* context) {
        g_originalAddTargetData(senses, unused, target, context);
        const auto now = Clock::now();
        if (now.time_since_epoch().count() >= g_askedUntil.load(std::memory_order_relaxed)) {
            return;
        }
        const void* agent = Read<const void*>(target, kTargetAgent);
        if (agent != nullptr && Method<IsPlayerFn>(agent, kIsPlayerSlot)(agent, nullptr)) {
            Capture(senses, target, context, now);
        }
    }
}

namespace DevTools::AiSight {

void Snapshot(std::vector<Sight>& out) {
    out.clear();
    const auto now = Clock::now();
    g_askedUntil.store((now + kStale).time_since_epoch().count(), std::memory_order_relaxed);
    const std::lock_guard lock(g_mutex);
    std::erase_if(g_sights, [now](const auto& entry) { return now - entry.second.at > kStale; });
    for (const auto& [observer, copy] : g_sights) {
        out.push_back(copy.sight);
    }
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
