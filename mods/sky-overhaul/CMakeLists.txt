# CMakeLists.txt for Sky Overhaul - an FCSE plugin. See README.md.
#
# Deliberately a project of its own rather than a target inside tools/FCSE: this is a mod built on
# the loader, not part of it, and the only thing it takes from that tree is the one public header a
# third-party plugin would copy out of it. If this ever fails to build because it reached into
# FCSE's sources, that is the bug, not the include path below.
cmake_minimum_required (VERSION 3.10)

# Static CRT (/MT), matching FCSE.exe - a /MD plugin builds and runs fine on a developer machine but
# demands the Visual C++ redistributable from every player who installs it, and the failure lands as
# a missing-DLL box with nothing in fcse.log.
#
# MUST be set before project(): with CMP0091 OLD, project() bakes /MD into the per-config flags and
# CMAKE_MSVC_RUNTIME_LIBRARY is silently ignored from then on.
if (POLICY CMP0091)
  cmake_policy(SET CMP0091 NEW)
  set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()

project ("SkyOverhaul")

if (NOT CMAKE_SIZEOF_VOID_P EQUAL 4)
  message(FATAL_ERROR "SkyOverhaul must be built 32-bit (x86) - Far Cry 2 is a 32-bit process and a "
                      "64-bit DLL cannot load into it. Select the x86-debug/x86-release preset.")
endif()

set(FCSE_INCLUDE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../../tools/FCSE/include")
if (NOT EXISTS "${FCSE_INCLUDE_DIR}/fcse_api.h")
  message(FATAL_ERROR "fcse_api.h was not found at ${FCSE_INCLUDE_DIR}. SkyOverhaul is an FCSE "
                      "plugin and that header is its entire dependency - it is expected at "
                      "tools/FCSE/include relative to the repository root.")
endif()

# fxc ships with the Windows SDK that the MSVC toolchain already requires, and vcvarsall puts its
# folder on PATH - so the shader is compiled at build time and embedded as a header, and neither
# the plugin nor the player needs D3DX or the long-dead DirectX SDK.
find_program(FXC_EXECUTABLE NAMES fxc
  HINTS "$ENV{WindowsSdkVerBinPath}x86" "$ENV{WindowsSdkVerBinPath}x64")
if (NOT FXC_EXECUTABLE)
  message(FATAL_ERROR "fxc.exe was not found. It ships with the Windows SDK; building from a "
                      "developer prompt (or through build.ps1, which sets one up) puts it on PATH.")
endif()

set(DAZZLE_SHADER "${CMAKE_CURRENT_SOURCE_DIR}/src/shaders/dazzle.fx")

# entry point : profile : generated header : array name. MainPS needs ps_2_b for its instruction
# count; the two accumulation passes fit in ps_2_0.
set(DAZZLE_PASSES
  "MainPS:ps_2_b:dazzle_ps.h:g_dazzlePixelShader"
  "AccumulatePS:ps_2_0:dazzle_accumulate_ps.h:g_dazzleAccumulatePixelShader"
  "BleachPS:ps_2_0:dazzle_bleach_ps.h:g_dazzleBleachPixelShader")

set(DAZZLE_HEADERS "")
foreach(PASS IN LISTS DAZZLE_PASSES)
  string(REPLACE ":" ";" PARTS "${PASS}")
  list(GET PARTS 0 ENTRY)
  list(GET PARTS 1 PROFILE)
  list(GET PARTS 2 HEADER_NAME)
  list(GET PARTS 3 ARRAY_NAME)
  set(HEADER "${CMAKE_CURRENT_BINARY_DIR}/generated/${HEADER_NAME}")
  add_custom_command(
    OUTPUT "${HEADER}"
    COMMAND "${CMAKE_COMMAND}" -E make_directory "${CMAKE_CURRENT_BINARY_DIR}/generated"
    COMMAND "${FXC_EXECUTABLE}" /nologo /T ${PROFILE} /E ${ENTRY} /O3 /Vn ${ARRAY_NAME}
            /Fh "${HEADER}" "${DAZZLE_SHADER}"
    DEPENDS "${DAZZLE_SHADER}"
    COMMENT "Compiling src/shaders/dazzle.fx (${ENTRY})"
    VERBATIM)
  list(APPEND DAZZLE_HEADERS "${HEADER}")
endforeach()

add_library(SkyOverhaul SHARED
  "src/main.cpp"
  "src/dazzle.cpp"
  "src/engine/device_reset.cpp"
  "src/engine/frame.cpp"
  "src/engine/screen_draw.cpp"
  "src/engine/sky_state.cpp"
  "src/engine/sun_occlusion.cpp"
  ${DAZZLE_HEADERS}
)

# src/ on the path so every #include reads from the root of this tree - "engine/sky_state.h", never
# "../engine/sky_state.h" - and moving a file between folders does not rewrite anyone's includes.
target_include_directories(SkyOverhaul PRIVATE
  "${CMAKE_CURRENT_SOURCE_DIR}/src"
  "${CMAKE_CURRENT_BINARY_DIR}/generated"
  "${FCSE_INCLUDE_DIR}"
)

# The wash is drawn through the game's own device, so the plugin links Direct3D only to name the
# interface and read its vtable; it never creates a device of the game's.
target_link_libraries(SkyOverhaul PRIVATE d3d9)

# No "lib" prefix and no decoration: bin\plugins\SkyOverhaul.dll is what the player installs and
# what fcse.log names in every line this plugin writes.
set_target_properties(SkyOverhaul PROPERTIES OUTPUT_NAME "SkyOverhaul" PREFIX "")

if (CMAKE_VERSION VERSION_GREATER 3.12)
  set_property(TARGET SkyOverhaul PROPERTY CXX_STANDARD 20)
endif()
