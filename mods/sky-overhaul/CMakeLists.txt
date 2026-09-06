# CMakeLists.txt for Sky Overhaul - an FCSE plugin. See README.md.
#
# Deliberately a project of its own rather than a target inside tools/FCSE: this is a mod built on
# the loader, not part of it, and the only thing it takes from that tree is the one public header a
# third-party plugin would copy out of it. If this ever fails to build because it reached into
# FCSE's sources, that is the bug, not the include path below.
cmake_minimum_required (VERSION 3.10)

# Static CRT (/MT), matching FCSE.exe - a /MD plugin builds and runs fine on a developer machine but
# demands the Visual C++ redistributable from every player who installs it, and the failure lands as
# a missing-DLL box with nothing in fcse.log.
#
# MUST be set before project(): with CMP0091 OLD, project() bakes /MD into the per-config flags and
# CMAKE_MSVC_RUNTIME_LIBRARY is silently ignored from then on.
if (POLICY CMP0091)
  cmake_policy(SET CMP0091 NEW)
  set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()

project ("SkyOverhaul")

if (NOT CMAKE_SIZEOF_VOID_P EQUAL 4)
  message(FATAL_ERROR "SkyOverhaul must be built 32-bit (x86) - Far Cry 2 is a 32-bit process and a "
                      "64-bit DLL cannot load into it. Select the x86-debug/x86-release preset.")
endif()

set(FCSE_INCLUDE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../../tools/FCSE/include")
if (NOT EXISTS "${FCSE_INCLUDE_DIR}/fcse_api.h")
  message(FATAL_ERROR "fcse_api.h was not found at ${FCSE_INCLUDE_DIR}. SkyOverhaul is an FCSE "
                      "plugin and that header is its entire dependency - it is expected at "
                      "tools/FCSE/include relative to the repository root.")
endif()

add_library(SkyOverhaul SHARED
  "src/main.cpp"
  "src/veil.cpp"
  "src/engine/sun_occlusion.cpp"
  "src/engine/sky_state.cpp"
)

# src/ on the path so every #include reads from the root of this tree - "engine/sky_state.h", never
# "../engine/sky_state.h" - and moving a file between folders does not rewrite anyone's includes.
target_include_directories(SkyOverhaul PRIVATE
  "${CMAKE_CURRENT_SOURCE_DIR}/src"
  "${FCSE_INCLUDE_DIR}"
)

# The wash is drawn through the game's own device, so the plugin links Direct3D only to name the
# interface and read its vtable; it never creates a device of the game's.
target_link_libraries(SkyOverhaul PRIVATE d3d9)

# No "lib" prefix and no decoration: bin\plugins\SkyOverhaul.dll is what the player installs and
# what fcse.log names in every line this plugin writes.
set_target_properties(SkyOverhaul PROPERTIES OUTPUT_NAME "SkyOverhaul" PREFIX "")

if (CMAKE_VERSION VERSION_GREATER 3.12)
  set_property(TARGET SkyOverhaul PROPERTY CXX_STANDARD 20)
endif()
