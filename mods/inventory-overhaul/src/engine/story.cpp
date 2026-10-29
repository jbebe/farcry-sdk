// The story's game mode services, found as the pause menu's pages find them, at Steam's addresses.
#include "engine/story.h"

#include "engine/memory.h"
#include "fcse_api.h"

#include <algorithm>
#include <cstring>
#include <map>

namespace InventoryOverhaul::Story {

namespace {
    using LocalizeFn = const wchar_t*(__fastcall*)(void* table, void* unused, const EngineString<char>* section,
                                                   const EngineString<char>* key, uint32_t file);
    using BuildClassIdFn = void(__fastcall*)(void* unused, void* unused2);
    using GetServiceFn = void*(__fastcall*)(void* services, void* unused, const uint32_t* type, int create);
    using BuddyStatusFn = int(__fastcall*)(void* buddies, void* unused, const void* name);
    using StatusTextFn = const wchar_t*(__cdecl*)(int status, int gender);
    using LocalizeIntoFn = EngineString<wchar_t>*(__fastcall*)(void* table, void* unused, EngineString<wchar_t>* out,
                                                              const EngineString<char>* section,
                                                              const EngineString<char>* key, uint32_t file);
    using NamesFn = EngineString<wchar_t>*(__fastcall*)(void* page, void* unused, EngineString<wchar_t>* out,
                                                       EngineString<wchar_t> text);
    using FreeFn = void(__fastcall*)(EngineString<wchar_t>* string);

    // CStringTableMgr::Localize and its singleton, and the id of the strings' file every page passes,
    // a constant in the image.
    FCSE::Relocation<LocalizeFn> g_localize{FCSE::Uplay(0x004D1E40)};
    FCSE::Relocation<void* const*> g_stringTable{FCSE::Uplay(0x01644778)};
    constexpr uint32_t kStringFile = 0x079C5159;
    // The table's lookup into a string of the engine's own, CFCXPauseMenuPage::GetDescriptionWithBuddyName,
    // which frees the text it is given, and the engine's std::wstring destructor.
    FCSE::Relocation<LocalizeIntoFn> g_localizeInto{FCSE::Uplay(0x004D1FC0)};
    FCSE::Relocation<NamesFn> g_names{FCSE::Uplay(0x00854750)};
    FCSE::Relocation<FreeFn> g_free{FCSE::Uplay(0x00002890)};
    // CGameModeManager's GetGameModeService and its singleton; the class ids of CFCXMissionManager
    // and CBuddiesManager, each built when its name is still unset.
    FCSE::Relocation<GetServiceFn> g_getService{FCSE::Uplay(0x004DBB80)};
    FCSE::Relocation<void* const*> g_services{FCSE::Uplay(0x01644D74)};
    FCSE::Relocation<BuildClassIdFn> g_buildMissionsType{FCSE::Uplay(0x00666680)};
    FCSE::Relocation<const void* const*> g_missionsTypeName{FCSE::Uplay(0x01649EAC)};
    FCSE::Relocation<const uint32_t*> g_missionsType{FCSE::Uplay(0x01649EBC)};
    FCSE::Relocation<BuildClassIdFn> g_buildBuddiesType{FCSE::Uplay(0x00681C50)};
    FCSE::Relocation<const void* const*> g_buddiesTypeName{FCSE::Uplay(0x0164A474)};
    FCSE::Relocation<const uint32_t*> g_buddiesType{FCSE::Uplay(0x0164A484)};
    // CBuddiesManager::GetBuddyStatus by name, and CFCXUIBuddyUtil::GetBuddyStateString.
    FCSE::Relocation<BuddyStatusFn> g_buddyStatus{FCSE::Uplay(0x007463E0)};
    FCSE::Relocation<StatusTextFn> g_statusText{FCSE::Uplay(0x007575B0)};

    // The mission manager's Jackal and Predecessor tapes, SMissionTape records each starting with its
    // string key, and whether one was found.
    constexpr ptrdiff_t kTapeLists[] = {0x158, 0x164};
    constexpr ptrdiff_t kTapeSize = 0x78;
    constexpr ptrdiff_t kTapeFound = 0x74;
    // The buddies manager's SBuddyData records, each starting with its name, and the primary and
    // secondary buddy's records, the records' end for none.
    constexpr ptrdiff_t kBuddies = 0x44;
    constexpr ptrdiff_t kBuddyCount = 0x48;
    constexpr ptrdiff_t kPrimaryBuddy = 0x50;
    constexpr ptrdiff_t kSecondaryBuddy = 0x54;
    constexpr ptrdiff_t kBuddySize = 0xC0;
    constexpr ptrdiff_t kBuddyGender = 0x74;
    constexpr ptrdiff_t kBuddyUnlocked = 0x98;
    constexpr ptrdiff_t kBuddyHistory = 0x9C;

