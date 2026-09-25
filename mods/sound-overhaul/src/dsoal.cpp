// Loads DSOAL from dsoal\ beside this plugin and points Dunia.dll's DirectSound imports at it.
#include "fcse_api.h"

#include <windows.h>

#include <algorithm>
#include <string>
#include <vector>

extern "C" IMAGE_DOS_HEADER __ImageBase;

namespace {
    constexpr WORD kEaxDirectSoundCreate8 = 6;
    constexpr WORD kDirectSoundCreate8 = 11;

    // With its trailing backslash.
    std::wstring DirectoryOf(HMODULE module) {
        wchar_t path[MAX_PATH];
        const DWORD length = GetModuleFileNameW(module, path, MAX_PATH);
        const std::wstring full(path, length);
        return full.substr(0, full.find_last_of(L'\\') + 1);
    }

    // The directory of the module holding `address`, or empty when no module does.
    std::wstring DirectoryAt(uintptr_t address) {
        HMODULE module = nullptr;
        if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                                    GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                                reinterpret_cast<LPCWSTR>(address), &module)) {
            return {};
        }
        return DirectoryOf(module);
    }

    bool IsSystemDirectory(const std::wstring& directory) {
        wchar_t system[MAX_PATH];
        const UINT length = GetSystemDirectoryW(system, MAX_PATH);
        return _wcsicmp(directory.c_str(), (std::wstring(system, length) + L"\\").c_str()) == 0;
    }

    const IMAGE_IMPORT_DESCRIPTOR* ImportsOf(uintptr_t base, const char* dll) {
        const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
        const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS32*>(base + dos->e_lfanew);
        const IMAGE_DATA_DIRECTORY& imports =
            nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];

        for (auto* entry =
                 reinterpret_cast<const IMAGE_IMPORT_DESCRIPTOR*>(base + imports.VirtualAddress);
             entry->Name != 0; ++entry) {
            if (_stricmp(reinterpret_cast<const char*>(base + entry->Name), dll) == 0) {
                return entry->OriginalFirstThunk != 0 ? entry : nullptr;
            }
        }
        return nullptr;
    }

    // The ordinal each slot imports, in slot order; 0 for an import by name.
    std::vector<WORD> OrdinalsOf(uintptr_t base, const IMAGE_IMPORT_DESCRIPTOR& imports) {
        std::vector<WORD> ordinals;
        for (auto* name = reinterpret_cast<const IMAGE_THUNK_DATA32*>(base + imports.OriginalFirstThunk);
             name->u1.AddressOfData != 0; ++name) {
            ordinals.push_back(IMAGE_SNAP_BY_ORDINAL32(name->u1.Ordinal)
                                   ? static_cast<WORD>(IMAGE_ORDINAL32(name->u1.Ordinal))
                                   : 0);
        }
        return ordinals;
    }
}

void LoadDsoal() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();
    const uintptr_t base = api->duniaBase;

    const IMAGE_IMPORT_DESCRIPTOR* dsound = ImportsOf(base, "DSOUND.dll");
    const IMAGE_IMPORT_DESCRIPTOR* eax = ImportsOf(base, "EAX.DLL");
    if (dsound == nullptr || eax == nullptr) {
        api->Log("DSOAL: Dunia.dll's DirectSound imports were not found - Windows DirectSound stays");
        return;
    }

    auto* dsoundSlots = reinterpret_cast<DWORD*>(base + dsound->FirstThunk);
    const std::wstring bound = DirectoryAt(*dsoundSlots);
    if (!IsSystemDirectory(bound)) {
        FCSE::Logf("DSOAL: DirectSound is already replaced (%ls) - left in charge",
                   bound.empty() ? L"not by a module" : bound.c_str());
        return;
    }

    const std::wstring directory =
        DirectoryOf(reinterpret_cast<HMODULE>(&__ImageBase)) + L"dsoal\\";
    const auto load = [&directory](const wchar_t* file) {
        HMODULE module = LoadLibraryW((directory + file).c_str());
        if (module == nullptr) {
            FCSE::Logf("DSOAL: %ls could not be loaded (error %lu) - Windows DirectSound stays",
                       file, GetLastError());
        }
        return module;
    };

    // OpenAL Soft reads its settings from beside DSOAL, unless the player names a file of their own.
    if (GetEnvironmentVariableW(L"ALSOFT_CONF", nullptr, 0) == 0) {
        SetEnvironmentVariableW(L"ALSOFT_CONF", (directory + L"alsoft.ini").c_str());
    }

    // The OpenAL driver first: DSOAL loads it by bare name as it initialises.
    const HMODULE dsoal = load(L"dsoal-aldrv.dll") ? load(L"dsound.dll") : nullptr;
    if (dsoal == nullptr) {
        return;
    }

    // DSOUND.dll's slots take DSOAL's export of the same ordinal. They are contiguous, so one write
    // redirects them all or none.
    std::vector<FARPROC> replacements;
    for (const WORD ordinal : OrdinalsOf(base, *dsound)) {
        replacements.push_back(ordinal != 0 ? GetProcAddress(dsoal, MAKEINTRESOURCEA(ordinal))
                                            : nullptr);
    }

    // DARE tries EAXDirectSoundCreate8 first, and without Creative's drivers it builds a Windows
    // DirectSound through COM. DSOAL's DirectSoundCreate8 takes the same arguments.
    const std::vector<WORD> eaxOrdinals = OrdinalsOf(base, *eax);
    const auto eaxCreate = std::find(eaxOrdinals.begin(), eaxOrdinals.end(), kEaxDirectSoundCreate8);
    const FARPROC create = GetProcAddress(dsoal, MAKEINTRESOURCEA(kDirectSoundCreate8));

    if (std::find(replacements.begin(), replacements.end(), nullptr) != replacements.end() ||
        eaxCreate == eaxOrdinals.end() || create == nullptr) {
        api->Log("DSOAL: an import could not be matched to it - Windows DirectSound stays");
        return;
    }

    DWORD* eaxCreateSlot =
        reinterpret_cast<DWORD*>(base + eax->FirstThunk) + (eaxCreate - eaxOrdinals.begin());
    if (api->Patch(dsoundSlots, replacements.data(), replacements.size() * sizeof(FARPROC)) &&
        api->Patch(eaxCreateSlot, &create, sizeof(create))) {
        FCSE::Logf("DSOAL: DirectSound and EAX.DLL's device creation now go through %ls",
                   directory.c_str());
    }
}
