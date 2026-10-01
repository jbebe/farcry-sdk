-- Flashlight - a spot light at the player's head, switched by the Flashlight control
-- (toggle_flashlight: L by default, rebindable in Options > Controls).
--
-- The light is one of the engine's own scene lights, made the way the player's vehicle makes its
-- headlight (CVehicleTypeWheeled::EnableFrontHeadlights) and placed every frame from the render
-- camera. The control, its label and the HUD icon are data in this layer; only what data cannot
-- reach is here.

local fcse = require 'fcse'
local ffi = fcse.ffi

ffi.cdef[[
typedef struct { int32_t index; int32_t generation; } flashlight_handle;
typedef struct { void* resource; bool requested; } flashlight_sound_ref;
]]

-- CRC32 of the signal the control sends, as the input system hashes it.
local SIGNAL_TOGGLE = 0x78D9863A

-- The switch, a bank of this layer's own, played through the player's own foley.
local CLICK = 0x00FC0A00
local SOUND_FOLEY_PLAYER = 15

local RANGE = 25.0
local OUTER_ANGLE = math.rad(45)
local INNER_ANGLE = math.rad(20)
-- The player headlight's warm white.
local COLOUR = { 1.0, 0.957, 0.820 }
local INTENSITY = 3.0
local SHADOW_FACTOR = 1.0

-- Where the lamp sits relative to the eye, in metres: a little above and to the right, so what it
-- lights shows some shape rather than lying flat under a light at the viewpoint.
local ABOVE = 0.03
local RIGHT = 0.12

-- CSceneLight.
local LIGHT_TYPE, LIGHT_ENABLED = 0x04, 0x08
local LIGHT_POSITION, LIGHT_DIRECTION, LIGHT_UP = 0x0C, 0x18, 0x24
local LIGHT_RANGE, LIGHT_COLOUR, LIGHT_INTENSITY = 0x30, 0x34, 0x40
local LIGHT_CAST_SHADOW, LIGHT_SHADOW_FACTOR = 0x48, 0x4C
local LIGHT_OUTER_ANGLE, LIGHT_INNER_ANGLE = 0x5C, 0x60
local SPOT = 3

-- The render camera's position and axes, as CSceneCamera lays them out.
local CAMERA_POSITION, CAMERA_FRONT, CAMERA_UP, CAMERA_RIGHT = 0x08, 0x48, 0x54, 0x60

-- The local player's scene, and the camera manager embedded in it.
local PLAYER_SCENE = 0x04
local SCENE_CAMERA_MANAGER = 0xB8

-- In EnableFrontHeadlights: `mov ecx, <light container>`, `call CreateObject`, `call ModifyOriginal`.
local HEADLIGHT_CONTAINER = 0x42
local HEADLIGHT_CREATE_CALL = 0x46
local HEADLIGHT_MODIFY_CALL = 0x5F

local SOUND_PLAY_SLOT = 0x9C
local SOUND_REQUEST_LOAD_SLOT = 0x08

local container, create_light, modify_light, destroy_light
local get_local_player, active_camera, render_camera
local get_sound_system, get_from_sound_id

local handle = ffi.new('flashlight_handle', { -1, -1 })
local owner = nil
local on = false
local toggles = 0
local cast_shadows = true
local click_bank = nil
local trace = false

local function call_target(call)
  return call + 5 + fcse.mem.read_i32(call + 1)
end

local function method(object, slot, signature)
  local vtable = fcse.mem.read_ptr(tonumber(ffi.cast('uintptr_t', object)))
  return ffi.cast(signature, fcse.mem.read_ptr(vtable + slot))
end

local function write_vec3(base, offset, x, y, z)
  local v = ffi.cast('float*', base + offset)
  v[0], v[1], v[2] = x, y, z
end

local function read_vec3(base, offset)
  local v = ffi.cast('float*', base + offset)
  return v[0], v[1], v[2]
end

-- A play never loads its bank, so the click's is requested once and never let go.
local function hold_click()
  if click_bank ~= nil then
    return
  end
  click_bank = ffi.new('flashlight_sound_ref')
  get_from_sound_id(click_bank, CLICK, nil)
  if click_bank.resource ~= nil then
    method(click_bank.resource, SOUND_REQUEST_LOAD_SLOT, 'void(__thiscall*)(void*)')(click_bank.resource)
  end
end

local function click()
  local system = get_sound_system()
  if system == nil then
    return
  end
  local played = method(system, SOUND_PLAY_SLOT,
                        'uint32_t(__thiscall*)(void*, uint32_t, int32_t, void*, float)')(
    system, CLICK, SOUND_FOLEY_PLAYER, nil, 0.0)
  local loaded = click_bank.resource ~= nil and
                 ffi.cast('uint16_t*', ffi.cast('char*', click_bank.resource) + 8)[0] or -1
  fcse.log(('flashlight: click %08X, bank loaded %d'):format(played, loaded))
end

local function light()
  return ffi.cast('char*', modify_light(container, handle, true))
end

-- Everything about the light but where it is, written whenever it is switched on.
local function configure()
  if handle.index == -1 then
    create_light(container, handle, false)
  end
  local l = light()
  ffi.cast('int32_t*', l + LIGHT_TYPE)[0] = SPOT
  ffi.cast('float*', l + LIGHT_RANGE)[0] = RANGE
  write_vec3(l, LIGHT_COLOUR, COLOUR[1], COLOUR[2], COLOUR[3])
  ffi.cast('float*', l + LIGHT_INTENSITY)[0] = INTENSITY
  ffi.cast('uint8_t*', l + LIGHT_CAST_SHADOW)[0] = cast_shadows and 1 or 0
  ffi.cast('float*', l + LIGHT_SHADOW_FACTOR)[0] = SHADOW_FACTOR
  ffi.cast('float*', l + LIGHT_OUTER_ANGLE)[0] = OUTER_ANGLE
  ffi.cast('float*', l + LIGHT_INNER_ANGLE)[0] = INNER_ANGLE
