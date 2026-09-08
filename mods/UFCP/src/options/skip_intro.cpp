// Skip the Ubisoft, Dunia and rating screens.
//
// Ported from FC2JackalFix (MIT, (c) 2026 Joshhhuaaa, TGP482) - source/gameplay/skipintro.ixx.
//
// The engine already knows how to skip them: CFCXSplashPage::Update carries a SkipIntroMovies path,
// reached from the profile flag of that name. The branch that chooses between the two is
//
//     76 11    jbe +11h    ; play the movie
//
// and NOPping it takes the skip path unconditionally. The profile flag is left alone, so a player
// who set it themselves is unaffected either way.
//
// This is a preference rather than a fix: the intro is what the game shipped doing, and the default
// here leaves it playing.
#include "fcse_api.h"

#include "engine/toggle_patch.h"

#include <cstdint>

namespace {
    // The branch is 21 bytes into the match.
    constexpr ptrdiff_t kMovieBranch = 21;

    constexpr uint8_t kNopBranch[] = {0x90, 0x90};

    FCSE::Relocation<uint8_t*> g_splashUpdate{FCSE::Pattern(
        "80 BE 64 01 00 00 00 75 1B A1 ?? ?? ?? ?? 83 B8 90 00 00 00 00 76 11")};

    UFCP::TogglePatch g_patch{kNopBranch};
}

void InstallSkipIntroHook() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!g_splashUpdate) {
        api->Log("skip intro: the splash page's movie branch was not found in this build - the "
                 "intro cannot be skipped");
        return;
    }

    g_patch.Resolve(g_splashUpdate.address() + kMovieBranch);
}

void __cdecl OnSkipIntroChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
    g_patch.Set(value->asCheckbox);
    FCSE::ApiPointer()->Log(value->asCheckbox ? "skip intro: on" : "skip intro: off");
}
