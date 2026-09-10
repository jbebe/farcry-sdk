# Dear ImGui, pinned once for DevTools and for every plugin that draws a window into its overlay -
# both sides share one ImGuiContext, so they have to build the same library. Upstream ships no
# CMakeLists.txt, so MakeAvailable populates and stops, and this is the from-source static library.
include(FetchContent)
FetchContent_Declare(
  imgui
  GIT_REPOSITORY https://github.com/ocornut/imgui.git
  GIT_TAG        v1.91.5
  GIT_SHALLOW    TRUE
)
FetchContent_MakeAvailable(imgui)

add_library(imgui STATIC
  "${imgui_SOURCE_DIR}/imgui.cpp"
  "${imgui_SOURCE_DIR}/imgui_draw.cpp"
  "${imgui_SOURCE_DIR}/imgui_tables.cpp"
  "${imgui_SOURCE_DIR}/imgui_widgets.cpp"
  "${imgui_SOURCE_DIR}/misc/cpp/imgui_stdlib.cpp"
)
target_include_directories(imgui PUBLIC "${imgui_SOURCE_DIR}")
