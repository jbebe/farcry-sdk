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
                      "plugin and that header is its entire dependency on the loader - it is "
                      "expected at tools/FCSE/include relative to the repository root.")
endif()

# DevTools' overlay contract and the Dear ImGui it pins - the one thing taken from another mod. The
# window they add appears only with DevTools installed; without it the plugin runs unchanged.
set(DEVTOOLS_INCLUDE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../DevTools/include")
include("${DEVTOOLS_INCLUDE_DIR}/devtools_imgui.cmake")

# fxc ships with the Windows SDK that the MSVC toolchain already requires, and vcvarsall puts its
# folder on PATH - so the shader is compiled at build time and embedded as a header, and neither
# the plugin nor the player needs D3DX or the long-dead DirectX SDK.
find_program(FXC_EXECUTABLE NAMES fxc
  HINTS "$ENV{WindowsSdkVerBinPath}x86" "$ENV{WindowsSdkVerBinPath}x64")
if (NOT FXC_EXECUTABLE)
  message(FATAL_ERROR "fxc.exe was not found. It ships with the Windows SDK; building from a "
                      "developer prompt (or through build.ps1, which sets one up) puts it on PATH.")
endif()

# source file : entry point : profile : generated header : array name. The dazzle's MainPS needs
# ps_2_b for its instruction count and the accumulation passes fit in ps_2_0; everything drawn inside
# a world pass needs ps_3_0, which in turn needs the vs_3_0 ClipQuad draws through.
set(SHADER_PASSES
  "dazzle.fx:MainPS:ps_2_b:dazzle_ps.h:g_dazzlePixelShader"
  "dazzle.fx:AccumulatePS:ps_2_0:dazzle_accumulate_ps.h:g_dazzleAccumulatePixelShader"
  "dazzle.fx:BleachPS:ps_2_0:dazzle_bleach_ps.h:g_dazzleBleachPixelShader"
  "clip_quad.fx:MainVS:vs_3_0:clip_quad_vs.h:g_clipQuadVertexShader"
  "clouds.fx:MainPS:ps_3_0:clouds_ps.h:g_cloudsPixelShader"
  "clouds.fx:CoverPS:ps_3_0:clouds_cover_ps.h:g_cloudsCoverPixelShader"
  "clouds.fx:MaskPS:ps_3_0:clouds_mask_ps.h:g_cloudsMaskPixelShader"
  "clouds.fx:ShadowPS:ps_3_0:clouds_shadow_ps.h:g_cloudsShadowPixelShader"
  "sky.fx:MainPS:ps_3_0:sky_ps.h:g_skyPixelShader"
  "night.fx:MainPS:ps_3_0:night_ps.h:g_nightPixelShader"
  "shadows.fx:BlurPS:ps_3_0:shadows_blur_ps.h:g_shadowsBlurPixelShader"
  "shadows.fx:ApplyPS:ps_3_0:shadows_apply_ps.h:g_shadowsApplyPixelShader")

# Included by more than one shader source, so any change to it recompiles them all.
set(SHADER_INCLUDES "${CMAKE_CURRENT_SOURCE_DIR}/src/shaders/shadows.inc.fx")

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
    DEPENDS "${SOURCE}" ${SHADER_INCLUDES}
    COMMENT "Compiling src/shaders/${SOURCE_NAME} (${ENTRY})"
    VERBATIM)
  list(APPEND SHADER_HEADERS "${HEADER}")
endforeach()

add_library(SkyOverhaul SHARED
  "src/main.cpp"
  "src/clouds.cpp"
  "src/dazzle.cpp"
  "src/grade.cpp"
  "src/night.cpp"
  "src/shadows.cpp"
  "src/sky.cpp"
  "src/sky_model.cpp"
  "src/tuning.cpp"
  "src/engine/camera.cpp"
  "src/engine/cloud_layer.cpp"
  "src/engine/depth_texture.cpp"
  "src/engine/device_reset.cpp"
  "src/engine/dome_draw.cpp"
  "src/engine/fog_tint.cpp"
  "src/engine/known_shaders.cpp"
  "src/engine/noise.cpp"
  "src/engine/frame.cpp"
  "src/engine/screen_draw.cpp"
  "src/engine/shader.cpp"
  "src/engine/sun_occlusion.cpp"
  "src/engine/time_of_day.cpp"
  "src/engine/vtable.cpp"
  ${SHADER_HEADERS}
)

# src/ on the path so every #include reads from the root of this tree - "engine/frame.h", never
# "../engine/frame.h" - and moving a file between folders does not rewrite anyone's includes.
target_include_directories(SkyOverhaul PRIVATE
  "${CMAKE_CURRENT_SOURCE_DIR}/src"
  "${CMAKE_CURRENT_BINARY_DIR}/generated"
  "${DEVTOOLS_INCLUDE_DIR}"
  "${FCSE_INCLUDE_DIR}"
)

# The wash is drawn through the game's own device, so the plugin links Direct3D only to name the
# interface and read its vtable; it never creates a device of the game's. Dear ImGui is the core
# alone, drawing into DevTools' context - the backends are DevTools'.
target_link_libraries(SkyOverhaul PRIVATE d3d9 imgui)

# No "lib" prefix and no decoration: bin\plugins\SkyOverhaul.dll is what the player installs and
# what fcse.log names in every line this plugin writes.
set_target_properties(SkyOverhaul PROPERTIES OUTPUT_NAME "SkyOverhaul" PREFIX "")

if (CMAKE_VERSION VERSION_GREATER 3.12)
  set_property(TARGET SkyOverhaul PROPERTY CXX_STANDARD 20)
endif()
