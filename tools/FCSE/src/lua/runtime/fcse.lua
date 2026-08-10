-- fcse.lua - the API every FCSE script gets, preloaded as `require 'fcse'`.
--
-- Embedded into FCSE.exe as an RCDATA resource, not shipped as a loose file: there is nothing for a
-- player to delete or for an installer to leave stale against the exe that reads it. Same reasoning
-- as the .mgb layouts.
--
-- Written in Lua rather than C++ on purpose. LuaJIT's FFI already does the hard part - laying types
-- over memory and calling native code - so the C side only has to expose what FFI genuinely cannot
-- reach: FCSE's hook/patch/settings registries. Everything below is built from those few primitives,
-- which keeps the native surface small enough to audit and puts the ergonomics somewhere they can be
-- changed without a rebuild.
--
-- Every address crosses the Lua/C boundary as a plain number, never as cdata. LuaJIT's cdata is its
-- own value type that the standard Lua C API cannot inspect, so a shim taking cdata would have to
-- reach into LuaJIT internals. Far Cry 2 is a 32-bit process and a double represents every 32-bit
-- address exactly, so numbers cost nothing here.

local ffi = require('ffi')
local C = _FCSE_NATIVE -- installed by lua_api.cpp; cleared from _G once this module holds it
_FCSE_NATIVE = nil

local fcse = {}

--------------------------------------------------------------------------------
-- Logging
--------------------------------------------------------------------------------

-- Takes print()'s argument list rather than one string: a script author reaching for a log call is
-- usually mid-debug and wants several values at once.
function fcse.log(...)
  local parts = {}
  for i = 1, select('#', ...) do
    parts[i] = tostring((select(i, ...)))
  end
  C.log(table.concat(parts, ' '))
end

--------------------------------------------------------------------------------
-- Addresses
--------------------------------------------------------------------------------

-- Dunia.dll's load base and on-disk size. The base is not fixed, so a confirmed RVA has to be added
-- to it at runtime rather than written down as an absolute address.
fcse.base = C.dunia_base()
fcse.size = C.dunia_size()

-- RVA -> live address. The most-used call in any script: every address in this project's notes is an
-- RVA against Dunia's 0x10000000 preferred base.
function fcse.rva(offset)
  return fcse.base + offset
end

-- Live address -> RVA, for reporting an address back in the form the notes use.
function fcse.to_rva(address)
  return address - fcse.base
end

-- An address as you found it in one specific build, resolved to wherever that same code lives in
-- the build the player is actually running:
--
--   local BeginPageRendering = fcse.uplay(0x005FA9C0)    -- read off Steam's Dunia.dll
--   local BeginPageRendering = fcse.retail(0x005ED140)   -- ...or GOG's. The same function.
--
-- Far Cry 2 v1.03 shipped as two PC builds that place the same code at different addresses, and
-- they are not a fixed distance apart - the offset takes 2,608 different values across the mapping
-- - so this lookup is the only correct way to translate. Which build an address came from is the
-- only thing you have to know about it, and you always do: it is the copy of the game you opened.
--
-- Prefer these over rva(). `fcse.rva(0x005FA9C0)` is duniaBase plus the number, which is the right
-- answer on exactly one of the two builds and unrelated code on the other.
--
-- Returns nil when the mapping has no counterpart for that address, so a script can disable the
-- feature rather than hook a wild pointer. A byte signature (mem.scan) is the fallback for builds
-- the mapping has never seen.
local function resolver(build)
  return function(rva)
    local address = C.resolve(build, rva)
    if address == 0 then
      return nil
    end
    return address
  end
end

fcse.uplay = resolver('uplay')
fcse.retail = resolver('retail')

--------------------------------------------------------------------------------
-- Types and calls
--------------------------------------------------------------------------------

fcse.ffi = ffi
fcse.cdef = ffi.cdef
fcse.sizeof = ffi.sizeof
fcse.offsetof = ffi.offsetof
fcse.new = ffi.new
fcse.string = ffi.string

-- Lay a C type over an address.
function fcse.cast(ctype, address)
  if address == nil or address == 0 then
    error('fcse.cast: null address for ' .. tostring(ctype), 2)
  end
  return ffi.cast(ctype, address)
end

-- Turn an address into a callable of the given signature.
--
--   local AddDiamonds = fcse.fn('void(__thiscall*)(void*, int)', fcse.rva(0x59F210))
--
-- The signature must name the calling convention for anything that is not __cdecl. Engine methods
-- are __thiscall (`this` in ecx), so most calls into Dunia need it spelled out; getting it wrong
-- corrupts the stack instead of failing cleanly.
function fcse.fn(signature, address)
  if address == nil or address == 0 then
    error('fcse.fn: null address for ' .. tostring(signature), 2)
  end
  return ffi.cast(signature, address)
