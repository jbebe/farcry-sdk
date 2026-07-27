# CMakeLists.txt for FCSE (Far Cry Script Extender) - see README.md for the design.
cmake_minimum_required (VERSION 3.10)

if (POLICY CMP0141)
  cmake_policy(SET CMP0141 NEW)
  set(CMAKE_MSVC_DEBUG_INFORMATION_FORMAT "$<IF:$<AND:$<C_COMPILER_ID:MSVC>,$<CXX_COMPILER_ID:MSVC>>,$<$<CONFIG:Debug,RelWithDebInfo>:EditAndContinue>,$<$<CONFIG:Debug,RelWithDebInfo>:ProgramDatabase>>")
endif()

project ("FCSE")

if (NOT CMAKE_SIZEOF_VOID_P EQUAL 4)
  message(FATAL_ERROR "FCSE must be built 32-bit (x86) - Far Cry 2 is a 32-bit process and a "
                       "64-bit FCSE.exe cannot load Dunia.dll or run plugin DLLs built for it. "
                       "Select the x86-debug/x86-release preset.")
endif()

include(FetchContent)
FetchContent_Declare(
  minhook
  GIT_REPOSITORY https://github.com/TsudaKageyu/minhook.git
  GIT_TAG        v1.3.3
)
FetchContent_Populate(minhook)

# Upstream MinHook ships only Visual Studio project files, no CMake support - same minimal
# from-source static-library target as tools/misc/modpatcher/CMakeLists.txt.
add_library(minhook STATIC
  "${minhook_SOURCE_DIR}/src/buffer.c"
  "${minhook_SOURCE_DIR}/src/hook.c"
  "${minhook_SOURCE_DIR}/src/trampoline.c"
  "${minhook_SOURCE_DIR}/src/hde/hde32.c"
)
target_include_directories(minhook PUBLIC "${minhook_SOURCE_DIR}/include")
target_include_directories(minhook PRIVATE "${minhook_SOURCE_DIR}/src")

add_executable(FCSE WIN32
  "src/main.cpp"
  "src/log.cpp"
  "src/log.h"
  "src/caller_identity.cpp"
  "src/caller_identity.h"
  "src/dunia_api.cpp"
  "src/dunia_api.h"
  "src/stock_constants.cpp"
  "src/stock_constants.h"
  "src/debug_commands.cpp"
  "src/debug_commands.h"
  "src/function_registry.cpp"
  "src/function_registry.h"
  "src/hook.cpp"
  "src/hook.h"
  "src/patch.cpp"
  "src/patch.h"
  "src/plugin_loader.cpp"
  "src/plugin_loader.h"
  "include/plugin_api.h"
)

target_link_libraries(FCSE PRIVATE minhook)

set_target_properties(FCSE PROPERTIES OUTPUT_NAME "FCSE")

# The whole point of this loader per docs/docs/todos.md's "Tools/dll plugins" entry: enough
# simultaneously-loaded plugins can otherwise run a 32-bit process out of address space.
target_link_options(FCSE PRIVATE "/LARGEADDRESSAWARE")

if (CMAKE_VERSION VERSION_GREATER 3.12)
  set_property(TARGET FCSE PROPERTY CXX_STANDARD 20)
endif()

add_subdirectory(example_plugin)
add_subdirectory(conflict_plugin)
