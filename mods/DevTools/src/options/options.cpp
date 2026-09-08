// The options, as data.
#include "engine/keys.h"
#include "options/options.h"

int GetDeveloperConsole();
void SetDeveloperConsole(int value);
int GetInvincibility();
void SetInvincibility(int value);
int GetInfiniteAmmo();
void SetInfiniteAmmo(int value);
int GetUnlockAllWeapons();
void SetUnlockAllWeapons(int value);
int GetDiamonds();
void SetDiamonds(int value);
int GetNoclipKey();
void SetNoclipKey(int value);
int GetFreecamKey();
void SetFreecamKey(int value);
int GetFpsCounter();
void SetFpsCounter(int value);
int GetSkipSystemDetection();
void SetSkipSystemDetection(int value);

namespace {
    using DevTools::Options::Kind;
    using DevTools::Options::Option;

    // Display order, which is also the order they appear in fcse.ini. What each one does is in
    // README.md and in the file that implements it.
    //
    // Four of these default to on, which is the one place DevTools does not leave the game as it
    // shipped: a developer console, a frame rate readout, a shorter launch and a camera key are what
    // this plugin is installed for, so having to switch them on before it is useful is friction with
    // no upside. The cheats all still default to off - those change how the game plays.
    constexpr Option kOptions[] = {
        {"Developer console", Kind::Toggle, 1, 0, 1, &GetDeveloperConsole, &SetDeveloperConsole},
        {"Invincibility", Kind::Toggle, 0, 0, 1, &GetInvincibility, &SetInvincibility},
        {"Infinite ammo", Kind::Toggle, 0, 0, 1, &GetInfiniteAmmo, &SetInfiniteAmmo},
        {"Unlock all weapons", Kind::Toggle, 0, 0, 1, &GetUnlockAllWeapons, &SetUnlockAllWeapons},
        {"Diamonds", Kind::Slider, 0, 0, 999, &GetDiamonds, &SetDiamonds},
        {"Noclip key", Kind::Key, DevTools::Keys::kChoiceOff, 0, 0, &GetNoclipKey, &SetNoclipKey},
        {"Freecam key", Kind::Key, DevTools::Keys::kChoiceF2, 0, 0, &GetFreecamKey, &SetFreecamKey},
        {"FPS counter", Kind::Toggle, 1, 0, 1, &GetFpsCounter, &SetFpsCounter},
        {"Skip system detection", Kind::Toggle, 1, 0, 1, &GetSkipSystemDetection,
         &SetSkipSystemDetection},
    };
}

namespace DevTools::Options {

std::span<const Option> All() { return kOptions; }

}
