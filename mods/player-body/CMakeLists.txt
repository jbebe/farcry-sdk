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

find_program(FXC_EXECUTABLE NAMES fxc
  HINTS "$ENV{WindowsSdkVerBinPath}x86" "$ENV{WindowsSdkVerBinPath}x64")
if (NOT FXC_EXECUTABLE)
  message(FATAL_ERROR "fxc.exe was not found. It ships with the Windows SDK; build through build.ps1.")
endif()

set(SHADOW_SHADER "${CMAKE_CURRENT_SOURCE_DIR}/src/shaders/shadow.fx")
set(SHADOW_HEADER "${CMAKE_CURRENT_BINARY_DIR}/generated/shadow_ps.h")
add_custom_command(
  OUTPUT "${SHADOW_HEADER}"
  COMMAND "${CMAKE_COMMAND}" -E make_directory "${CMAKE_CURRENT_BINARY_DIR}/generated"
  COMMAND "${FXC_EXECUTABLE}" /nologo /T ps_2_b /E MainPS /O3 /Vn g_shadowPixelShader
          /Fh "${SHADOW_HEADER}" "${SHADOW_SHADER}"
  DEPENDS "${SHADOW_SHADER}"
  COMMENT "Compiling src/shaders/shadow.fx"
  VERBATIM)

add_library(PlayerBody SHARED
  "src/main.cpp"
  "src/body.cpp"
  "src/legs.cpp"
  "src/shadow.cpp"
  "src/third_person.cpp"
  "src/engine/camera.cpp"
  "src/engine/graphic.cpp"
  "src/engine/pawn.cpp"
  "src/engine/scene_depth.cpp"
  "src/engine/skeleton.cpp"
  "src/engine/skin.cpp"
  "src/engine/sky_pass.cpp"
  "src/engine/third_camera.cpp"
  "src/engine/vtable.cpp"
  "${SHADOW_HEADER}"
)

target_include_directories(PlayerBody PRIVATE
  "${CMAKE_CURRENT_SOURCE_DIR}/src"
  "${CMAKE_CURRENT_BINARY_DIR}/generated"
  "${FCSE_INCLUDE_DIR}"
)

target_link_libraries(PlayerBody PRIVATE d3d9)

set_target_properties(PlayerBody PROPERTIES OUTPUT_NAME "PlayerBody" PREFIX "" CXX_STANDARD 20)
