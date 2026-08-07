# Installing FCSE plugins

Drop plugin `.dll` files directly in this folder (`bin\plugins\`), then launch `FCSE.exe` instead
of `FarCry2.exe`. Nothing else to configure - every `.dll` here is loaded automatically on the next
launch.

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

See [`../include/plugin_api.h`](../include/plugin_api.h) for the full API (copy that single header
into your own project - it has no other dependency) and
[`../example_plugin/example_plugin.cpp`](../example_plugin/example_plugin.cpp) for a minimal
working one covering all four tiers of the API: overriding a named engine callback, detouring a
function, patching bytes directly, and registering a persistent setting. See
[`../README.md`](../README.md) for how the loader itself works and why the API is shaped the way it
is.
