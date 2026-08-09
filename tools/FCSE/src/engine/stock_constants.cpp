#include "engine/stock_constants.h"

#include "log.h"

#include <windows.h>

namespace FCSE {

namespace {
    constexpr uintptr_t kPreferredImageBase = 0x00400000;
    constexpr uintptr_t kMalariaCurveVa = 0x004020fc;
    constexpr uintptr_t kPlayerSPFinalizeVa = 0x00402100;
}

StockConstants LoadStockConstants(const std::wstring& directory) {
    StockConstants result;

    std::wstring path = directory + L"FarCry2.exe";
    HMODULE hExe = LoadLibraryExW(path.c_str(), nullptr, DONT_RESOLVE_DLL_REFERENCES);
    if (hExe == nullptr) {
        Log::Loader("FarCry2.exe not found next to the loader - MalariaCurve/PlayerSPFinalize "
                     "will use inert fallback constants instead of the real stock values "
                     "(multiplier 1.0, value 0)");
        return result;
    }

    // DONT_RESOLVE_DLL_REFERENCES can set the low bit of the returned handle in some cases
    // (image-as-datafile ambiguity); mask it off before treating this as a real base address.
    uintptr_t base = reinterpret_cast<uintptr_t>(hExe) & ~static_cast<uintptr_t>(1);

    const float* malariaCurve =
        reinterpret_cast<const float*>(base + (kMalariaCurveVa - kPreferredImageBase));
    const int32_t* playerSPFinalize =
        reinterpret_cast<const int32_t*>(base + (kPlayerSPFinalizeVa - kPreferredImageBase));

    result.malariaCurveMultiplier = *malariaCurve;
    result.playerSPFinalizeValue = *playerSPFinalize;

    Log::Loader("Read stock constants from FarCry2.exe: MalariaCurve multiplier=" +
                std::to_string(result.malariaCurveMultiplier) +
                ", PlayerSPFinalize value=" + std::to_string(result.playerSPFinalizeValue));

    FreeLibrary(hExe);
    return result;
}

} // namespace FCSE
