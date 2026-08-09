# FCSE plugins

This folder is the whole plugin-facing surface of FCSE: [`plugin_api.h`](plugin_api.h) is the entire
ABI, it has no dependency on anything else in this tree, and copying that one file into your own
project is the whole setup. Below is what a player does with the result.

## Installing plugins

Drop plugin `.dll` files directly in the game's `bin\plugins\` folder, then launch `FCSE.exe`
instead of `FarCry2.exe`. Nothing else to configure - every `.dll` there is loaded automatically on
the next launch. Lua mods live in that same folder and are found the same way, so installing a mod
is one instruction whichever language it is written in.

Check `bin\fcse.log` after launching to confirm what happened: which plugins were found and
loaded, what each one registered/hooked/patched, and whether anything conflicted with another
installed plugin (if two plugins both try to claim the same thing, the loser is named in the log,
not silently ignored).

## Configuring plugins

Plugins that expose settings get a group in `bin\fcse.ini`, named after the plugin and created on
the first launch after you install it. Edit values there (changes apply on the next launch), or
change them in-game from the Mod Configuration Menu under Options, which writes back to the same
file.

A plugin with no group simply doesn't have any settings to configure. Groups belonging to plugins
you've since removed are left alone rather than deleted, so uninstalling a plugin for a while
doesn't lose how you had it set up.

## Writing a plugin

See [`plugin_api.h`](plugin_api.h) for the full API (documented inline) and
[`../example_plugin/example_plugin.cpp`](../example_plugin/example_plugin.cpp) for a minimal
working one covering all four tiers of the API: overriding a named engine callback, detouring a
function, patching bytes directly, and registering a persistent setting. See
[`../README.md`](../README.md) for how the loader itself works and why the API is shaped the way it
is.