    bool g_installed = false;

    void* Service(const FCSE::Relocation<BuildClassIdFn>& build, const void* const* name, const uint32_t* type) {
        if (!g_installed) {
            return nullptr;
        }
        if (*name == nullptr) {
            build(nullptr, nullptr);
        }
        void* services = *g_services.get();
        return services != nullptr ? g_getService(services, nullptr, type, 1) : nullptr;
    }
}

bool Install() {
    g_installed = g_localize && g_stringTable && g_getService && g_services && g_buildMissionsType &&
                  g_missionsTypeName && g_missionsType && g_buildBuddiesType && g_buddiesTypeName && g_buddiesType &&
                  g_buddyStatus && g_statusText;
    return g_installed;
}

const std::wstring& Localize(const char* section, const std::string& key) {
    // The table does not change while the game runs, so each string is looked up once.
    static std::map<std::string, std::wstring> s_known;
    static const std::wstring s_none;
    const std::string id = std::string(section) + '/' + key;
    if (const auto known = s_known.find(id); known != s_known.end()) {
        return known->second;
    }
    void* table = g_installed ? *g_stringTable.get() : nullptr;
    if (table == nullptr) {
        return s_none;
    }
    const auto sectionString = EngineString<char>::Of(section, std::strlen(section));
    const auto keyString = EngineString<char>::Of(key.c_str(), key.size());
    const wchar_t* text = g_localize(table, nullptr, &sectionString, &keyString, kStringFile);
    return s_known[id] = text != nullptr ? text : L"";
}

std::wstring Words(const char* section, const std::string& key) {
    void* table = g_installed && g_localizeInto && g_names && g_free ? *g_stringTable.get() : nullptr;
    if (table == nullptr) {
        return L"";
    }
    const auto sectionString = EngineString<char>::Of(section, std::strlen(section));
    const auto keyString = EngineString<char>::Of(key.c_str(), key.size());
    EngineString<wchar_t> text{};
    g_localizeInto(table, nullptr, &text, &sectionString, &keyString, kStringFile);
    EngineString<wchar_t> named{};
    g_names(nullptr, nullptr, &named, text);
    std::wstring words(named.Text(), named.length);
    g_free(&named);
    return words;
}

std::vector<std::string> FoundTapes() {
    std::vector<std::string> keys;
    const void* missions = Service(g_buildMissionsType, g_missionsTypeName.get(), g_missionsType.get());
    if (missions == nullptr) {
        return keys;
    }
    for (const ptrdiff_t list : kTapeLists) {
        const auto* tapes = Read<const uint8_t*>(missions, list);
        const auto count = Read<uint32_t>(missions, list + sizeof(void*));
        for (uint32_t i = 0; i < count; ++i) {
            const uint8_t* tape = tapes + i * kTapeSize;
            if (Read<uint8_t>(tape, kTapeFound) != 0) {
                keys.push_back(ReadString(tape));
            }
        }
    }
    return keys;
}

std::vector<Buddy> Buddies() {
    std::vector<std::pair<int, Buddy>> met;
    void* buddies = Service(g_buildBuddiesType, g_buddiesTypeName.get(), g_buddiesType.get());
    if (buddies == nullptr) {
        return {};
    }
    const auto* records = Read<const uint8_t*>(buddies, kBuddies);
    const auto count = Read<int32_t>(buddies, kBuddyCount);
    const auto* primary = Read<const uint8_t*>(buddies, kPrimaryBuddy);
    const auto* secondary = Read<const uint8_t*>(buddies, kSecondaryBuddy);
    for (int32_t i = 0; i < count; ++i) {
        const uint8_t* record = records + i * kBuddySize;
        if (Read<uint8_t>(record, kBuddyUnlocked) == 0) {
            continue;
        }
        const int status = g_buddyStatus(buddies, nullptr, record);
        const wchar_t* text = g_statusText(status, Read<int32_t>(record, kBuddyGender));
        met.push_back({Read<int32_t>(record, kBuddyHistory),
                       {ReadString(record), static_cast<Status>(status), text != nullptr ? text : L"",
                        record == primary ? 1 : record == secondary ? 2 : 0}});
    }
    std::stable_sort(met.begin(), met.end(), [](const auto& a, const auto& b) { return a.first > b.first; });
    std::vector<Buddy> sorted;
    for (auto& [history, buddy] : met) {
        sorted.push_back(std::move(buddy));
    }
    return sorted;
}

}
