# CMakeLists.txt for Inventory Overhaul - an FCSE plugin. See README.md.
cmake_minimum_required (VERSION 3.21)

# Static CRT (/MT), matching FCSE.exe.
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

project ("InventoryOverhaul")

if (NOT CMAKE_SIZEOF_VOID_P EQUAL 4)
  message(FATAL_ERROR "Inventory Overhaul must be built 32-bit (x86) - Far Cry 2 is a 32-bit process. "
                      "Select the x86-debug/x86-release preset.")
endif()

add_library(InventoryOverhaul SHARED
  "src/main.cpp"
  "src/two_guns.cpp"
  "src/engine/inventory.cpp"
)

target_include_directories(InventoryOverhaul PRIVATE
  "${CMAKE_CURRENT_SOURCE_DIR}/src"
  "${CMAKE_CURRENT_SOURCE_DIR}/../../tools/FCSE/include"
)
target_compile_features(InventoryOverhaul PRIVATE cxx_std_20)
