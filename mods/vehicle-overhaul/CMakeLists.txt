# CMakeLists.txt for Vehicle Overhaul - an FCSE plugin. See README.md.
cmake_minimum_required (VERSION 3.21)

# Static CRT (/MT), matching FCSE.exe; must be set before project() to take effect.
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

project ("VehicleOverhaul")

if (NOT CMAKE_SIZEOF_VOID_P EQUAL 4)
  message(FATAL_ERROR "VehicleOverhaul must be built 32-bit (x86) - Far Cry 2 is a 32-bit process. "
                      "Select the x86-debug/x86-release preset.")
endif()

add_library(VehicleOverhaul SHARED
  "src/main.cpp"
  "src/chase.cpp"
  "src/third_person.cpp"
  "src/engine/camera.cpp"
  "src/engine/entity.cpp"
  "src/engine/input.cpp"
  "src/engine/pawn_tick.cpp"
  "src/engine/terrain.cpp"
  "src/engine/third_camera.cpp"
  "src/engine/vehicle.cpp"
)

target_include_directories(VehicleOverhaul PRIVATE
  "${CMAKE_CURRENT_SOURCE_DIR}/src"
  "${CMAKE_CURRENT_SOURCE_DIR}/../../tools/FCSE/include"
)
target_compile_features(VehicleOverhaul PRIVATE cxx_std_20)
