# CMakeLists.txt for FCSE (Far Cry Script Extender) - see README.md for the design.
cmake_minimum_required (VERSION 3.10)

if (POLICY CMP0141)
  cmake_policy(SET CMP0141 NEW)
  set(CMAKE_MSVC_DEBUG_INFORMATION_FORMAT "$<IF:$<AND:$<C_COMPILER_ID:MSVC>,$<CXX_COMPILER_ID:MSVC>>,$<$<CONFIG:Debug,RelWithDebInfo>:EditAndContinue>,$<$<CONFIG:Debug,RelWithDebInfo>:ProgramDatabase>>")
endif()

# Static CRT (/MT), not the default /MD - so FCSE.exe and its plugins depend on nothing but
# KERNEL32/USER32 and run on any machine that runs the game.
#
# With /MD the loader imports MSVCP140.dll, VCRUNTIME140.dll and the UCRT stubs, so a player without
# the VS 2015-2022 x86 redistributable gets a missing-DLL box *before any of our code runs* - no
# fcse.log, nothing to diagnose from, and no way for the player to tell it apart from FCSE being
# broken. Verify with `dumpbin /dependents`; see README.md's Verification section.
#
# This is about the C runtime, not about matching the compiler Far Cry 2 itself was built with.
# FCSE shares no C++ objects with Dunia.dll - it calls engine functions through hand-declared
# __thiscall signatures and reads engine structs by byte offset, so the engine's own layout rules
# apply regardless of which compiler built this exe. The plugin ABI (include/plugin_api.h) is pure C
# for the same reason. Building against the game's own 2008-era toolchain would buy none of that and
# cost the C++20 the sources are written in.
#
# Each module getting its own CRT copy is the one constraint /MT adds: memory allocated in one
# module must not be freed in another. Nothing here does that - the plugin API passes const char*
# and callbacks, never ownership.
#
# MUST be set before project(): with CMP0091 OLD, project() bakes /MD straight into
# CMAKE_<LANG>_FLAGS_<CONFIG> and CMAKE_MSVC_RUNTIME_LIBRARY is then ignored entirely - a silent
# no-op that still builds and still links the dynamic CRT.
#
# A build tree configured before this landed keeps that baked-in /MD in its cache and will warn
# `D9025: overriding '/MDd' with '/MTd'` on every file. Harmless - /MT still wins - but delete
# out/build/<preset> once to clear it.
if (POLICY CMP0091)
  cmake_policy(SET CMP0091 NEW)
  set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()

project ("FCSE")

# For the embedded .mgb resources below. project() enables C and CXX only, and a .rc added to a
# target of a project without RC enabled is skipped *silently* - the build would succeed and produce
# an FCSE.exe with no settings-page layout in it. page_assets.cpp's FindResource failure path names
# this line for that reason.
enable_language(RC)

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
# See the note in cmake/luajit.cmake: Populate is deprecated as of CMake 4.x and its warning goes to
# stderr, which is fatal under build.ps1. MinHook ships no CMakeLists.txt either, so MakeAvailable
# populates and stops rather than adding a subdirectory.
FetchContent_MakeAvailable(minhook)

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

# LuaJIT - the interpreter behind the Lua mods in bin\plugins\. Kept in its own module because, unlike MinHook, it
# cannot be expressed as a list of .c files: LuaJIT's build is a bootstrap that generates headers and
# lj_vm.obj with tools it compiles first.
include("${CMAKE_CURRENT_SOURCE_DIR}/cmake/luajit.cmake")

# FCSE's own Magma UI package, embedded into the exe as an RCDATA resource so the loader installs as
# a single file. Both aspect variants: the UI ships as `pc` and `pcwidescreen` sets whose pages
# differ in size and geometry, and page_assets.cpp picks between them at runtime using the engine's
# own widescreen flag.
#
# assets/ holds the XML and nothing else. The binaries are built here, into the build tree, by the
# .mgb encoder in tools/JackAll - the same one that decodes the game's own packages, so this project
# owns no second implementation of the format and there is no committed artifact to go stale against
# the source it was built from.
set(FCSE_MGB_XML            "${CMAKE_CURRENT_SOURCE_DIR}/assets/fcse.mgb.xml")
set(FCSE_MGB_WIDESCREEN_XML "${CMAKE_CURRENT_SOURCE_DIR}/assets/fcse_widescreen.mgb.xml")
set(FCSE_MGB            "${CMAKE_CURRENT_BINARY_DIR}/assets/fcse.mgb")
set(FCSE_MGB_WIDESCREEN "${CMAKE_CURRENT_BINARY_DIR}/assets/fcse_widescreen.mgb")

