#include "engine/splash.h"

#include "engine/dunia_api.h"
#include "log.h"

#include <cstdint>
#include <cstring>
#include <string>
#include <windows.h>

namespace FCSE {

namespace {

    constexpr char kGdiPlusModule[] = "gdiplus.dll";
    constexpr char kGdiPlusImport[] = "GdipCreateHBITMAPFromBitmap";

    using GdipCreateHBITMAPFromBitmapFn = int(__stdcall*)(void* bitmap, HBITMAP* out,
                                                          DWORD background);
    constexpr int kGdiPlusOk = 0;

    struct DetouredImport {
        uintptr_t* slot = nullptr;
        uintptr_t original = 0;

        void Restore();
    };

    DetouredImport g_createHBitmap;

    uintptr_t* FindImportSlot(uintptr_t base, const char* moduleName, const char* functionName) {
        const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
        if (dos->e_magic != IMAGE_DOS_SIGNATURE) {
            return nullptr;
        }
        const auto* headers = reinterpret_cast<const IMAGE_NT_HEADERS32*>(base + dos->e_lfanew);
        if (headers->Signature != IMAGE_NT_SIGNATURE) {
            return nullptr;
        }

        const IMAGE_DATA_DIRECTORY& imports =
            headers->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
        if (imports.VirtualAddress == 0 || imports.Size == 0) {
            return nullptr;
        }

        for (const auto* descriptor =
                 reinterpret_cast<const IMAGE_IMPORT_DESCRIPTOR*>(base + imports.VirtualAddress);
             descriptor->Name != 0; ++descriptor) {
            if (_stricmp(reinterpret_cast<const char*>(base + descriptor->Name), moduleName) != 0) {
                continue;
            }

            if (descriptor->OriginalFirstThunk == 0) {
                return nullptr;
            }

            const auto* names =
                reinterpret_cast<const IMAGE_THUNK_DATA32*>(base + descriptor->OriginalFirstThunk);
            auto* slots = reinterpret_cast<uintptr_t*>(base + descriptor->FirstThunk);
            for (; names->u1.AddressOfData != 0; ++names, ++slots) {
                if (IMAGE_SNAP_BY_ORDINAL32(names->u1.Ordinal)) {
                    continue;
                }
                const auto* imported =
                    reinterpret_cast<const IMAGE_IMPORT_BY_NAME*>(base + names->u1.AddressOfData);
                if (strcmp(reinterpret_cast<const char*>(imported->Name), functionName) == 0) {
                    return slots;
                }
            }
            return nullptr;
        }
        return nullptr;
    }

    bool WriteImportSlot(uintptr_t* slot, uintptr_t value) {
        DWORD previous = 0;
        if (!VirtualProtect(slot, sizeof(*slot), PAGE_READWRITE, &previous)) {
            return false;
        }
        *slot = value;
        VirtualProtect(slot, sizeof(*slot), previous, &previous);
        return true;
    }

    void DetouredImport::Restore() {
        if (slot != nullptr && original != 0) {
            WriteImportSlot(slot, original);
        }
        slot = nullptr;
    }

    void Desaturate(HBITMAP bitmap) {
        DIBSECTION dib{};
        if (GetObjectW(bitmap, sizeof(dib), &dib) != sizeof(dib)) {
            Log::Loader("Splash: GDI+ returned a bitmap that is not a DIB section - leaving the "
                        "splash in colour");
            return;
        }
        if (dib.dsBm.bmBits == nullptr || dib.dsBm.bmBitsPixel != 32 ||
            dib.dsBmih.biCompression != BI_RGB) {
            Log::Loader("Splash: unexpected splash bitmap format (" +
                        std::to_string(dib.dsBm.bmBitsPixel) + "bpp, compression " +
                        std::to_string(dib.dsBmih.biCompression) +
                        ") - leaving the splash in colour");
            return;
        }

        GdiFlush();

        auto* pixels = static_cast<uint8_t*>(dib.dsBm.bmBits);
        const int height = dib.dsBm.bmHeight < 0 ? -dib.dsBm.bmHeight : dib.dsBm.bmHeight;
        for (int y = 0; y < height; ++y) {
            uint8_t* pixel = pixels + static_cast<size_t>(y) * dib.dsBm.bmWidthBytes;
            for (int x = 0; x < dib.dsBm.bmWidth; ++x, pixel += 4) {
                const uint32_t luma = (pixel[2] * 77u + pixel[1] * 150u + pixel[0] * 29u) >> 8;
                pixel[0] = pixel[1] = pixel[2] = static_cast<uint8_t>(luma);
            }
        }

        Log::Loader("Splash: recoloured to black and white (" + std::to_string(dib.dsBm.bmWidth) +
                    "x" + std::to_string(height) + ")");
    }

    int __stdcall CreateHBitmapDetour(void* bitmap, HBITMAP* out, DWORD background) {
        const GdipCreateHBITMAPFromBitmapFn original =
            reinterpret_cast<GdipCreateHBITMAPFromBitmapFn>(g_createHBitmap.original);
        const int status = original(bitmap, out, background);

        g_createHBitmap.Restore();

        if (status == kGdiPlusOk && out != nullptr && *out != nullptr) {
            Desaturate(*out);
        }
        return status;
    }

} // namespace

bool Splash::Install() {
    const uintptr_t base = DuniaApi::Base();
    if (base == 0) {
        Log::Loader("Splash: Dunia.dll not resolved yet, cannot patch its import table");
        return false;
    }

    uintptr_t* slot = FindImportSlot(base, kGdiPlusModule, kGdiPlusImport);
    if (slot == nullptr) {
        Log::Loader("Splash: this Dunia.dll does not import " + std::string(kGdiPlusModule) + "!" +
                    kGdiPlusImport + " - splash left as-is");
        return false;
    }
    if (*slot == 0) {
        Log::Loader("Splash: the " + std::string(kGdiPlusImport) +
                    " import slot is empty - splash left as-is");
        return false;
    }

    g_createHBitmap.original = *slot;
    if (!WriteImportSlot(slot, reinterpret_cast<uintptr_t>(&CreateHBitmapDetour))) {
        Log::Loader("Splash: could not make the " + std::string(kGdiPlusImport) +
                    " import slot writable - splash left as-is");
        g_createHBitmap.original = 0;
        return false;
    }
    g_createHBitmap.slot = slot;

    Log::Loader("Splash: detour installed on " + std::string(kGdiPlusModule) + "!" +
                kGdiPlusImport);
    return true;
}

} // namespace FCSE
