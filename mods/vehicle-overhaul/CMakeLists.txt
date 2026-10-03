# CMakeLists.txt for Vehicle Overhaul - an FCSE plugin. See README.md.
cmake_minimum_required (VERSION 3.21)

# Static CRT (/MT), matching FCSE.exe; must be set before project() to take effect.
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

project ("VehicleOverhaul")

if (NOT CMAKE_SIZEOF_VOID_P EQUAL 4)
  message(FATAL_ERROR "VehicleOverhaul must be built 32-bit (x86) - Far Cry 2 is a 32-bit process. "
                      "Select the x86-debug/x86-release preset.")
endif()

# DevTools' overlay contract and the Dear ImGui it pins. The tuning window appears only with DevTools
# installed; without it the plugin runs unchanged.
set(DEVTOOLS_INCLUDE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../DevTools/include")
include("${DEVTOOLS_INCLUDE_DIR}/devtools_imgui.cmake")

add_library(VehicleOverhaul SHARED
  "src/main.cpp"
  "src/chase.cpp"
  "src/drivetrain.cpp"
  "src/physics.cpp"
  "src/real_vehicle.cpp"
  "src/sounds.cpp"
  "src/third_person.cpp"
  "src/tuning.cpp"
  "src/engine/camera.cpp"
  "src/engine/entity.cpp"
  "src/engine/input.cpp"
  "src/engine/pawn_tick.cpp"
  "src/engine/sound_bank.cpp"
  "src/engine/sound_system.cpp"
  "src/engine/terrain.cpp"
  "src/engine/third_camera.cpp"
  "src/engine/vehicle.cpp"
  "src/engine/vehicle_sound.cpp"
  "src/engine/wheeled.cpp"
)

target_include_directories(VehicleOverhaul PRIVATE
  "${CMAKE_CURRENT_SOURCE_DIR}/src"
  "${CMAKE_CURRENT_SOURCE_DIR}/../../tools/FCSE/include"
  "${DEVTOOLS_INCLUDE_DIR}"
)
target_compile_features(VehicleOverhaul PRIVATE cxx_std_20)
# Dear ImGui is the core alone, drawing into DevTools' context - the backends are DevTools'.
target_link_libraries(VehicleOverhaul PRIVATE imgui)
