# CMakeLists.txt for Inventory Overhaul - an FCSE plugin. See README.md.
cmake_minimum_required (VERSION 3.21)

# Static CRT (/MT), matching FCSE.exe.
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

project ("InventoryOverhaul")

if (NOT CMAKE_SIZEOF_VOID_P EQUAL 4)
  message(FATAL_ERROR "Inventory Overhaul must be built 32-bit (x86) - Far Cry 2 is a 32-bit process. "
                      "Select the x86-debug/x86-release preset.")
endif()

# DevTools' overlay contract and the Dear ImGui it pins, for the test window; without DevTools
# installed the window is simply not there.
set(DEVTOOLS_INCLUDE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../DevTools/include")
include("${DEVTOOLS_INCLUDE_DIR}/devtools_imgui.cmake")

add_library(InventoryOverhaul SHARED
  "src/main.cpp"
  "src/contacts_page.cpp"
  "src/inventory_page.cpp"
  "src/items.cpp"
  "src/journal_page.cpp"
  "src/notebook.cpp"
  "src/page.cpp"
  "src/papers_page.cpp"
  "src/test_window.cpp"
  "src/two_guns.cpp"
  "src/engine/economy.cpp"
  "src/engine/game_thread.cpp"
  "src/engine/inventory.cpp"
  "src/engine/magma.cpp"
  "src/engine/story.cpp"
)

target_include_directories(InventoryOverhaul PRIVATE
  "${CMAKE_CURRENT_SOURCE_DIR}/src"
  "${DEVTOOLS_INCLUDE_DIR}"
  "${CMAKE_CURRENT_SOURCE_DIR}/../../tools/FCSE/include"
)
target_link_libraries(InventoryOverhaul PRIVATE imgui)
target_compile_features(InventoryOverhaul PRIVATE cxx_std_20)
