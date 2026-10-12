// Skip the Ubisoft, Dunia and rating screens.
//
// Ported from FC2JackalFix (MIT, (c) 2026 Joshhhuaaa, TGP482) - source/gameplay/skipintro.ixx.
//
// The engine already knows how to skip them: CFCXSplashPage::Update carries a SkipIntroMovies path,
// reached from the profile flag of that name. The branch that chooses between the two is
//
//     76 11    jbe +11h    ; play the movie
//
// and NOPping it takes the skip path unconditionally. The profile flag is left alone.
#include "fcse_api.h"

#include <cstdint>

namespace {
    // The branch is 21 bytes into the match.
    constexpr ptrdiff_t kMovieBranch = 21;

    constexpr uint8_t kNopBranch[] = {0x90, 0x90};

    FCSE::Relocation<uint8_t*> g_splashUpdate{FCSE::Pattern(
        "80 BE 64 01 00 00 00 75 1B A1 ?? ?? ?? ?? 83 B8 90 00 00 00 00 76 11")};
}

void ApplySkipIntroFix() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!g_splashUpdate) {
        api->Log("skip intro: the splash page's movie branch was not found in this build - the "
                 "intro plays");
        return;
    }

    if (api->Patch(g_splashUpdate.get() + kMovieBranch, kNopBranch, sizeof(kNopBranch))) {
        api->Log("intro videos skipped");
    }
}
