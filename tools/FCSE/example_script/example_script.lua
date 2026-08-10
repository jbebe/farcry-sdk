-- example_script - the same mod as example_plugin/example_plugin.cpp, written in Lua: two
-- toggleable rendering effects, both reachable from the in-game Mod Configuration Menu, both
-- surviving a restart.
--
--   Shake the UI    every menu, the HUD and the map jitter a few pixels each frame
--   Red UI          the entire 2D layer renders red-channel-only
--
-- The two files are meant to be read side by side. They use the same seams in the same order, so
-- the comparison answers "what does the script API give up?" directly. For this mod: nothing - and
-- a script needs no compiler, no CMake and no rebuild to change.
--
-- Install by copying this file into the game's bin\plugins\, next to the plugin DLLs - one place
-- for mods, whatever they are written in.
--
-- A single .lua file is a mod on its own, which is all this needs. A mod that wants more files can
-- instead be a folder with a main.lua in it (bin\plugins\my_mod\main.lua); everything beside that
-- main.lua is then a library to require, not a script that runs on its own. Both forms are found at
-- any depth under bin\plugins\.

local fcse = require 'fcse'
local ffi = fcse.ffi

fcse.log('example_script loaded - Dunia is at 0x' .. string.format('%08X', fcse.base))

--------------------------------------------------------------------------------
-- Effect 1 - shake the UI (a hook on an engine method)
--------------------------------------------------------------------------------

-- magma::CRenderNomadImpl is the vertex sink for Far Cry 2's entire 2D layer: HUD, map, weapon
-- wheel, and every menu. Each widget quad is emitted as BeginQuad -> SetVertex x4 -> EndQuad, and
-- EndQuad finishes each of the four corners with
--
--     x' = (x + m_originX) / m_width * m_scaleX + m_biasX
--     y' = (y + m_originY) / m_height
--
-- so m_originX/m_originY are a pixel-space translation added to every vertex the UI draws.
-- BeginPageRendering() refills both from the current viewport at the start of every page, which is
-- what makes them a good thing for a mod to touch: adding a random offset just after it runs shakes
-- the whole interface, and switching the effect off restores the game exactly, because the engine
-- overwrites both fields again on the very next frame. Nothing is saved, nothing is unpatched, and
-- a crash mid-shake leaves no trace on disk.
local ORIGIN_X = 0xE0 -- float, magma::CRenderNomadImpl
local ORIGIN_Y = 0xE4 -- float, the next one along

-- Screen pixels - m_originX/Y are in viewport pixels, so this is literal distance.
local SHAKE_AMPLITUDE = 6.0

local shake_enabled = false

-- A private generator rather than math.random(): a script sharing the global generator with
-- everything else in the process, and stepping it on every frame, is a side effect nobody asked
-- for. This is the same xorshift the C plugin uses, so both mods shake identically.
local rng = 0x9E3779B9

local function next_jitter()
  rng = bit.bxor(rng, bit.lshift(rng, 13))
  rng = bit.bxor(rng, bit.rshift(rng, 17))
  rng = bit.bxor(rng, bit.lshift(rng, 5))
  -- Top 24 bits as [0,1), then rescaled to [-SHAKE_AMPLITUDE, +SHAKE_AMPLITUDE].
  local unit = bit.rshift(rng, 8) / 16777216
  return (unit * 2.0 - 1.0) * SHAKE_AMPLITUDE
end

