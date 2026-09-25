# CMakeLists.txt for Sound Overhaul - an FCSE plugin. See README.md.
cmake_minimum_required (VERSION 3.10)

# Static CRT (/MT), matching FCSE.exe, so players need no Visual C++ redistributable. Must be set
# before project(): with CMP0091 OLD, project() bakes /MD in and this is silently ignored.
if (POLICY CMP0091)
  cmake_policy(SET CMP0091 NEW)
  set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()

project ("SoundOverhaul")

if (NOT CMAKE_SIZEOF_VOID_P EQUAL 4)
  message(FATAL_ERROR "Sound Overhaul must be built 32-bit (x86) - Far Cry 2 is a 32-bit process. "
                      "Select the x86-debug/x86-release preset.")
endif()

set(FCSE_INCLUDE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../../tools/FCSE/include")
if (NOT EXISTS "${FCSE_INCLUDE_DIR}/fcse_api.h")
  message(FATAL_ERROR "fcse_api.h was not found at ${FCSE_INCLUDE_DIR}.")
endif()

add_library(SoundOverhaul SHARED
  "src/main.cpp"
  "src/dsoal.cpp"
  "src/enumeration_fix.cpp"
)

target_include_directories(SoundOverhaul PRIVATE
  "${CMAKE_CURRENT_SOURCE_DIR}/src"
  "${FCSE_INCLUDE_DIR}"
)

set_target_properties(SoundOverhaul PROPERTIES OUTPUT_NAME "SoundOverhaul" PREFIX "")

if (CMAKE_VERSION VERSION_GREATER 3.12)
  set_property(TARGET SoundOverhaul PROPERTY CXX_STANDARD 20)
endif()