find_program(DOTNET_EXECUTABLE NAMES dotnet)
if (NOT DOTNET_EXECUTABLE)
  message(FATAL_ERROR "dotnet was not found on PATH. FCSE's settings-page layout is built from "
                      "assets/*.mgb.xml by tools/JackAll's CLI, which needs the .NET SDK "
                      "(https://dotnet.microsoft.com/download). Install it and re-configure.")
endif()

# `dotnet run` builds the CLI if it has to and forwards the app's exit code, so a failed encode
# fails the build. Release rather than the default Debug: this is a tool being used, not debugged,
# and it keeps the run out of whatever configuration a JackAll developer has in their tree.
set(JACKALL_CLI "${DOTNET_EXECUTABLE}" run
                --project "${CMAKE_CURRENT_SOURCE_DIR}/../JackAll/src/JackAll.Cli/JackAll.Cli.csproj"
                --configuration Release --verbosity quiet --)

# Verify before encoding, and as part of producing the binary rather than as a test: `mgb encode`
# proves the bytes are loadable, but a link naming an element that does not exist is a perfectly
# valid package that gives FCSE a page with no controls on it, and that failure should stop a build
# rather than wait for someone to run the suite. --page is the name fcse_page.cpp's kPageName asks
# GenericObjectServer::FindGenericObject for; without a registry entry under it the page is
# authored, laid out, and unreachable.
#
# DEPENDS names the XML and not the encoder that reads it: `mgb encode` is byte-deterministic for a
# given document (JackAll's own round-trip tests are what hold that), so re-encoding because JackAll
# changed would rewrite identical bytes. If it ever isn't, delete the build tree.
function(fcse_build_mgb xml mgb)
  get_filename_component(name "${mgb}" NAME)
  add_custom_command(
    OUTPUT "${mgb}"
    COMMAND ${JACKALL_CLI} mgb verify "${xml}" --page FCSE_PAGE
    COMMAND ${JACKALL_CLI} mgb encode "${xml}" -o "${mgb}"
    DEPENDS "${xml}"
    COMMENT "Building ${name} from ${xml}"
    VERBATIM)
endfunction()

fcse_build_mgb("${FCSE_MGB_XML}"            "${FCSE_MGB}")
fcse_build_mgb("${FCSE_MGB_WIDESCREEN_XML}" "${FCSE_MGB_WIDESCREEN}")

# The .rc's OBJECT_DEPENDS below is what actually re-runs these when an XML changes; the target is
# what orders them ahead of the link under generators that don't infer it from that dependency.
add_custom_target(fcse_mgb DEPENDS "${FCSE_MGB}" "${FCSE_MGB_WIDESCREEN}")

# The Lua script API, embedded alongside them - see the note in assets/fcse.rc.in.
set(FCSE_LUA_RUNTIME    "${CMAKE_CURRENT_SOURCE_DIR}/src/lua/runtime/fcse.lua")

configure_file("assets/fcse.rc.in" "${CMAKE_CURRENT_BINARY_DIR}/fcse.rc" @ONLY)

# rc.exe does not report what a resource statement pulled in and CMake does not scan .rc files for
# RCDATA dependencies, so without this, editing an .mgb.xml would relink the previous build's bytes
# and silently ship stale layout data. (configure_file already re-runs CMake when the .rc.in template
# changes - it says nothing about the files that template names.) Same staleness class as the
# POST_BUILD problem the staging rule this replaced was written to avoid.
#
# It is also what pulls the two layouts into the build at all: they are custom-command outputs, so
# naming them here is what makes the resource compile wait for - and re-run after - fcse_build_mgb.
set_property(SOURCE "${CMAKE_CURRENT_BINARY_DIR}/fcse.rc"
             APPEND PROPERTY OBJECT_DEPENDS "${FCSE_MGB}" "${FCSE_MGB_WIDESCREEN}"
                                            "${FCSE_LUA_RUNTIME}")