fcse.on('load', function()
  -- The address as it appears in Steam's Dunia.dll. Naming it from GOG's instead -
  -- fcse.retail(0x005ED140) - is the same function and resolves just as correctly on either build;
  -- only one of the two is ever needed. nil means this build has no counterpart, so the feature
  -- turns itself off rather than hooking a wild pointer.
  local BeginPageRendering = fcse.uplay(0x005FA9C0)

  if not BeginPageRendering then
    fcse.log('BeginPageRendering is not mapped on this build - UI shake disabled')
    return
  end

  -- The method is __thiscall (`this` in ECX, no stack arguments), and the signature has to say so:
  -- LuaJIT never infers a calling convention for a callback, and getting it wrong corrupts the
  -- stack rather than failing cleanly.
  local original
  original = fcse.hook(BeginPageRendering, 'void(__thiscall*)(void*)', function(self)
    -- Let the engine set the real viewport origin first, then perturb it. Doing it in this order
    -- is what makes the effect self-restoring - the value we modify is written fresh every frame,
    -- so we are never responsible for putting anything back.
    original(self)

    if not shake_enabled then
      return
    end

    -- A direct FFI store, deliberately *not* fcse.mem.write_f32(): that routes through FCSE's
    -- patch manager, which is for editing code and static data once and logs and overlap-checks
    -- every call. This is a per-frame write into a live engine object - ordinary memory this
    -- script is already allowed to touch.
    local base = ffi.cast('char*', self)
    local origin_x = ffi.cast('float*', base + ORIGIN_X)
    local origin_y = ffi.cast('float*', base + ORIGIN_Y)
    origin_x[0] = origin_x[0] + next_jitter()
    origin_y[0] = origin_y[0] + next_jitter()
  end)

  if original then
    fcse.log(('hooked magma::CRenderNomadImpl::BeginPageRendering at 0x%08X')
             :format(BeginPageRendering))
  end
  -- fcse.hook returning nil is already logged by FCSE, naming whoever won the address. `original`
  -- stays nil in that case, which is exactly why the handler above is never reached: it is only
  -- ever called through the hook that failed to install.
end)

--------------------------------------------------------------------------------
-- Effect 2 - red UI (claiming one of Dunia's named callbacks)
--------------------------------------------------------------------------------

-- The cheapest real hook there is: no address at all, just a name claimed from
-- 'register_functions'. FarCry2.exe ships a "toRed" handler that writes 1; the engine calls it once
-- from magma::CRenderNomadImpl::BeginRendering and keeps the answer in the renderer's "full colour"
-- field. Writing 0 instead makes the whole 2D layer render red-channel-only.
--
-- Because the name is a string key rather than an address, this half of the mod needs no address
-- library, no signature, and no per-build knowledge whatsoever.
--
-- First claimant wins across the whole process, so if example_plugin.dll is also installed it
-- loaded earlier and keeps the name - FCSE logs the rejection naming both.
local red_enabled = false

fcse.on('register_functions', function()
  fcse.command('toRed', function(param1)
    fcse.cast('int*', param1)[0] = red_enabled and 0 or 1
    return 0
  end)
end)

--------------------------------------------------------------------------------
-- Settings
--------------------------------------------------------------------------------

-- One row each in the in-game Mod Configuration Menu, and one line each under [example_script] in
-- bin\fcse.ini. Both callbacks fire immediately with whatever the file holds, so the flags above
-- are already correct before the game has started - a script never reads its own config.

fcse.setting{
  name = 'Shake the UI',
  default = false,
  on_changed = function(value)
    shake_enabled = value
    fcse.log('UI shake is ' .. (value and 'ON' or 'OFF'))
  end,
}

fcse.setting{
  name = 'Red UI',
  default = false,
  on_changed = function(value)
    red_enabled = value
    fcse.log('red UI is ' .. (value and 'ON' or 'OFF'))
  end,
}

--------------------------------------------------------------------------------
-- What else is available
--------------------------------------------------------------------------------
--
-- Per-frame work, with the frame delta in seconds straight from the engine's own timing block -
-- the same value CXGame::Update hands to CGame::Update:
--
--   fcse.on('update', function(dt) ... end)
--
-- Call an engine method. The signature must name the calling convention, same as a hook:
--
--   local AddDiamonds = fcse.fn('void(__thiscall*)(void*, int)', fcse.uplay(0x59F210))
--   AddDiamonds(player, 100)
--
-- Find code by signature instead of by address, for builds the address library has never seen.
-- `??` matches any byte; scan_all is worth checking first, since a pattern with several hits is
-- not the anchor it looks like:
--
--   local hit = fcse.mem.scan('8B 44 24 ?? 85 C0 74 ??')
--
-- Patch code or static data. Routed through FCSE's patch manager, so it handles VirtualProtect,
-- logs the change against this script, and is rejected if another script already claimed those
-- bytes:
--
--   fcse.mem.write_f32(fcse.uplay(0x1234567), 1.0)
--
-- Read memory and dump it, the fastest way to confirm a struct offset:
--
--   fcse.hex(fcse.uplay(0x005FA9C0), 64)
