# CMakeLists.txt for Sound Overhaul - an FCSE plugin. See README.md.
cmake_minimum_required (VERSION 3.21)

# Static CRT (/MT), matching FCSE.exe; must be set before project() to take effect.
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

project ("SoundOverhaul")

if (NOT CMAKE_SIZEOF_VOID_P EQUAL 4)
  message(FATAL_ERROR "Sound Overhaul must be built 32-bit (x86) - Far Cry 2 is a 32-bit process. "
                      "Select the x86-debug/x86-release preset.")
endif()

# DevTools' overlay contract and the Dear ImGui it pins. The window they add appears only with DevTools
# installed; without it the plugin runs unchanged.
set(DEVTOOLS_INCLUDE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../DevTools/include")
include("${DEVTOOLS_INCLUDE_DIR}/devtools_imgui.cmake")

add_library(SoundOverhaul SHARED
  "src/main.cpp"
  "src/dsoal.cpp"
  "src/enumeration_fix.cpp"
  "src/reverb.cpp"
  "src/player_reverb.cpp"
  "src/per_round_shots.cpp"
  "src/last_echo.cpp"
  "src/mutes.cpp"
  "src/rooms.cpp"
  "src/combat_music.cpp"
)

target_include_directories(SoundOverhaul PRIVATE
  "${CMAKE_CURRENT_SOURCE_DIR}/../../tools/FCSE/include"
  "${DEVTOOLS_INCLUDE_DIR}"
)
target_compile_features(SoundOverhaul PRIVATE cxx_std_20)
# Dear ImGui is the core alone, drawing into DevTools' context - the backends are DevTools'.
target_link_libraries(SoundOverhaul PRIVATE imgui)
