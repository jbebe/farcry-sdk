#include "engine/build_id.h"

#include "log.h"
#include "util/pe_image.h"

namespace FCSE {

namespace {

    struct KnownBuild {
        uint32_t timeDateStamp;
        uint32_t sizeOfImage;
        DuniaBuild build;
        bool supported;
        const char* id;
        const char* label;
        const char* reason;
    };

    // Measured 2026-08-10 across six sampled DLLs (DVD, GOG, Steam, and three
    // copies of ambiguous provenance), which collapsed to exactly three
    // distinct address spaces. (TimeDateStamp, SizeOfImage) separates all three
    // and both fields are in the mapped headers, so identification costs two
    // loads and no file I/O.
    constexpr KnownBuild kKnownBuilds[] = {
        {0x4AAE9636u, 27164672u, DuniaBuild::Uplay103, true,
         "fc2_103_uplay", "Far Cry 2 v1.03 (Steam / Ubisoft Connect)", ""},
        {0x49FB4BF6u, 26386432u, DuniaBuild::Retail103, true,
         "fc2_103_retail", "Far Cry 2 v1.03 (GOG / patched retail)", ""},
        {0x48E298DBu, 25702400u, DuniaBuild::Retail100, false,
         "fc2_100_retail", "Far Cry 2 v1.00 (unpatched retail/DVD)",
         "This is the unpatched 1.00 release. FCSE supports v1.03 only - "
         "install the official 1.03 patch and try again."},
    };

    std::string Hex32(uint32_t value) {
        char buf[11] = {'0', 'x'};
        for (int i = 9; i >= 2; --i) {
            buf[i] = "0123456789ABCDEF"[value & 0xF];
            value >>= 4;
        }
        buf[10] = '\0';
        return std::string(buf);
    }

    std::string Describe(const BuildInfo& info) {
        return "TimeDateStamp=" + Hex32(info.timeDateStamp) +
               ", SizeOfImage=" + std::to_string(info.sizeOfImage);
    }

} // namespace

const char* ToString(DuniaBuild build) {
    switch (build) {
        case DuniaBuild::Retail103: return "fc2_103_retail";
        case DuniaBuild::Uplay103:  return "fc2_103_uplay";
        case DuniaBuild::Retail100: return "fc2_100_retail";
        default:                    return "unknown";
    }
}

BuildInfo IdentifyDuniaBuild(HMODULE duniaModule) {
    BuildInfo info;

    const IMAGE_NT_HEADERS32* nt = PeHeaders(duniaModule);
    if (nt == nullptr) {
        info.reason = "Dunia.dll does not have readable PE headers in memory.";
        Log::Loader("build detection: " + info.reason);
        return info;
    }

    info.timeDateStamp = nt->FileHeader.TimeDateStamp;
    info.sizeOfImage = nt->OptionalHeader.SizeOfImage;

    for (const KnownBuild& known : kKnownBuilds) {
        if (known.timeDateStamp != info.timeDateStamp ||
            known.sizeOfImage != info.sizeOfImage) {
            continue;
        }
        info.build = known.build;
        info.supported = known.supported;
        info.id = known.id;
        info.label = known.label;
        info.reason = known.reason;
        Log::Loader(std::string("build detection: ") + known.label + " (" +
                    known.id + ", " + Describe(info) + ")");
        return info;
    }

    // Report the measurement, not just the failure: these two numbers are
    // everything needed to add the build to kKnownBuilds and regenerate a
    // mapping for it.
    info.reason = "This Dunia.dll is not a build FCSE knows about (" +
                  Describe(info) +
                  "). FCSE supports Far Cry 2 v1.03 - the Steam, Ubisoft Connect "
                  "and GOG releases. If yours is a legitimate v1.03 install, "
                  "please report those two numbers.";
    Log::Loader("build detection: unrecognised Dunia.dll, " + Describe(info));
    return info;
}

} // namespace FCSE
