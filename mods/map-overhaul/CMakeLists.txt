# CMakeLists.txt for Map Overhaul's plugin - an FCSE plugin. See README.md.
cmake_minimum_required (VERSION 3.21)

# Static CRT (/MT), matching FCSE.exe; must be set before project() to take effect.
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

project ("MapOverhaul")

if (NOT CMAKE_SIZEOF_VOID_P EQUAL 4)
  message(FATAL_ERROR "MapOverhaul must be built 32-bit (x86) - Far Cry 2 is a 32-bit process. "
                      "Select the x86-debug/x86-release preset.")
endif()

add_library(MapOverhaul SHARED
  "src/main.cpp"
  "src/gps.cpp"
)

target_include_directories(MapOverhaul PRIVATE
  "${CMAKE_CURRENT_SOURCE_DIR}/src"
  "${CMAKE_CURRENT_SOURCE_DIR}/../../tools/FCSE/include"
)

set_target_properties(MapOverhaul PROPERTIES OUTPUT_NAME "MapOverhaul" PREFIX "" CXX_STANDARD 20)
