# CMakeLists.txt for Sound Overhaul - an FCSE plugin. See README.md.
cmake_minimum_required (VERSION 3.21)

# Static CRT (/MT), matching FCSE.exe; must be set before project() to take effect.
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

project ("SoundOverhaul")

if (NOT CMAKE_SIZEOF_VOID_P EQUAL 4)
  message(FATAL_ERROR "Sound Overhaul must be built 32-bit (x86) - Far Cry 2 is a 32-bit process. "
                      "Select the x86-debug/x86-release preset.")
endif()

add_library(SoundOverhaul SHARED
  "src/main.cpp"
  "src/dsoal.cpp"
  "src/enumeration_fix.cpp"
  "src/reverb.cpp"
  "src/reverb_log.cpp"
)

target_include_directories(SoundOverhaul PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}/../../tools/FCSE/include")
target_compile_features(SoundOverhaul PRIVATE cxx_std_20)
