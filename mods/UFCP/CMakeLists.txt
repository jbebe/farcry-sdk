# CMakeLists.txt for UFCP (Unofficial Far Cry Patch) - an FCSE plugin. See README.md.
#
# Deliberately a project of its own rather than a target inside tools/FCSE: UFCP is a mod built on
# the loader, not part of it, and the only thing it takes from that tree is the one public header a
# third-party plugin would copy out of it. If this ever fails to build because it reached into
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

project ("UFCP")

if (NOT CMAKE_SIZEOF_VOID_P EQUAL 4)
  message(FATAL_ERROR "UFCP must be built 32-bit (x86) - Far Cry 2 is a 32-bit process and a 64-bit "
                      "DLL cannot load into it. Select the x86-debug/x86-release preset.")
endif()

set(FCSE_INCLUDE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../../tools/FCSE/include")
if (NOT EXISTS "${FCSE_INCLUDE_DIR}/fcse_api.h")
  message(FATAL_ERROR "fcse_api.h was not found at ${FCSE_INCLUDE_DIR}. UFCP is an FCSE plugin and "
                      "that header is its entire dependency - it is expected at tools/FCSE/include "
                      "relative to the repository root.")
endif()

add_library(UFCP SHARED
  "src/main.cpp"
  "src/fixes/jackal_tapes.cpp"
  "src/fixes/bonus_content.cpp"
  "src/fixes/load_savegame.cpp"
  "src/fixes/exit_teardown.cpp"
  "src/options/fov.cpp"
  "src/options/affinity.cpp"
  "src/options/developer_console.cpp"
)

# src/ on the path so every #include reads from the root of this tree - "fixes/fixes.h", never
# "../fixes/fixes.h" - and moving a file between folders does not rewrite anyone's includes.
target_include_directories(UFCP PRIVATE
  "${CMAKE_CURRENT_SOURCE_DIR}/src"
  "${FCSE_INCLUDE_DIR}"
)

# No "lib" prefix and no decoration: bin\plugins\UFCP.dll is what the player installs and what
# fcse.log names in every line this plugin writes.
set_target_properties(UFCP PROPERTIES OUTPUT_NAME "UFCP" PREFIX "")

if (CMAKE_VERSION VERSION_GREATER 3.12)
  set_property(TARGET UFCP PROPERTY CXX_STANDARD 20)
endif()