end

local function set_enabled(enabled)
  if handle.index ~= -1 then
    ffi.cast('uint8_t*', light() + LIGHT_ENABLED)[0] = enabled and 1 or 0
  end
end

-- The light belongs to the world it was made in; a new player means that world is gone.
local function forget()
  if handle.index ~= -1 then
    destroy_light(container, handle.index, handle.generation)
    handle.index, handle.generation = -1, -1
  end
  on = false
end

local function place(player)
  local scene = fcse.mem.read_ptr(tonumber(ffi.cast('uintptr_t', player)) + PLAYER_SCENE)
  if scene == 0 then
    return
  end
  local camera = active_camera(ffi.cast('void*', scene + SCENE_CAMERA_MANAGER))
  if camera == nil then
    return
  end
  local view = render_camera(camera)
  if view == nil then
    return
  end
  local c = ffi.cast('char*', view)
  local px, py, pz = read_vec3(c, CAMERA_POSITION)
  local fx, fy, fz = read_vec3(c, CAMERA_FRONT)
  local ux, uy, uz = read_vec3(c, CAMERA_UP)
  local rx, ry, rz = read_vec3(c, CAMERA_RIGHT)

  local l = light()
  if trace then
    trace = false
    fcse.log(('flashlight: light %d/%d at %s, camera at %.2f %.2f %.2f facing %.2f %.2f %.2f up %.2f %.2f %.2f')
             :format(handle.index, handle.generation, tostring(l), px, py, pz, fx, fy, fz, ux, uy, uz))
  end
  write_vec3(l, LIGHT_POSITION, px + ux * ABOVE + rx * RIGHT, py + uy * ABOVE + ry * RIGHT,
             pz + uz * ABOVE + rz * RIGHT)
  write_vec3(l, LIGHT_DIRECTION, fx, fy, fz)
  write_vec3(l, LIGHT_UP, ux, uy, uz)
end

local function resolve()
  local headlight = fcse.uplay(0x001AE6C0)
  local destroy = fcse.uplay(0x00459550)
  local player = fcse.uplay(0x00831870)
  local active = fcse.uplay(0x0057C1B0)
  local render = fcse.uplay(0x00504CE0)
  local sound = fcse.uplay(0x006215B0)
  local sound_id = fcse.uplay(0x006242F0)
  if not (headlight and destroy and player and active and render and sound and sound_id) then
    return false
  end
  if fcse.mem.read_u8(headlight + HEADLIGHT_CONTAINER - 1) ~= 0xB9 or
     fcse.mem.read_u8(headlight + HEADLIGHT_CREATE_CALL) ~= 0xE8 or
     fcse.mem.read_u8(headlight + HEADLIGHT_MODIFY_CALL) ~= 0xE8 then
    return false
  end

  container = ffi.cast('void*', fcse.mem.read_u32(headlight + HEADLIGHT_CONTAINER))
  create_light = fcse.fn('void*(__thiscall*)(void*, flashlight_handle*, bool)',
                         call_target(headlight + HEADLIGHT_CREATE_CALL))
  modify_light = fcse.fn('void*(__thiscall*)(void*, flashlight_handle*, bool)',
                         call_target(headlight + HEADLIGHT_MODIFY_CALL))
  destroy_light = fcse.fn('void(__thiscall*)(void*, int32_t, int32_t)', destroy)
  get_local_player = fcse.fn('void*(__cdecl*)()', player)
  active_camera = fcse.fn('void*(__thiscall*)(void*)', active)
  render_camera = fcse.fn('void*(__thiscall*)(void*)', render)
  get_sound_system = fcse.fn('void*(__cdecl*)()', sound)
  get_from_sound_id = fcse.fn('flashlight_sound_ref*(__cdecl*)(flashlight_sound_ref*, uint32_t, const char*)',
                              sound_id)
  return true
end

fcse.on('load', function()
  if not resolve() then
    fcse.log('flashlight: the light or camera calls were not found in this build - disabled')
    return
  end

  -- The game's signal dispatcher, one instruction in: the signal is the first stack argument.
  local dispatch = fcse.mem.scan('83 EC 50 A8 01 53 55 56 57 8B F9 75 ?? 83 C8 01 A3')
  if not dispatch then
    fcse.log('flashlight: the signal dispatcher was not found in this build - disabled')
    return
  end
  fcse.midhook(dispatch, function(ctx)
    local signal = fcse.mem.read_ptr(ctx.esp + 4)
    if signal ~= 0 and fcse.mem.read_u32(signal) == SIGNAL_TOGGLE then
      toggles = toggles + 1
    end
  end)

  fcse.on('update', function()
    local player = get_local_player()
    if player == nil then
      if owner ~= nil then
        forget()
        owner = nil
      end
      toggles = 0
      return
    end
    local address = tonumber(ffi.cast('uintptr_t', player))
    if address ~= owner then
      forget()
      owner = address
    end
    hold_click()

    if toggles > 0 then
      on = not on
      if on then
        configure()
        trace = true
      end
      set_enabled(on)
      click()
      fcse.log('flashlight: ' .. (on and 'on' or 'off'))
    end
    toggles = 0

    if on then
      place(player)
    end
  end)
end)

fcse.setting{
  name = 'Shadows',
  default = true,
  on_changed = function(value)
    cast_shadows = value
    if on then
      configure()
    end
  end,
}
