#include "dunia_api.h"

#include "log.h"

namespace FCSE {

namespace {
    HMODULE g_module = nullptr;
    uintptr_t g_base = 0;
    size_t g_size = 0;
    RunGameFn g_runGame = nullptr;
    RegisterGameFunctionProviderFn g_registerGameFunctionProvider = nullptr;
    AddFunctionCBFn g_addFunctionCB = nullptr;

    size_t GetModuleFileSize(const std::wstring& path) {
        WIN32_FILE_ATTRIBUTE_DATA data{};
        if (!GetFileAttributesExW(path.c_str(), GetFileExInfoStandard, &data)) {
            return 0;
        }
        ULARGE_INTEGER size;
        size.LowPart = data.nFileSizeLow;
        size.HighPart = data.nFileSizeHigh;
        return static_cast<size_t>(size.QuadPart);
    }
}

bool DuniaApi::Load(const std::wstring& directory) {
    std::wstring path = directory + L"Dunia.dll";

    g_size = GetModuleFileSize(path);
    if (g_size == 0) {
        Log::Loader("Dunia.dll not found next to the loader (expected at bin\\Dunia.dll)");
        return false;
    }

    g_module = LoadLibraryW(path.c_str());
    if (g_module == nullptr) {
        Log::Loader("LoadLibraryW(Dunia.dll) failed, error " + std::to_string(GetLastError()));
        g_size = 0;
        return false;
    }
    g_base = reinterpret_cast<uintptr_t>(g_module);

    g_runGame = reinterpret_cast<RunGameFn>(
        GetProcAddress(g_module, "?RunGame@@YA_NPAUHINSTANCE__@@PBD@Z"));
    g_registerGameFunctionProvider = reinterpret_cast<RegisterGameFunctionProviderFn>(
        GetProcAddress(g_module, "RegisterGameFunctionProvider"));
    g_addFunctionCB =
        reinterpret_cast<AddFunctionCBFn>(GetProcAddress(g_module, "AddFunctionCB"));

    if (g_runGame == nullptr || g_registerGameFunctionProvider == nullptr ||
        g_addFunctionCB == nullptr) {
        Log::Loader("Dunia.dll loaded but is missing one of the 3 required exports "
                    "(RunGame/RegisterGameFunctionProvider/AddFunctionCB) - unsupported build");
        FreeLibrary(g_module);
        g_module = nullptr;
        g_base = 0;
        g_size = 0;
        g_runGame = nullptr;
        g_registerGameFunctionProvider = nullptr;
        g_addFunctionCB = nullptr;
        return false;
    }

    Log::Loader("Dunia.dll resolved, base=0x" + std::to_string(g_base) +
                " size=" + std::to_string(g_size) + " bytes");
    return true;
}

HMODULE DuniaApi::Module() { return g_module; }
uintptr_t DuniaApi::Base() { return g_base; }
size_t DuniaApi::Size() { return g_size; }
RunGameFn DuniaApi::RunGame() { return g_runGame; }
RegisterGameFunctionProviderFn DuniaApi::RegisterGameFunctionProvider() {
    return g_registerGameFunctionProvider;
}
AddFunctionCBFn DuniaApi::AddFunctionCB() { return g_addFunctionCB; }

} // namespace FCSE