end

--------------------------------------------------------------------------------
-- Memory
--------------------------------------------------------------------------------

local mem = {}
fcse.mem = mem

local scalar_types = {
  u8 = 'uint8_t', i8 = 'int8_t',
  u16 = 'uint16_t', i16 = 'int16_t',
  u32 = 'uint32_t', i32 = 'int32_t',
  u64 = 'uint64_t', i64 = 'int64_t',
  f32 = 'float', f64 = 'double',
}

for suffix, ctype in pairs(scalar_types) do
  local pointer_type = ctype .. '*'
  local array_type = ctype .. '[1]'
  local width = ffi.sizeof(ctype)

  -- Reads go straight through FFI. Reading needs no protection change, and routing it through a
  -- native call would cost a boundary crossing per access in code that may run every frame.
  mem['read_' .. suffix] = function(address)
    return ffi.cast(pointer_type, address)[0]
  end

  -- Writes go through FCSE's PatchManager rather than a direct FFI store, which buys three things a
  -- raw store does not: the VirtualProtect dance (engine .text and .rdata are not writable), a log
  -- line naming the script, and rejection when a *different* script already claimed those bytes.
  mem['write_' .. suffix] = function(address, value)
    local box = ffi.new(array_type, value)
    return C.patch(address, ffi.string(box, width))
  end
end

-- Pointer-sized read, returned as a number so it composes with rva()/to_rva() and the read_* family
-- rather than forcing the caller to unwrap cdata.
function mem.read_ptr(address)
  return tonumber(ffi.cast('uintptr_t', ffi.cast('void**', address)[0]))
end

-- Reads a NUL-terminated string. `limit` caps the scan so a bad address yields a short wrong answer
-- instead of walking into unmapped memory.
function mem.read_string(address, limit)
  return ffi.string(ffi.cast('const char*', address), limit)
end

-- Reads `count` bytes as a Lua string, for hexdumps and capturing a signature.
function mem.read_bytes(address, count)
  return ffi.string(ffi.cast('const char*', address), count)
end

-- Writes raw bytes from a Lua string.
function mem.write_bytes(address, bytes)
  return C.patch(address, bytes)
end

--------------------------------------------------------------------------------
-- Pattern scanning
--------------------------------------------------------------------------------

-- Finds a byte signature in Dunia's executable section.
--
--   local hit = fcse.mem.scan('8B 44 24 ?? 85 C0 74 ??')
--
-- `??` matches any byte. Returns the address of the first match, or nil. The scan is native: 14 MB
-- of .text per call is not something to walk in interpreted Lua.
--
-- Prefer a signature over a hardcoded RVA for anything meant to survive a game patch - an RVA is
-- only valid for the exact build it was read from.
function mem.scan(pattern)
  local address = C.scan(pattern)
  if address == 0 then
    return nil
  end
  return address
end

-- Every match rather than the first. Worth checking before committing to a signature: a pattern with
-- several hits is not the anchor it looks like.
function mem.scan_all(pattern)
  return C.scan_all(pattern)
end

--------------------------------------------------------------------------------
-- Hexdump
--------------------------------------------------------------------------------

-- Dumps `count` bytes at `address` to fcse.log, 16 per line with an ASCII column. The fastest way to
-- confirm a struct offset is to look at the bytes.
function fcse.hex(address, count)
  count = count or 64
  local bytes = mem.read_bytes(address, count)
  for offset = 0, count - 1, 16 do
    local hex, ascii = {}, {}
    for i = 1, 16 do
      local byte = bytes:byte(offset + i)
      if byte then
        hex[i] = string.format('%02X', byte)
        ascii[i] = (byte >= 0x20 and byte < 0x7F) and string.char(byte) or '.'
      else
        hex[i] = '  '
        ascii[i] = ' '
      end
    end
    fcse.log(string.format('%08X  %s  %s', address + offset,
                           table.concat(hex, ' '), table.concat(ascii)))
  end
end

--------------------------------------------------------------------------------
-- Hooks
--------------------------------------------------------------------------------

-- Holds every callback for the life of the process. An ffi callback is collectable like anything
-- else, and the only other reference is the raw pointer inside MinHook's trampoline - which the GC
-- cannot see. Collecting one turns the installed detour into a jump into freed memory the next time
-- the game calls it. This table is load-bearing; it is not a leak to tidy up.
local live_callbacks = {}

