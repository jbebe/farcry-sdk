// DSOAL, the DirectSound replacement that brings EAX to a PC without Creative hardware, loaded from
// dsoal\ beside this plugin instead of from bin\.
//
// Windows has already bound Dunia.dll's DSOUND.dll imports to the system DirectSound by the time a
// plugin loads, so each import slot is rewritten to DSOAL's export of the same ordinal or name.
#include "fcse_api.h"

#include <windows.h>

#include <string>
#include <utility>
#include <vector>

namespace {
    // The OpenAL driver goes first: DSOAL loads it by bare name as it initialises, which then finds
    // the module already loaded.
    const wchar_t* const kLoadOrder[] = {L"dsoal-aldrv.dll", L"dsound.dll"};

    HMODULE ModuleAt(const void* address) {
        HMODULE module = nullptr;
        GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                               GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                           static_cast<LPCWSTR>(address), &module);
        return module;
    }

    // With its trailing backslash.
    std::wstring DirectoryOf(HMODULE module) {
        wchar_t path[MAX_PATH];
        const DWORD length = GetModuleFileNameW(module, path, MAX_PATH);
        const std::wstring full(path, length);
        return full.substr(0, full.find_last_of(L'\\') + 1);
    }

    const IMAGE_IMPORT_DESCRIPTOR* DsoundImports(uintptr_t base) {
        const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
        const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS32*>(base + dos->e_lfanew);
        const IMAGE_DATA_DIRECTORY& imports =
            nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];

        for (auto* entry =
                 reinterpret_cast<const IMAGE_IMPORT_DESCRIPTOR*>(base + imports.VirtualAddress);
             entry->Name != 0; ++entry) {
            if (_stricmp(reinterpret_cast<const char*>(base + entry->Name), "DSOUND.dll") == 0) {
                return entry;
            }
        }
        return nullptr;
    }
}

void LoadDsoal() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();
    const uintptr_t base = api->duniaBase;

    const IMAGE_IMPORT_DESCRIPTOR* imports = DsoundImports(base);
    if (imports == nullptr || imports->OriginalFirstThunk == 0) {
        api->Log("DSOAL: Dunia.dll's DirectSound imports were not found - Windows DirectSound stays");
        return;
    }

    auto* slots = reinterpret_cast<IMAGE_THUNK_DATA32*>(base + imports->FirstThunk);
    const auto* names =
        reinterpret_cast<const IMAGE_THUNK_DATA32*>(base + imports->OriginalFirstThunk);

    // A dsound.dll next to the game is someone's own replacement, DSOAL or not.
    if (DirectoryOf(ModuleAt(reinterpret_cast<const void*>(slots->u1.Function))) ==
        DirectoryOf(GetModuleHandleW(nullptr))) {
        api->Log("DSOAL: a dsound.dll in bin\\ already replaces DirectSound - left in charge");
        return;
    }

    const std::wstring directory =
        DirectoryOf(ModuleAt(reinterpret_cast<const void*>(&LoadDsoal))) + L"dsoal\\";
    HMODULE dsoal = nullptr;
    for (const wchar_t* file : kLoadOrder) {
        dsoal = LoadLibraryW((directory + file).c_str());
        if (dsoal == nullptr) {
            FCSE::Logf("DSOAL: %ls could not be loaded (error %lu) - Windows DirectSound stays",
                       file, GetLastError());
            return;
        }
    }

    // Every replacement is found before any slot is written, so the game never mixes the two.
    std::vector<std::pair<DWORD*, FARPROC>> redirects;
    for (; names->u1.AddressOfData != 0; ++names, ++slots) {
        const char* function =
            IMAGE_SNAP_BY_ORDINAL32(names->u1.Ordinal)
                ? MAKEINTRESOURCEA(IMAGE_ORDINAL32(names->u1.Ordinal))
                : reinterpret_cast<const IMAGE_IMPORT_BY_NAME*>(base + names->u1.AddressOfData)
                      ->Name;
        const FARPROC replacement = GetProcAddress(dsoal, function);
        if (replacement == nullptr) {
            api->Log("DSOAL: it lacks a function Dunia.dll imports - Windows DirectSound stays");
            return;
        }
        redirects.emplace_back(&slots->u1.Function, replacement);
    }

    for (const auto& [slot, replacement] : redirects) {
        if (!api->Patch(slot, &replacement, sizeof(replacement))) {
            return;
        }
    }
    FCSE::Logf("DSOAL: %zu DirectSound imports now go through %ls", redirects.size(),
               directory.c_str());
}
