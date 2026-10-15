# CMakeLists.txt for Legacy Plugin - an FCSE plugin. See README.md.
#
# A project of its own, like mods/UFCP: the only thing it takes from tools/FCSE is the public header.
cmake_minimum_required (VERSION 3.10)

# Static CRT (/MT), matching FCSE.exe, so players need no Visual C++ redistributable. Must be set
# before project(), or CMP0091 OLD bakes /MD into the per-config flags.
if (POLICY CMP0091)
  cmake_policy(SET CMP0091 NEW)
  set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()

project ("LegacyPlugin")

if (NOT CMAKE_SIZEOF_VOID_P EQUAL 4)
  message(FATAL_ERROR "Legacy Plugin must be built 32-bit (x86) - Far Cry 2 is a 32-bit process and "
                      "a 64-bit DLL cannot load into it. Select the x86-debug/x86-release preset.")
endif()

set(FCSE_INCLUDE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../../tools/FCSE/include")
if (NOT EXISTS "${FCSE_INCLUDE_DIR}/fcse_api.h")
  message(FATAL_ERROR "fcse_api.h was not found at ${FCSE_INCLUDE_DIR}. Legacy Plugin is an FCSE "
                      "plugin and that header is its entire dependency - it is expected at "
                      "tools/FCSE/include relative to the repository root.")
endif()

add_library(LegacyPlugin SHARED
  "src/main.cpp"
  "src/options/briefing_control.cpp"
  "src/options/rescue_keeps_weapon.cpp"
  "src/options/no_forced_rescue.cpp"
  "src/options/reputation_kept.cpp"
  "src/options/no_diamond_message.cpp"
  "src/options/no_blinking_items.cpp"
  "src/options/no_save_icons.cpp"
  "src/options/limited_saving.cpp"
  "src/options/brown_filter.cpp"
  "src/options/no_hit_indicator.cpp"
  "src/options/hud_fade.cpp"
  "src/options/skip_tutorials.cpp"
  "src/options/aim_toggle.cpp"
  "src/options/sprint_toggle.cpp"
  "src/options/sprint_turn.cpp"
  "src/options/no_rim_light.cpp"
  "src/options/no_blur.cpp"
)

target_include_directories(LegacyPlugin PRIVATE
  "${CMAKE_CURRENT_SOURCE_DIR}/src"
  "${FCSE_INCLUDE_DIR}"
)

# No "lib" prefix: bin\plugins\LegacyPlugin.dll is what the player installs and what fcse.log names.
set_target_properties(LegacyPlugin PROPERTIES OUTPUT_NAME "LegacyPlugin" PREFIX "")

if (CMAKE_VERSION VERSION_GREATER 3.12)
  set_property(TARGET LegacyPlugin PROPERTY CXX_STANDARD 20)
endif()
