# CMakeLists.txt for Flashlight - an FCSE plugin. See README.md.
cmake_minimum_required (VERSION 3.21)

# Static CRT (/MT), matching FCSE.exe; must be set before project() to take effect.
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

project ("Flashlight")

if (NOT CMAKE_SIZEOF_VOID_P EQUAL 4)
  message(FATAL_ERROR "Flashlight must be built 32-bit (x86) - Far Cry 2 is a 32-bit process. "
                      "Select the x86-debug/x86-release preset.")
endif()

add_library(Flashlight SHARED
  "src/main.cpp"
  "src/flashlight.cpp"
  "src/engine/frame.cpp"
  "src/engine/hud.cpp"
  "src/engine/input.cpp"
  "src/engine/light.cpp"
  "src/engine/player.cpp"
  "src/engine/sight.cpp"
  "src/engine/sound.cpp"
)

target_include_directories(Flashlight PRIVATE
  "${CMAKE_CURRENT_SOURCE_DIR}/src"
  "${CMAKE_CURRENT_SOURCE_DIR}/../../tools/FCSE/include"
)
target_compile_features(Flashlight PRIVATE cxx_std_20)
target_compile_definitions(Flashlight PRIVATE NOMINMAX)
