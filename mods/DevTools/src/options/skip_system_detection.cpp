// Skip system detection.
//
// Far Cry 2 probes the machine before it reaches the menu, and the probe is slow: bin\systemdetection.dll
// spins up WMI to enumerate the hardware and DxDiag to enumerate the display, and both are worth
// seconds on every launch. Nothing downstream needs the answers unless the graphics settings are
// being auto-detected, which happens once on a fresh profile.
//
// The probe is not Dunia's code, so nothing here is a Dunia pattern. systemdetection.dll reaches COM
// through its imported ole32!CoCreateInstance, so the whole feature is one IAT slot in one module,
// which leaves every other caller of CoCreateInstance in the process untouched.
//
// WMI is refused outright with REGDB_E_CLASSNOTREG, which the probe already copes with. DxDiag
// cannot be, because the startup path needs a provider back - so it is proxied instead: the real
// provider is created once, initialised once, its root container fetched once, and every later
// caller is handed that cached instance rather than paying for a fresh enumeration.
//
// This is the one option here that can crash on some machines, which is why it is in DevTools rather
// than UFCP: seconds off a hundred relaunches is worth a risk no player should be asked to take.
#include "fcse_api.h"

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <objbase.h>

#include <cstdint>

namespace {
    constexpr GUID kUnknown = {0x00000000, 0x0000, 0x0000, {0xC0, 0x00, 0, 0, 0, 0, 0, 0x46}};
    constexpr GUID kDxDiagProvider = {
        0xA65B8071, 0x3BFE, 0x4213, {0x9A, 0x5B, 0x49, 0x1D, 0xA4, 0x46, 0x1C, 0xA7}};
    constexpr GUID kIDxDiagProvider = {
        0x9C6B4CB0, 0x23F8, 0x49CC, {0xA3, 0xED, 0x45, 0xA5, 0x50, 0x00, 0xA6, 0xD2}};
    constexpr GUID kWbemLocator = {
        0x4590F811, 0x1D3A, 0x11D0, {0x89, 0x1F, 0x00, 0xAA, 0x00, 0x4B, 0x2E, 0x24}};

    // As much of IDxDiagProvider as the proxy needs, so dxdiag.h stays out of the build.
    struct DxDiagProvider;
    struct DxDiagProviderVtbl {
        HRESULT(__stdcall* QueryInterface)(DxDiagProvider*, REFIID, void**);
        ULONG(__stdcall* AddRef)(DxDiagProvider*);
        ULONG(__stdcall* Release)(DxDiagProvider*);
        HRESULT(__stdcall* Initialize)(DxDiagProvider*, void*);
        HRESULT(__stdcall* GetRootContainer)(DxDiagProvider*, void**);
    };
    struct DxDiagProvider {
        const DxDiagProviderVtbl* lpVtbl;
    };

    bool g_skip = false;

    DxDiagProvider* g_realProvider = nullptr;
    IUnknown* g_cachedRoot = nullptr;
    bool g_providerInitialised = false;
    LONG g_proxyRefs = 0;

    HRESULT __stdcall ProxyQueryInterface(DxDiagProvider* self, REFIID riid, void** out) {
        if (out == nullptr) {
            return E_POINTER;
        }

        if (IsEqualGUID(riid, kUnknown) || IsEqualGUID(riid, kIDxDiagProvider)) {
            InterlockedIncrement(&g_proxyRefs);
            *out = self;
            return S_OK;
        }

        *out = nullptr;
        return E_NOINTERFACE;
    }

    ULONG __stdcall ProxyAddRef(DxDiagProvider*) {
        return static_cast<ULONG>(InterlockedIncrement(&g_proxyRefs));
    }

    // The real provider is never released, so a later caller reuses the initialised instance.
    ULONG __stdcall ProxyRelease(DxDiagProvider*) {
        const LONG refs = InterlockedDecrement(&g_proxyRefs);
        return refs > 0 ? static_cast<ULONG>(refs) : 0;
    }

    HRESULT __stdcall ProxyInitialize(DxDiagProvider*, void* params) {
        if (g_providerInitialised) {
            return S_OK;
        }

        const HRESULT hr = g_realProvider->lpVtbl->Initialize(g_realProvider, params);
        g_providerInitialised = SUCCEEDED(hr);
        return hr;
    }

