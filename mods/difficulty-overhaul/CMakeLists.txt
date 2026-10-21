# CMakeLists.txt for Difficulty Overhaul - an FCSE plugin. See README.md.
cmake_minimum_required (VERSION 3.21)

# Static CRT (/MT), matching FCSE.exe; must be set before project() to take effect.
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

project ("DifficultyOverhaul")

if (NOT CMAKE_SIZEOF_VOID_P EQUAL 4)
  message(FATAL_ERROR "Difficulty Overhaul must be built 32-bit (x86) - Far Cry 2 is a 32-bit "
                      "process. Select the x86-debug/x86-release preset.")
endif()

add_library(DifficultyOverhaul SHARED
  "src/main.cpp"
  "src/hit_log.cpp"
  "src/damage_taken.cpp"
  "src/damage_dealt.cpp"
  "src/engine/damage.cpp"
  "src/engine/game.cpp"
)

target_include_directories(DifficultyOverhaul PRIVATE
  "${CMAKE_CURRENT_SOURCE_DIR}/src"
  "${CMAKE_CURRENT_SOURCE_DIR}/../../tools/FCSE/include"
)
target_compile_features(DifficultyOverhaul PRIVATE cxx_std_20)
