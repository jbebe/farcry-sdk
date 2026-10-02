# CMakeLists.txt for Weapon Overhaul - an FCSE plugin. See README.md.
cmake_minimum_required (VERSION 3.21)

# Static CRT (/MT), matching FCSE.exe; must be set before project() to take effect.
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

project ("WeaponOverhaul")

if (NOT CMAKE_SIZEOF_VOID_P EQUAL 4)
  message(FATAL_ERROR "WeaponOverhaul must be built 32-bit (x86) - Far Cry 2 is a 32-bit process. "
                      "Select the x86-debug/x86-release preset.")
endif()

# fxc ships with the Windows SDK the MSVC toolchain already requires, so shaders are compiled at
# build time and embedded as headers.
find_program(FXC_EXECUTABLE NAMES fxc
  HINTS "$ENV{WindowsSdkVerBinPath}x86" "$ENV{WindowsSdkVerBinPath}x64")
if (NOT FXC_EXECUTABLE)
  message(FATAL_ERROR "fxc.exe was not found. It ships with the Windows SDK; build.ps1 sets up a "
                      "developer prompt that puts it on PATH.")
endif()

# source file : entry point : profile : generated header : array name.
set(SHADER_PASSES
  "blur.fx:DownPS:ps_2_b:blur_down_ps.h:g_blurDownPixelShader"
  "blur.fx:BlurPS:ps_2_b:blur_blur_ps.h:g_blurPixelShader"
  "blur.fx:CompositePS:ps_2_b:blur_composite_ps.h:g_blurCompositePixelShader")

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

add_library(WeaponOverhaul SHARED
  "src/main.cpp"
  "src/blur.cpp"
  "src/sway.cpp"
  "src/engine/aim.cpp"
  "src/engine/device_reset.cpp"
  "src/engine/frame.cpp"
  "src/engine/screen_draw.cpp"
  "src/engine/shader.cpp"
  "src/engine/trace.cpp"
  "src/engine/vtable.cpp"
  "src/engine/weapon_draws.cpp"
  ${SHADER_HEADERS}
)

target_include_directories(WeaponOverhaul PRIVATE
  "${CMAKE_CURRENT_SOURCE_DIR}/src"
  "${CMAKE_CURRENT_BINARY_DIR}/generated"
  "${CMAKE_CURRENT_SOURCE_DIR}/../../tools/FCSE/include"
)
target_link_libraries(WeaponOverhaul PRIVATE d3d9)
target_compile_features(WeaponOverhaul PRIVATE cxx_std_20)
