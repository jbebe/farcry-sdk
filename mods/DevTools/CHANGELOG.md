# Changelog

Notable changes to DevTools, loosely following
[Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

### Added
- First release. DevTools is the developer half of what UFCP was carrying, split into a plugin of
  its own: UFCP is what a player installs, this is what a modder or a reverse engineer installs.
- **Savegame launch** fix. `-load <name>.sav` quit to desktop instead of booting straight into the
  save, which is the fastest way to iterate on one.
- **Developer console** option, off by default. Far Cry 2's own console opens on `~`, but roughly 57
  of its commands are marked developer-only and answer "Unknown command" — `load_level`,
  `set_health`, `teleport_to_current_objective`, `aidebugtool`, `console_dump_elements` among them.
  Turning this on lists and runs them. The separate filter that keeps multiplayer-only and
  editor-only commands out of a single-player console is left alone.
- **Invincibility** option, off by default. God mode that holds: the profile flag re-applied every
  frame, a health floor that lifts an already-hurt player back above the failure threshold through
  the engine's own `SetToMax`, and vehicle protection on both damage entry points. The three states
  that park the player below the threshold on purpose - forced failure, buddy rescue, revive
  invulnerability - are left alone, so a rescue still plays out. Drowning stays lethal.
- **Infinite ammo** option, off by default, and the syringe fix that has to come with it. The
  engine's shared item decrementer returns early while the unlimited-ammo flag is set, which made
  healing free as well; the three syringe call sites are told apart by return address and the flag
  is cleared around that one call. This corrects the `cheat_UnlimitedAmmo` catalog row too.
- **Unlock all weapons** option, off by default. The profile flag bypasses the per-weapon unlock
  list only; the two act-tag comparisons in `CWeaponBazaar::IsWeaponUnlocked` are what hold the rest
  of the bazaar back, and both are patched while this is on.
- **Diamonds** option, `0` by default. A target the wallet is topped up to every frame rather than a
  one-off gift: spending comes off the grant first, lowering the setting hands back only what is
  above it, and saves are written with the grant subtracted out - so granted diamonds never become
  real progress. `Cheat_AddDiamonds` cannot say that.
- **Noclip**, off until a key is bound. Physics off and the body flown directly, with the view still
  on the head and the HUD still up. The pause menu, quicksave and quickload are refused while it is
  up. There is no console command for this and never was.
- **Freecam**, off until a key is bound. Activates `Cameras.Camera.Free`, a camera prototype the
  shipped game still carries, still binds F3 and F4 to in its input maps, and never reaches - the
  handlers those bindings name are gone from the retail binary. Correcting a documented dead end:
  the archetype is entity-library data, not a string in `Dunia.dll`, which is why the survey that
  concluded free-fly was editor-only could not see it.
- **FPS counter** option, off by default. Writes the `showFps` global directly, so it persists across
  restarts and is up before the console registers the variable.
- **Skip system detection** option, off by default. Cuts seconds off every launch by refusing WMI and
  caching DxDiag behind one redirected import in `systemdetection.dll`. The one option here that can
  crash on some machines, and the reason it is in DevTools rather than UFCP.
- **Overlay**, on **Home**. The command catalog on screen, with a search box, a category filter, the
  right control for each command's argument, and a note on hover. While it is open the game cannot
  see the mouse or keyboard, so a click is only a click. It draws with Dear ImGui, pinned at
  `v1.91.5`, cloned by CMake at configure time and compiled into the plugin - the first third-party
  dependency this project has taken on, and the reason a first configure now needs a network
  connection.
- **Windows from other plugins** in the overlay. `include/devtools_api.h` lets another FCSE plugin
  add a window with one call. Each time Home opens the overlay the windows are laid out side by side,
  and they can be dragged anywhere after that; each closes on its own, a bar lists the closed ones to
  open again, and closing the last gives the game its input back. A plugin built against a different
  Dear ImGui, or a title already taken, is refused in `fcse.log`. The same header lets a plugin post
  a line to the game's console, which runs at the end of the next frame.
- **Command API**, which the overlay is written against. One call runs a
  console line or a Lua chunk on the game thread, from anywhere, with developer-only commands
  reachable regardless of the option above; a catalog of 277 commands describes what to offer and
  what each takes. Only commands that still reach real code are listed - the survey that established
  which of the engine's 416 do is in the developer console page. Nothing here is a setting, and
  nothing appears in the Mod Configuration Menu.
