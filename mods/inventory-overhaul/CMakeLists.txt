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
  "src/items.cpp"
  "src/notebook.cpp"
  "src/page.cpp"
  "src/page_layout.cpp"
  "src/test_window.cpp"
  "src/two_guns.cpp"
  "src/engine/direct3d.cpp"
  "src/engine/economy.cpp"
  "src/engine/paper.cpp"
  "src/engine/game_thread.cpp"
  "src/engine/inventory.cpp"
)

target_include_directories(InventoryOverhaul PRIVATE
  "${CMAKE_CURRENT_SOURCE_DIR}/src"
  "${DEVTOOLS_INCLUDE_DIR}"
  "${CMAKE_CURRENT_SOURCE_DIR}/../../tools/FCSE/include"
)
target_link_libraries(InventoryOverhaul PRIVATE d3d9 gdi32 imgui)
target_compile_features(InventoryOverhaul PRIVATE cxx_std_20)

# A sample Inventory page drawn to a .bmp, to look at the page without the game. Built on request:
# cmake --build --preset x86-release --target PagePreview
add_executable(PagePreview EXCLUDE_FROM_ALL "tools/page_preview.cpp" "src/page_layout.cpp")
target_include_directories(PagePreview PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}/src")
target_link_libraries(PagePreview PRIVATE gdi32)
target_compile_features(PagePreview PRIVATE cxx_std_20)
