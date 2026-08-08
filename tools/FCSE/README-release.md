# FCSE (Far Cry Script Extender)

An SKSE-style loader for Far Cry 2 - lets third-party mods change engine behavior that normally only
lives in `Dunia.dll` itself, without needing to ship (or fight over) a shared patched copy of that
file. Mods come in two forms: **Lua scripts**, which need nothing but a text editor, and **plugin
DLLs**, for anything that needs real native code.

## Installing

1. Copy `FCSE.exe` into the game's `bin\` folder, next to the existing `FarCry2.exe` - that file
   is left completely untouched; `FCSE.exe` is an additional way to launch the game, not a
   replacement.
2. Drop mods into `bin\plugins\` - Lua scripts and plugin `.dll` files both go there (the folder is
   created automatically the first time you launch `FCSE.exe` if it doesn't exist yet).
3. Launch `FCSE.exe` instead of `FarCry2.exe`.

Check `bin\fcse.log` afterward to confirm what happened - it records every script and plugin
found/loaded, what each one registered/hooked/patched, and any conflicts between them.

## Writing a mod

**In Lua** - the quickest route, and enough for most mods. A script is one file:

```lua
local fcse = require 'fcse'

fcse.log('hello from Lua')

fcse.setting{ name = 'Enabled', default = true,
              on_changed = function(value) enabled = value end }

fcse.on('update', function()
  -- runs every frame
end)
```

Save it as `bin\plugins\my_mod.lua` and launch. A mod that needs more than one file can instead be a
folder with a `main.lua` inside it (`bin\plugins\my_mod\main.lua`) - the files beside that `main.lua`
are then libraries to `require`, not scripts that run on their own. Both forms are found at any depth
under `bin\plugins\`, so mods can be grouped into folders freely. Scripts can read
and write engine memory, scan for byte signatures, detour functions and add their own rows to the
in-game Mod Configuration Menu - no compiler, no build step. The separate `fcse-example-script`
download is a working, commented starting point.

Note the dialect is **Lua 5.1** (LuaJIT), not 5.4 - the same flavor used by most other game modding
frameworks. This is unrelated to the ancient Lua that Far Cry 2 itself contains internally.

**In C++** - for anything Lua cannot reach. See the separate `fcse-example-plugin` download for a
minimal, complete, working plugin (source + compiled DLL) to start from.

Full writeup for both at https://jbebe.github.io/farcry-sdk/fcse.

## Safety

A Lua script and a plugin DLL have the same power over the game and over your machine - a `.lua`
file is code, not configuration. Install mods only from sources you trust.
