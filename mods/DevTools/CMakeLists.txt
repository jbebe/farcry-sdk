# CMakeLists.txt for DevTools - an FCSE plugin. See README.md.
#
# Deliberately a project of its own rather than a target inside tools/FCSE: DevTools is a mod built
# on the loader, not part of it, and the only thing it takes from that tree is the one public header
# a third-party plugin would copy out of it. If this ever fails to build because it reached into
# FCSE's sources, that is the bug, not the include path below.
cmake_minimum_required (VERSION 3.10)

# Static CRT (/MT), matching FCSE.exe - a /MD plugin builds and runs fine on a developer machine but
# demands the Visual C++ redistributable from every player who installs it, and the failure lands as
# a missing-DLL box with nothing in fcse.log. See tools/FCSE/CMakeLists.txt for the full note.
#
# MUST be set before project(): with CMP0091 OLD, project() bakes /MD into the per-config flags and
# CMAKE_MSVC_RUNTIME_LIBRARY is silently ignored from then on.
if (POLICY CMP0091)
  cmake_policy(SET CMP0091 NEW)
  set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()

project ("DevTools")

if (NOT CMAKE_SIZEOF_VOID_P EQUAL 4)
  message(FATAL_ERROR "DevTools must be built 32-bit (x86) - Far Cry 2 is a 32-bit process and a "
                      "64-bit DLL cannot load into it. Select the x86-debug/x86-release preset.")
endif()

set(FCSE_INCLUDE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../../tools/FCSE/include")
if (NOT EXISTS "${FCSE_INCLUDE_DIR}/fcse_api.h")
  message(FATAL_ERROR "fcse_api.h was not found at ${FCSE_INCLUDE_DIR}. DevTools is an FCSE plugin "
                      "and that header is its entire dependency on the loader - it is expected at "
                      "tools/FCSE/include relative to the repository root.")
endif()

# Dear ImGui - the overlay's widgets, and the one piece of third-party code here. The library comes
# from include/devtools_imgui.cmake, identical in every plugin that adds a window. The Win32 and D3D9
# backends are DevTools' alone and compiled into the plugin below: they are what Far Cry 2 calls for,
# a windowed message loop and Direct3DCreate9.
set(DEVTOOLS_INCLUDE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/include")
include("${DEVTOOLS_INCLUDE_DIR}/devtools_imgui.cmake")

add_library(DevTools SHARED
  "src/main.cpp"
  "src/commands/catalog.cpp"
  "src/engine/camera.cpp"
  "src/engine/console.cpp"
  "src/engine/debug_camera.cpp"
  "src/engine/entity.cpp"
  "src/engine/game_profile.cpp"
  "src/engine/game_thread.cpp"
  "src/engine/input.cpp"
  "src/engine/keys.cpp"
  "src/engine/pawn_tick.cpp"
  "src/engine/player.cpp"
  "src/engine/renderer.cpp"
  "src/engine/weather.cpp"
  "src/fixes/load_savegame.cpp"
  "src/options/developer_console.cpp"
  "src/options/diamonds.cpp"
  "src/options/fps_counter.cpp"
  "src/options/freecam.cpp"
  "src/options/infinite_ammo.cpp"
  "src/options/invincibility.cpp"
  "src/options/noclip.cpp"
  "src/options/options.cpp"
  "src/options/skip_system_detection.cpp"
  "src/options/unlock_all_weapons.cpp"
  "src/overlay/overlay.cpp"
  "${imgui_SOURCE_DIR}/backends/imgui_impl_dx9.cpp"
  "${imgui_SOURCE_DIR}/backends/imgui_impl_win32.cpp"
)

# d3d9 for the overlay's own device handling, dinput8 for taking the mouse and keyboard off the game
# while the overlay is up. Both ship with the Windows SDK; the legacy DirectX SDK is not needed.
# ole32 is for the one CoInitializeEx call that holds dxdiagn.dll loaded under the cached provider.
target_link_libraries(DevTools PRIVATE imgui d3d9 dinput8 dxguid ole32)

# src/ on the path so every #include reads from the root of this tree rather than relative to the
# including file, and moving a file between folders does not rewrite anyone's includes.
target_include_directories(DevTools PRIVATE
  "${CMAKE_CURRENT_SOURCE_DIR}/src"
  "${DEVTOOLS_INCLUDE_DIR}"
  "${FCSE_INCLUDE_DIR}"
  "${imgui_SOURCE_DIR}/backends"
)

# No "lib" prefix and no decoration: bin\plugins\DevTools.dll is what the player installs and what
# fcse.log names in every line this plugin writes.
set_target_properties(DevTools PROPERTIES OUTPUT_NAME "DevTools" PREFIX "")

if (CMAKE_VERSION VERSION_GREATER 3.12)
  set_property(TARGET DevTools PROPERTY CXX_STANDARD 20)
endif()
