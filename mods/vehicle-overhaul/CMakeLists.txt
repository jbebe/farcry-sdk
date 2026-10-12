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

# fxc ships with the Windows SDK the toolchain already requires, and vcvarsall puts it on PATH, so
# the shaders are compiled at build time and embedded as headers.
find_program(FXC_EXECUTABLE NAMES fxc
  HINTS "$ENV{WindowsSdkVerBinPath}x86" "$ENV{WindowsSdkVerBinPath}x64")
if (NOT FXC_EXECUTABLE)
  message(FATAL_ERROR "fxc.exe was not found. It ships with the Windows SDK; building through "
                      "build.ps1, which sets up a developer prompt, puts it on PATH.")
endif()

# source file : entry point : profile : generated header : array name
set(SHADER_PASSES
  "lamp_glow.fx:MainPS:ps_2_0:lamp_glow_ps.h:g_lampGlowPixelShader")

set(SHADER_HEADERS "")
foreach(PASS IN LISTS SHADER_PASSES)
  string(REPLACE ":" ";" PARTS "${PASS}")
  list(GET PARTS 0 SOURCE_NAME)
  list(GET PARTS 1 ENTRY)
  list(GET PARTS 2 PROFILE)
  list(GET PARTS 3 HEADER_NAME)
  list(GET PARTS 4 ARRAY_NAME)
  set(SOURCE "${CMAKE_CURRENT_SOURCE_DIR}/src/shaders/${SOURCE_NAME}")
  set(HEADER "${CMAKE_CURRENT_BINARY_DIR}/generated/${HEADER_NAME}")
  add_custom_command(
    OUTPUT "${HEADER}"
    COMMAND "${CMAKE_COMMAND}" -E make_directory "${CMAKE_CURRENT_BINARY_DIR}/generated"
    COMMAND "${FXC_EXECUTABLE}" /nologo /T ${PROFILE} /E ${ENTRY} /O3 /Vn ${ARRAY_NAME}
            /Fh "${HEADER}" "${SOURCE}"
    DEPENDS "${SOURCE}"
    COMMENT "Compiling src/shaders/${SOURCE_NAME} (${ENTRY})"
    VERBATIM)
  list(APPEND SHADER_HEADERS "${HEADER}")
endforeach()

add_library(VehicleOverhaul SHARED
  "src/main.cpp"
  "src/axles.cpp"
  "src/chase.cpp"
  "src/drivetrain.cpp"
  "src/gearbox.cpp"
  "src/lamp_glow.cpp"
  "src/lights.cpp"
  "src/physics.cpp"
  "src/real_vehicle.cpp"
  "src/sounds.cpp"
  "src/third_person.cpp"
  "src/tuning.cpp"
  "src/engine/camera.cpp"
  "src/engine/device_reset.cpp"
  "src/engine/entity.cpp"
  "src/engine/frame.cpp"
  "src/engine/handoff.cpp"
  "src/engine/headlights.cpp"
  "src/engine/input.cpp"
  "src/engine/pawn_tick.cpp"
  "src/engine/screen_draw.cpp"
  "src/engine/shader.cpp"
  "src/engine/sound_bank.cpp"
  "src/engine/sound_system.cpp"
  "src/engine/terrain.cpp"
  "src/engine/third_camera.cpp"
  "src/engine/vehicle.cpp"
  "src/engine/vehicle_sound.cpp"
  "src/engine/view.cpp"
  "src/engine/vtable.cpp"
  "src/engine/wheeled.cpp"
  ${SHADER_HEADERS}
)

target_include_directories(VehicleOverhaul PRIVATE
  "${CMAKE_CURRENT_SOURCE_DIR}/src"
  "${CMAKE_CURRENT_BINARY_DIR}/generated"
  "${CMAKE_CURRENT_SOURCE_DIR}/../../tools/FCSE/include"
  "${DEVTOOLS_INCLUDE_DIR}"
)
target_compile_features(VehicleOverhaul PRIVATE cxx_std_20)
# Dear ImGui is the core alone, drawing into DevTools' context - the backends are DevTools'.
target_link_libraries(VehicleOverhaul PRIVATE imgui d3d9)
