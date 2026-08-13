-- Loads the real src/lua/runtime/fcse.lua against a stubbed native table, so the runtime's own
-- logic is exercised without a game attached. Mirrors what lua_host.cpp does at startup.
local ffi = require('ffi')

local ok_count, fail_count = 0, 0
local function check(name, f)
  local good, err = pcall(f)
  if good then
    ok_count = ok_count + 1
    print(string.format("  %-52s OK", name))
  else
    fail_count = fail_count + 1
    print(string.format("  %-52s FAIL: %s", name, tostring(err)))
  end
end

-- A stand-in for the C shim. Records calls so we can assert what the Lua side forwarded.
local calls = {}
local scratch = ffi.new("uint8_t[256]")
local scratch_addr = tonumber(ffi.cast("uintptr_t", scratch))

_FCSE_NATIVE = {
  log = function(msg) calls[#calls + 1] = { 'log', msg } end,
  dunia_base = function() return 0x10000000 end,
  dunia_size = function() return 20183176 end,
  current_script = function() return 'test_script' end,
  -- Stands in for the address library: 0x005FA9C0 is mapped from either build (to different
  -- live addresses, as the real table would give), everything else is unmapped.
  resolve = function(build, rva)
    calls[#calls + 1] = { 'resolve', build, rva }
    if rva == 0x005FA9C0 then return build == 'uplay' and 0x105FA9C0 or 0x105FA000 end
    return 0
  end,
  patch = function(address, bytes)
    calls[#calls + 1] = { 'patch', address, bytes }
    -- Emulate the real patch: write the bytes so read-back assertions are meaningful.
    ffi.copy(ffi.cast('uint8_t*', address), bytes, #bytes)
    return true
  end,
  hook = function(target, detour)
    calls[#calls + 1] = { 'hook', target, detour }
    return target -- pretend the trampoline is the target
  end,
  add_function_cb = function(fn, name)
    calls[#calls + 1] = { 'add_function_cb', fn, name }
    return true
  end,
  register_setting = function(name, default, cb)
    calls[#calls + 1] = { 'register_setting', name, default, cb }
    return true
  end,
  scan = function(pattern) calls[#calls + 1] = { 'scan', pattern } return 0 end,
  scan_all = function(pattern) calls[#calls + 1] = { 'scan_all', pattern } return {} end,
}

local path = ...
local chunk = assert(loadfile(path))
local fcse = chunk()

print("=== runtime loaded ===")

check("returns a module table", function()
  assert(type(fcse) == 'table', 'not a table')
end)

check("_FCSE_NATIVE is cleared from _G", function()
  assert(rawget(_G, '_FCSE_NATIVE') == nil, 'native table leaked into _G')
end)

check("base/size populated from the shim", function()
  assert(fcse.base == 0x10000000, 'base=' .. tostring(fcse.base))
  assert(fcse.size == 20183176, 'size=' .. tostring(fcse.size))
end)

check("rva / to_rva round-trip", function()
  assert(fcse.rva(0x1000) == 0x10001000)
  assert(fcse.to_rva(0x10001000) == 0x1000)
end)

check("uplay / retail forward the source build to resolve", function()
  calls = {}
  assert(fcse.uplay(0x005FA9C0) == 0x105FA9C0)
  assert(calls[1][2] == 'uplay' and calls[1][3] == 0x005FA9C0, 'wrong forward')
  assert(fcse.retail(0x005FA9C0) == 0x105FA000, 'retail resolved to the uplay address')
  assert(calls[2][2] == 'retail', 'wrong build tag')
end)

check("an unmapped address is nil, not 0", function()
  -- A feature checks this once and disables itself; 0 would become a jump to the DOS header.
  assert(fcse.uplay(0x00BADBAD) == nil, 'unmapped address did not come back nil')
end)

check("log concatenates like print", function()
  calls = {}
  fcse.log('a', 1, true)
  assert(calls[1][2] == 'a 1 true', 'got: ' .. tostring(calls[1][2]))
end)

check("every read_*/write_* pair exists", function()
  for _, s in ipairs({ 'u8','i8','u16','i16','u32','i32','u64','i64','f32','f64' }) do
    assert(type(fcse.mem['read_' .. s]) == 'function', 'missing read_' .. s)
    assert(type(fcse.mem['write_' .. s]) == 'function', 'missing write_' .. s)
  end
end)

check("read_u32 reads real memory", function()
  ffi.cast('uint32_t*', scratch_addr)[0] = 0xDEADBEEF
  assert(fcse.mem.read_u32(scratch_addr) == 0xDEADBEEF)
end)

check("write_u32 forwards correct byte width to patch", function()
  calls = {}
  fcse.mem.write_u32(scratch_addr, 0x11223344)
  assert(calls[1][1] == 'patch', 'did not call patch')
  assert(#calls[1][3] == 4, 'expected 4 bytes, got ' .. #calls[1][3])
  assert(fcse.mem.read_u32(scratch_addr) == 0x11223344, 'value not written')
end)

check("write_f32 forwards 4 bytes, write_f64 forwards 8", function()
  calls = {}
  fcse.mem.write_f32(scratch_addr, 1.5)
  assert(#calls[1][3] == 4, 'f32 wrote ' .. #calls[1][3])
  assert(fcse.mem.read_f32(scratch_addr) == 1.5)
  calls = {}
  fcse.mem.write_f64(scratch_addr, 2.5)
  assert(#calls[1][3] == 8, 'f64 wrote ' .. #calls[1][3])
  assert(fcse.mem.read_f64(scratch_addr) == 2.5)
end)

check("write_u8 forwards exactly 1 byte", function()
  calls = {}
  fcse.mem.write_u8(scratch_addr, 0x7F)
  assert(#calls[1][3] == 1, 'u8 wrote ' .. #calls[1][3] .. ' bytes')
end)

check("read_bytes / write_bytes round-trip", function()
  fcse.mem.write_bytes(scratch_addr, '\1\2\3\4')
  assert(fcse.mem.read_bytes(scratch_addr, 4) == '\1\2\3\4')
end)

check("read_ptr returns a number, not cdata", function()
  ffi.cast('uint32_t*', scratch_addr)[0] = 0x12345678
  local v = fcse.mem.read_ptr(scratch_addr)
  assert(type(v) == 'number', 'got ' .. type(v))
  assert(v == 0x12345678, string.format('got 0x%X', v))
end)

check("scan returns nil when the shim reports 0", function()
  assert(fcse.mem.scan('90 90') == nil)
end)

check("scan_all passes the shim's match list straight through", function()
  local hits = fcse.mem.scan_all('90 ?? 90')
  assert(type(hits) == 'table', 'got ' .. type(hits))
  assert(#hits == 0, 'the shim reports no matches')
end)

check("cast rejects a null address", function()
  local ok = pcall(fcse.cast, 'int*', 0)
  assert(not ok, 'null cast should error')
end)

check("fn rejects a null address", function()
  local ok = pcall(fcse.fn, 'void(*)(void)', 0)
  assert(not ok, 'null fn should error')
end)

check("hook forwards a numeric detour address", function()
  calls = {}
  local original = fcse.hook(0x10001000, 'int(__cdecl*)(void*, void*)', function() return 0 end)
  assert(calls[1][1] == 'hook')
  assert(type(calls[1][3]) == 'number', 'detour was ' .. type(calls[1][3]))
  assert(calls[1][3] ~= 0, 'detour address is 0')
  assert(original ~= nil, 'no trampoline returned')
end)

check("hook rejects a non-function handler", function()
  assert(not pcall(fcse.hook, 0x10001000, 'int(__cdecl*)(void*, void*)', 42))
end)

check("command forwards name and a numeric address", function()
  calls = {}
  fcse.command('toRed', function() return 0 end)
  assert(calls[1][1] == 'add_function_cb')
  assert(type(calls[1][2]) == 'number', 'address was ' .. type(calls[1][2]))
  assert(calls[1][3] == 'toRed')
end)

check("setting validates its spec", function()
  assert(not pcall(fcse.setting, 'not a table'))
  assert(not pcall(fcse.setting, {}), 'missing name accepted')
  assert(not pcall(fcse.setting, { name = 'x' }), 'missing default accepted')
  assert(not pcall(fcse.setting, { name = 'x', default = 'nope' }), 'non-boolean default accepted')
  assert(not pcall(fcse.setting, { name = 'x', default = true, on_changed = 5 }))
  assert(pcall(fcse.setting, { name = 'x', default = true }), 'valid spec rejected')
end)

check("on() registers under the right event", function()
  local f = function() end
  fcse.on('update', f)
  local list = fcse._handlers.update
  assert(#list >= 1)
  assert(list[#list].fn == f)
  assert(list[#list].script == 'test_script', 'script was ' .. tostring(list[#list].script))
  assert(list[#list].failures == 0, 'failures should start at 0')
end)

-- The host caches _handlers in the Lua registry once, at load, and dispatches through that
-- reference every frame. on() must keep mutating the same table rather than replacing it.
check("on() mutates _handlers in place rather than replacing it", function()
  local before = fcse._handlers
  local lists = { before.load, before.register_functions, before.update }
  fcse.on('update', function() end)
  assert(rawequal(fcse._handlers, before), '_handlers table identity changed')
  assert(rawequal(fcse._handlers.load, lists[1]), 'load list was replaced')
  assert(rawequal(fcse._handlers.register_functions, lists[2]), 'register list was replaced')
  assert(rawequal(fcse._handlers.update, lists[3]), 'update list was replaced')
end)

check("on() rejects an unknown event", function()
  local ok, err = pcall(fcse.on, 'onFrame', function() end)
  assert(not ok)
  assert(tostring(err):find('unknown event'), 'unhelpful message: ' .. tostring(err))
  -- the message should list the real event names, since that is what the author needs
  assert(tostring(err):find('update'), 'message does not list valid events')
end)

check("on() rejects a non-function handler", function()
  assert(not pcall(fcse.on, 'update', 'nope'))
end)

check("hex dumps 16 bytes per line with an ASCII column", function()
  calls = {}
  for i = 0, 31 do ffi.cast('uint8_t*', scratch_addr)[i] = 0x41 + (i % 26) end
  fcse.hex(scratch_addr, 32)
  assert(#calls == 2, 'expected 2 lines, got ' .. #calls)
  assert(calls[1][2]:find('ABCDEFGHIJKLMNOP'), 'no ascii column: ' .. calls[1][2])
end)

print(string.format("\n%d passed, %d failed", ok_count, fail_count))
os.exit(fail_count == 0 and 0 or 1)