add_executable(FCSE WIN32
  "${CMAKE_CURRENT_BINARY_DIR}/fcse.rc"
  "src/main.cpp"
  "src/log.cpp"
  "src/log.h"
  "src/caller_identity.cpp"
  "src/caller_identity.h"
  "src/crash_log.cpp"
  "src/crash_log.h"
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
  "src/ini_file.cpp"
  "src/ini_file.h"
  "src/fcse_page.cpp"
  "src/fcse_page.h"
  "src/magma_package.cpp"
  "src/magma_package.h"
  "src/mods_tab.cpp"
  "src/mods_tab.h"
  "src/page_assets.cpp"
  "src/page_assets.h"
  "src/patch.cpp"
  "src/patch.h"
  "src/plugin_loader.cpp"
  "src/plugin_loader.h"
  "src/settings_registry.cpp"
  "src/settings_registry.h"
  "src/lua/lua_api.cpp"
  "src/lua/lua_api.h"
  "src/lua/lua_host.cpp"
  "src/lua/lua_host.h"
  "src/lua/tick_source.cpp"
  "src/lua/tick_source.h"
  "src/lua/runtime/fcse.lua"
  "include/plugin_api.h"
)

# Listed in the target so it shows up in an IDE next to the code that embeds it, but it is data for
# rc.exe, not something to hand the C++ compiler.
set_source_files_properties("${FCSE_LUA_RUNTIME}" PROPERTIES HEADER_FILE_ONLY TRUE)

target_link_libraries(FCSE PRIVATE minhook luajit)

# Ordering only - OBJECT_DEPENDS above already handles staleness. Without this the resource compile
# and the .mgb encode are two independent leaves of the graph under generators that do not derive an
# order from a source-level dependency on a generated file.
add_dependencies(FCSE fcse_mgb)

# luajit is an IMPORTED target, so linking it creates no build-order edge on the command that
# produces lua51.lib - CMake would happily start the link before LuaJIT finished building. This is
# what actually sequences the two.
add_dependencies(FCSE luajit_build)

set_target_properties(FCSE PROPERTIES OUTPUT_NAME "FCSE")

# The whole point of this loader per docs/docs/todos.md's "Tools/dll plugins" entry: enough
# simultaneously-loaded plugins can otherwise run a 32-bit process out of address space.
target_link_options(FCSE PRIVATE "/LARGEADDRESSAWARE")

if (CMAKE_VERSION VERSION_GREATER 3.12)
  set_property(TARGET FCSE PROPERTY CXX_STANDARD 20)
endif()

add_subdirectory(example_plugin)

# Tests for the pieces that are pure logic and can run without the game attached: the config file's
# reader/writer, whose failure mode (silently mangling the player's fcse.ini) is worth catching at
# build time rather than in-game, and the Lua runtime, whose failure mode is worse - a mistake in
# fcse.lua breaks every script at once, and the only place that would otherwise surface is a
# player's log. Plain console exe / plain script, no test framework.
enable_testing()

add_executable(ini_file_tests
  "tests/ini_file_tests.cpp"
  "src/ini_file.cpp"
  "src/ini_file.h"
)
target_include_directories(ini_file_tests PRIVATE "src")

if (CMAKE_VERSION VERSION_GREATER 3.12)
  set_property(TARGET ini_file_tests PROPERTY CXX_STANDARD 20)
endif()

add_test(NAME ini_file_tests COMMAND ini_file_tests)

# Runs the real src/lua/runtime/fcse.lua against a stubbed native table, in the very interpreter this
# build produced - so the dialect, the FFI behaviour and the runtime under test are all the ones that
# will ship, not an approximation of them.
#
# Uses luajit.exe out of the LuaJIT build rather than a system Lua: there is no system Lua to rely on
# here, and a different interpreter would not be a meaningful test of a LuaJIT-only runtime.
add_test(NAME lua_runtime_tests
         COMMAND "${LUAJIT_SRC_DIR}/luajit.exe"
                 "${CMAKE_CURRENT_SOURCE_DIR}/tests/lua_runtime_tests.lua"
                 "${FCSE_LUA_RUNTIME}")
