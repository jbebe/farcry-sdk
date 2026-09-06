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

# Dear ImGui - the overlay's widgets, and the one piece of third-party code here. Upstream ships no
# CMakeLists.txt, so MakeAvailable populates and stops, and this is the same from-source static
# library tools/FCSE/CMakeLists.txt builds MinHook as. The Win32 and D3D9 backends are the ones Far
# Cry 2 calls for: a windowed message loop and Direct3DCreate9.
include(FetchContent)
FetchContent_Declare(
  imgui
  GIT_REPOSITORY https://github.com/ocornut/imgui.git
  GIT_TAG        v1.91.5
)
FetchContent_MakeAvailable(imgui)

add_library(imgui STATIC
  "${imgui_SOURCE_DIR}/imgui.cpp"
  "${imgui_SOURCE_DIR}/imgui_draw.cpp"
  "${imgui_SOURCE_DIR}/imgui_tables.cpp"
  "${imgui_SOURCE_DIR}/imgui_widgets.cpp"
  "${imgui_SOURCE_DIR}/misc/cpp/imgui_stdlib.cpp"
  "${imgui_SOURCE_DIR}/backends/imgui_impl_dx9.cpp"
  "${imgui_SOURCE_DIR}/backends/imgui_impl_win32.cpp"
)
target_include_directories(imgui PUBLIC "${imgui_SOURCE_DIR}" "${imgui_SOURCE_DIR}/backends")

add_library(DevTools SHARED
  "src/main.cpp"
  "src/commands/catalog.cpp"
  "src/engine/console.cpp"
  "src/engine/game_thread.cpp"
  "src/engine/input.cpp"
  "src/engine/renderer.cpp"
  "src/fixes/load_savegame.cpp"
  "src/options/developer_console.cpp"
  "src/overlay/overlay.cpp"
)

# d3d9 for the overlay's own device handling, dinput8 for taking the mouse and keyboard off the game
# while the overlay is up. Both ship with the Windows SDK; the legacy DirectX SDK is not needed.
target_link_libraries(DevTools PRIVATE imgui d3d9 dinput8 dxguid)

# src/ on the path so every #include reads from the root of this tree rather than relative to the
# including file, and moving a file between folders does not rewrite anyone's includes.
target_include_directories(DevTools PRIVATE
  "${CMAKE_CURRENT_SOURCE_DIR}/src"
  "${FCSE_INCLUDE_DIR}"
)

# No "lib" prefix and no decoration: bin\plugins\DevTools.dll is what the player installs and what
# fcse.log names in every line this plugin writes.
set_target_properties(DevTools PROPERTIES OUTPUT_NAME "DevTools" PREFIX "")

if (CMAKE_VERSION VERSION_GREATER 3.12)
  set_property(TARGET DevTools PROPERTY CXX_STANDARD 20)
endif()