    HRESULT __stdcall ProxyGetRootContainer(DxDiagProvider*, void** out) {
        if (out == nullptr) {
            return E_POINTER;
        }

        if (g_cachedRoot == nullptr) {
            const HRESULT hr = g_realProvider->lpVtbl->GetRootContainer(
                g_realProvider, reinterpret_cast<void**>(&g_cachedRoot));
            if (FAILED(hr)) {
                return hr;
            }
        }

        // Its own reference, so each caller's Release stays balanced.
        g_cachedRoot->AddRef();
        *out = g_cachedRoot;
        return S_OK;
    }

    const DxDiagProviderVtbl kProxyVtbl = {ProxyQueryInterface, ProxyAddRef, ProxyRelease,
                                           ProxyInitialize, ProxyGetRootContainer};
    DxDiagProvider g_proxyProvider = {&kProxyVtbl};

    using CoCreateInstanceFn = HRESULT(__stdcall*)(REFCLSID, LPUNKNOWN, DWORD, REFIID, LPVOID*);
    CoCreateInstanceFn g_originalCoCreateInstance = nullptr;

    HRESULT __stdcall CoCreateInstanceDetour(REFCLSID clsid, LPUNKNOWN outer, DWORD context,
                                             REFIID riid, LPVOID* out) {
        if (g_skip && out != nullptr) {
            if (IsEqualGUID(clsid, kWbemLocator)) {
                return REGDB_E_CLASSNOTREG;
            }

            if (IsEqualGUID(clsid, kDxDiagProvider) && IsEqualGUID(riid, kIDxDiagProvider)) {
                if (g_realProvider == nullptr) {
                    const HRESULT hr = g_originalCoCreateInstance(
                        clsid, outer, context, riid, reinterpret_cast<void**>(&g_realProvider));
                    if (FAILED(hr)) {
                        g_realProvider = nullptr;
                        return hr;
                    }

                    // The caller's CoUninitialize would unload dxdiagn.dll under the cached pointers.
                    if (CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED) == RPC_E_CHANGED_MODE) {
                        CoInitializeEx(nullptr, COINIT_MULTITHREADED);
                    }
                }

                InterlockedIncrement(&g_proxyRefs);
                *out = &g_proxyProvider;
                return S_OK;
            }
        }