local function retain(callback)
  live_callbacks[#live_callbacks + 1] = callback
  return callback
end

local function address_of(callback)
  return tonumber(ffi.cast('uintptr_t', callback))
end

-- Detours `address` to `handler`, returning a callable for the original.
--
--   local original
--   original = fcse.hook(addr, 'int(__cdecl*)(void*, void*)', function(a, b)
--     return original(a, b)
--   end)
--
-- The signature describes the function being hooked and must name its calling convention. LuaJIT
-- infers __stdcall when *calling* a function but never for a callback, so an unannotated __stdcall
-- target corrupts the stack on return. This is the one place a script cannot be vague.
--
-- Returns nil (logged) if another script already owns a hook on that address: FCSE installs one
-- detour per address and the first claimant keeps it.
function fcse.hook(address, signature, handler)
  if address == nil or address == 0 then
    error('fcse.hook: null address', 2)
  end
  if type(handler) ~= 'function' then
    error('fcse.hook: handler must be a function, got ' .. type(handler), 2)
  end

  local callback = ffi.cast(signature, handler)
  local trampoline = C.hook(address, address_of(callback))
  if trampoline == nil then
    callback:free()
    return nil
  end

  retain(callback)
  return ffi.cast(signature, trampoline)
end

--------------------------------------------------------------------------------
-- Function registry
--------------------------------------------------------------------------------

-- Claims one of Dunia's named function-registry callbacks - the mechanism behind toRed, AddDiamond,
-- MalariaCurve and the rest. Needs no address at all, which makes it the cheapest real hook a script
-- can install.
--
-- Only valid from an `on('register_functions')` handler: the registry does not exist before Dunia
-- builds it and is closed to new names afterwards. First claimant wins across the whole process, so
-- a compiled plugin that wanted the same name and loaded earlier keeps it.
--
-- Handlers always receive exactly two raw pointer arguments, whatever the specific name reads.
function fcse.command(name, handler)
  if type(name) ~= 'string' or name == '' then
    error('fcse.command: `name` is required', 2)
  end
  if type(handler) ~= 'function' then
    error('fcse.command: handler must be a function, got ' .. type(handler), 2)
  end
  local callback = retain(ffi.cast('int(__cdecl*)(void*, void*)', handler))
  return C.add_function_cb(address_of(callback), name)
end

--------------------------------------------------------------------------------
-- Settings
--------------------------------------------------------------------------------

-- Registers a persistent, player-editable setting: one line in bin\fcse.ini under a group named
-- after the script, and one row in the in-game Mod Configuration Menu.
--
--   fcse.setting{ name = 'Verbose logging', default = false,
--                 on_changed = function(value) verbose = value end }
--
-- on_changed fires immediately with whatever the file holds - so a script never reads its own config
-- - and again after every in-game toggle.
--
-- Booleans only, that being the one type FCSE_SettingType currently defines.
function fcse.setting(spec)
  if type(spec) ~= 'table' then
    error('fcse.setting: expected a table, got ' .. type(spec), 2)
  end
  if type(spec.name) ~= 'string' or spec.name == '' then
    error('fcse.setting: `name` is required', 2)
  end
  if type(spec.default) ~= 'boolean' then
    error('fcse.setting: `default` must be true or false', 2)
  end
  if spec.on_changed ~= nil and type(spec.on_changed) ~= 'function' then
    error('fcse.setting: `on_changed` must be a function', 2)
  end
  return C.register_setting(spec.name, spec.default, spec.on_changed)
end

--------------------------------------------------------------------------------
-- Events
--------------------------------------------------------------------------------

--   'load'               - every script loaded, before any engine code runs. Hooks and patches here.
--   'register_functions' - Dunia's registry exists. The only valid time to call fcse.command.
--   'update'             - once per frame, from the engine's own frame loop (CXGame::Update).
--                          The handler is passed the frame delta in seconds; prefer accumulating it
--                          over counting frames, since the rate is not fixed.
--
-- Each entry records the script that registered it so the host can name it in a traceback and stop
-- calling that one script if it keeps failing.
local handlers = { load = {}, register_functions = {}, update = {} }
fcse._handlers = handlers

function fcse.on(event, handler)
  local list = handlers[event]
  if list == nil then
    local names = {}
    for name in pairs(handlers) do names[#names + 1] = name end
    table.sort(names)
    error("fcse.on: unknown event '" .. tostring(event) .. "', expected one of: "
          .. table.concat(names, ', '), 2)
  end
  if type(handler) ~= 'function' then
    error('fcse.on: handler must be a function, got ' .. type(handler), 2)
  end
  list[#list + 1] = { script = C.current_script(), fn = handler, failures = 0 }
  return handler
end

return fcse
