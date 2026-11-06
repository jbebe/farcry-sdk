# CMakeLists.txt for Player Body - an FCSE plugin. See README.md.
cmake_minimum_required (VERSION 3.21)

# Static CRT (/MT), matching FCSE.exe; must be set before project() to take effect.
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

project ("PlayerBody")

if (NOT CMAKE_SIZEOF_VOID_P EQUAL 4)
  message(FATAL_ERROR "PlayerBody must be built 32-bit (x86) - Far Cry 2 is a 32-bit process. "
                      "Select the x86-debug/x86-release preset.")
endif()

set(FCSE_INCLUDE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../../tools/FCSE/include")

add_library(PlayerBody SHARED
  "src/main.cpp"
  "src/body.cpp"
  "src/legs.cpp"
  "src/engine/camera.cpp"
  "src/engine/graphic.cpp"
  "src/engine/pawn.cpp"
  "src/engine/skeleton.cpp"
  "src/engine/skin.cpp"
)

target_include_directories(PlayerBody PRIVATE
  "${CMAKE_CURRENT_SOURCE_DIR}/src"
  "${FCSE_INCLUDE_DIR}"
)

set_target_properties(PlayerBody PROPERTIES OUTPUT_NAME "PlayerBody" PREFIX "" CXX_STANDARD 20)
