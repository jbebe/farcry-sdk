# CMakeLists.txt for the test range's scenarios - an FCSE plugin. See ../README.md.
cmake_minimum_required (VERSION 3.21)

# Static CRT (/MT), matching FCSE.exe; must be set before project() to take effect.
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

project ("TestRange")

if (NOT CMAKE_SIZEOF_VOID_P EQUAL 4)
  message(FATAL_ERROR "TestRange must be built 32-bit (x86) - Far Cry 2 is a 32-bit process. "
                      "Select the x86-debug/x86-release preset.")
endif()

# The window lives in DevTools' overlay, and its lines run through DevTools' console.
set(DEVTOOLS_INCLUDE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../../../mods/DevTools/include")
include("${DEVTOOLS_INCLUDE_DIR}/devtools_imgui.cmake")

add_library(TestRange SHARED
  "src/main.cpp"
  "src/scenarios.cpp"
  "src/window.cpp"
)

target_include_directories(TestRange PRIVATE
  "${CMAKE_CURRENT_SOURCE_DIR}/../../FCSE/include"
  "${DEVTOOLS_INCLUDE_DIR}"
)
target_compile_features(TestRange PRIVATE cxx_std_20)
target_link_libraries(TestRange PRIVATE imgui)