        return g_originalCoCreateInstance(clsid, outer, context, riid, out);
    }

    // Redirects one named import of one module, leaving every other caller in the process alone.
    bool HookImport(void* moduleBase, const char* fromModule, const char* function, void* detour,
                    void** original) {
        auto* base = static_cast<uint8_t*>(moduleBase);
        auto* dos = reinterpret_cast<IMAGE_DOS_HEADER*>(base);
        if (base == nullptr || dos->e_magic != IMAGE_DOS_SIGNATURE) {
            return false;
        }

        auto* nt = reinterpret_cast<IMAGE_NT_HEADERS*>(base + dos->e_lfanew);
        if (nt->Signature != IMAGE_NT_SIGNATURE) {
            return false;
        }

        const IMAGE_DATA_DIRECTORY& dir =
            nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
        if (dir.VirtualAddress == 0) {
            return false;
        }

        for (auto* import = reinterpret_cast<IMAGE_IMPORT_DESCRIPTOR*>(base + dir.VirtualAddress);
             import->Name != 0; ++import) {
            if (lstrcmpiA(reinterpret_cast<const char*>(base + import->Name), fromModule) != 0) {
                continue;
            }

            // Names come from OriginalFirstThunk, or from FirstThunk when it is absent.
            auto* slot = reinterpret_cast<IMAGE_THUNK_DATA*>(base + import->FirstThunk);
            auto* name = reinterpret_cast<IMAGE_THUNK_DATA*>(
                base +
                (import->OriginalFirstThunk != 0 ? import->OriginalFirstThunk : import->FirstThunk));

            for (; name->u1.AddressOfData != 0; ++name, ++slot) {
                if (IMAGE_SNAP_BY_ORDINAL(name->u1.Ordinal)) {
                    continue;
                }

                auto* imported =
                    reinterpret_cast<IMAGE_IMPORT_BY_NAME*>(base + name->u1.AddressOfData);
                if (lstrcmpA(reinterpret_cast<const char*>(imported->Name), function) != 0) {
                    continue;
                }

                *original = reinterpret_cast<void*>(slot->u1.Function);

                DWORD old = 0;
                if (!VirtualProtect(&slot->u1.Function, sizeof(slot->u1.Function), PAGE_READWRITE,
                                    &old)) {
                    return false;
                }

                slot->u1.Function = reinterpret_cast<uintptr_t>(detour);
                VirtualProtect(&slot->u1.Function, sizeof(slot->u1.Function), old, &old);
                return true;
            }
        }

        return false;
    }

    struct LdrUnicodeString {
        USHORT Length;
        USHORT MaximumLength;
        wchar_t* Buffer;
    };

    struct LdrDllLoadedNotificationData {
        ULONG Flags;
        const LdrUnicodeString* FullDllName;
        const LdrUnicodeString* BaseDllName;
        void* DllBase;
        ULONG SizeOfImage;
    };

    constexpr ULONG kLdrDllNotificationReasonLoaded = 1;

    using LdrNotificationFn = void(__stdcall*)(ULONG, const LdrDllLoadedNotificationData*, void*);
    using LdrRegisterFn = LONG(__stdcall*)(ULONG, LdrNotificationFn, void*, void**);

    // The loader hands over a counted string, which need not be terminated. `wanted` is lower case.
    bool NameIs(const LdrUnicodeString* name, const wchar_t* wanted, size_t wantedLength) {
        if (name == nullptr || name->Buffer == nullptr ||
            name->Length != wantedLength * sizeof(wchar_t)) {
            return false;
        }

        for (size_t i = 0; i < wantedLength; ++i) {
            wchar_t c = name->Buffer[i];
            if (c >= L'A' && c <= L'Z') {
                c = static_cast<wchar_t>(c - L'A' + L'a');
            }
            if (c != wanted[i]) {
                return false;
            }
        }
        return true;
    }

    // Runs under the loader lock, so it touches nothing but the image the loader just handed it.
    void __stdcall OnDllLoaded(ULONG reason, const LdrDllLoadedNotificationData* data, void*) {
        constexpr wchar_t kName[] = L"systemdetection.dll";
        constexpr size_t kNameLength = (sizeof(kName) / sizeof(kName[0])) - 1;

        if (reason != kLdrDllNotificationReasonLoaded || !g_skip ||
            g_originalCoCreateInstance != nullptr ||
            !NameIs(data->BaseDllName, kName, kNameLength)) {
            return;
        }

        HookImport(data->DllBase, "ole32.dll", "CoCreateInstance", &CoCreateInstanceDetour,
                   reinterpret_cast<void**>(&g_originalCoCreateInstance));
    }
}

// Watches for the probe's DLL, which Dunia loads well after FCSE_Load, once InitDuniaEngine is
// already running. Nothing is redirected unless the setting is on by the time it arrives.
void InstallSkipSystemDetectionHook() {
    const HMODULE ntdll = GetModuleHandleW(L"ntdll.dll");
    const auto registerNotification = reinterpret_cast<LdrRegisterFn>(
        ntdll != nullptr ? GetProcAddress(ntdll, "LdrRegisterDllNotification") : nullptr);

    void* cookie = nullptr;
    if (registerNotification != nullptr) {
        registerNotification(0, &OnDllLoaded, nullptr, &cookie);
    }
}

int GetSkipSystemDetection() { return g_skip ? 1 : 0; }

void SetSkipSystemDetection(int value) {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if ((value != 0) == g_skip) {
        return;
    }

    g_skip = value != 0;

    // The probe runs once, early. A change made in-game is kept in fcse.ini and applies next launch.
    if (!g_skip) {
        api->Log("skip system detection: off - the hardware probe runs as it shipped");
    } else if (g_originalCoCreateInstance != nullptr) {
        api->Log("skip system detection: on - WMI will be refused and DxDiag cached");
    } else {
        api->Log("skip system detection: on - takes effect on the next launch");
    }
}
