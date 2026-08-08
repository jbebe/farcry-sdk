-- example_script - a working starting point for an FCSE Lua script, and a smoke test for the host.
--
-- Install by copying this file into the game's bin\plugins\, next to the plugin DLLs - one place
-- for mods, whatever they are written in.
--
-- A single .lua file is a mod on its own, which is all this needs. A mod that wants more files can
-- instead be a folder with a main.lua in it (bin\plugins\my_mod\main.lua); everything beside that
-- main.lua is then a library to require, not a script that runs on its own. Both forms are found at
-- any depth under bin\plugins\.
--
-- Everything here is read-only or self-contained. Nothing patches engine memory, so it is safe to
-- leave installed while you work on something else.

local fcse = require 'fcse'

fcse.log('hello from Lua - Dunia is at 0x' .. string.format('%08X', fcse.base)
         .. ', ' .. fcse.size .. ' bytes')

--------------------------------------------------------------------------------
-- Settings
--------------------------------------------------------------------------------

-- One checkbox. It shows up as a row in the in-game Mod Configuration Menu and as a line under
-- [example_script] in bin\fcse.ini. The callback fires immediately with whatever the file holds, so
-- `verbose` is correct before the game has even started.
local verbose = false

fcse.setting{
  name = 'Verbose logging',
  default = false,
  on_changed = function(value)
    verbose = value
    fcse.log('verbose logging is now ' .. tostring(value))
  end,
}

--------------------------------------------------------------------------------
-- Per-frame updates
--------------------------------------------------------------------------------

-- Handlers receive the frame delta in seconds, straight from the engine's own timing block - the
-- same value CXGame::Update hands to CGame::Update. Accumulating it is how anything time-based
-- should be written, rather than counting frames and assuming a rate.
local elapsed = 0
local frames = 0

fcse.on('update', function(dt)
  frames = frames + 1
  elapsed = elapsed + dt

  if elapsed >= 5.0 then
    fcse.log(string.format('%d frames in %.1fs (%.1f fps)', frames, elapsed, frames / elapsed))
    frames = 0
    elapsed = 0
  end
end)

--------------------------------------------------------------------------------
-- Reading engine memory
--------------------------------------------------------------------------------

fcse.on('load', function()
  -- Every address in this project's notes is an RVA against Dunia's 0x10000000 preferred base, so
  -- it has to be rebased onto wherever the module actually loaded.
  local entry = fcse.rva(0x1000)

  if verbose then
    fcse.log('first bytes of Dunia at RVA 0x1000:')
    fcse.hex(entry, 32)
  end

  -- A signature is worth more than a hardcoded RVA for anything meant to outlive a game patch: the
  -- RVA is only valid for the exact build it was read from. `??` matches any byte.
  local hits = fcse.mem.scan_all('55 8B EC 83 EC ?? 53 56 57')
  fcse.log(('common function prologue: %d match(es) in .text'):format(#hits))
end)

--------------------------------------------------------------------------------
-- What else is available
--------------------------------------------------------------------------------
--
-- Claim one of Dunia's named callbacks - no address needed at all. Only valid from
-- 'register_functions', and first-claimant-wins, so a compiled plugin that wants the same name and
-- loaded earlier keeps it (example_plugin.dll claims "toRed", which is why this is left commented):
--
--   fcse.on('register_functions', function()
--     fcse.command('toRed', function(param1, param2)
--       fcse.cast('int*', param1)[0] = 0   -- 0 = red-channel-only 2D/HUD rendering
--       return 0
--     end)
--   end)
--
-- Call an engine method. The signature must name the calling convention; engine methods are
-- __thiscall, and getting that wrong corrupts the stack rather than failing cleanly:
--
--   local AddDiamonds = fcse.fn('void(__thiscall*)(void*, int)', fcse.rva(0x59F210))
--   AddDiamonds(player, 100)
--
-- Detour a function. `original` is the trampoline back into the real one:
--
--   local original
--   original = fcse.hook(addr, 'int(__cdecl*)(void*, void*)', function(a, b)
--     fcse.log('called')
--     return original(a, b)
--   end)
--
-- Write memory. Routed through FCSE's patch manager, so it handles VirtualProtect, logs the change
-- against this script, and is rejected if another script already claimed those bytes:
--
--   fcse.mem.write_f32(fcse.rva(0x1234567), 1.0)
