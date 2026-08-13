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
# apply regardless of which compiler built this exe. The plugin ABI (include/fcse_api.h) is pure C
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
#
# `--unset=Platform` because build.ps1 drives this whole build from a vcvarsall x86 developer
# environment, and vcvarsall exports `Platform=x86`. MSBuild reads Platform off the environment as a
# project property, so without the unset the C++ target architecture leaks into the C# build:
# JackAll.Cli builds into bin\x86\ with a 32-bit apphost, which `dotnet run` then launches as a
# 32-bit process. That process resolves its runtime through DOTNET_ROOT, so wherever DOTNET_ROOT
# names the x64 install - which is exactly what actions/setup-dotnet does in CI - it loads an x64
# hostfxr.dll and dies with HRESULT 0x800700C1. It only ever worked because a developer machine
# tends to also have the x86 .NET runtime installed for it to fall back to, which is what made this
# a CI-only failure. The encoder is a build tool that runs on the host, so it should be built for
# the host regardless.
set(JACKALL_CLI "${CMAKE_COMMAND}" -E env --unset=Platform
                "${DOTNET_EXECUTABLE}" run
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
#
# Both layouts come out of one custom command rather than one each, because the encoder is invoked as
# `dotnet run`: two independent edges are exactly what a parallel generator runs at the same time,
# and two concurrent `dotnet run` on the same project both build JackAll.Cli into the same obj\
# folder - one of them then loses the race with `CSC : error CS2012: Cannot open ...
# JackAll.Core.dll for writing`. It only shows up on a cold build tree, since an incremental one has
# nothing left to encode, which makes it a CI-only failure in practice. Keeping the invocations on a
# single edge also means the CLI is built once and every command after the first reuses it.
add_custom_command(
  OUTPUT "${FCSE_MGB}" "${FCSE_MGB_WIDESCREEN}"
  COMMAND ${JACKALL_CLI} mgb verify "${FCSE_MGB_XML}" --page FCSE_PAGE
  COMMAND ${JACKALL_CLI} mgb encode "${FCSE_MGB_XML}" -o "${FCSE_MGB}"
  COMMAND ${JACKALL_CLI} mgb verify "${FCSE_MGB_WIDESCREEN_XML}" --page FCSE_PAGE
  COMMAND ${JACKALL_CLI} mgb encode "${FCSE_MGB_WIDESCREEN_XML}" -o "${FCSE_MGB_WIDESCREEN}"
  DEPENDS "${FCSE_MGB_XML}" "${FCSE_MGB_WIDESCREEN_XML}"
  COMMENT "Building fcse.mgb and fcse_widescreen.mgb from assets/*.mgb.xml"
  VERBATIM)

# The .rc's OBJECT_DEPENDS below is what actually re-runs these when an XML changes; the target is
# what orders them ahead of the link under generators that don't infer it from that dependency.
add_custom_target(fcse_mgb DEPENDS "${FCSE_MGB}" "${FCSE_MGB_WIDESCREEN}")

# The Lua script API, embedded alongside them - see the note in assets/fcse.rc.in.
set(FCSE_LUA_RUNTIME    "${CMAKE_CURRENT_SOURCE_DIR}/src/lua/runtime/fcse.lua")

# The application icon. Committed rather than built from assets/logo.png the way the layouts above
# are built from their XML: resampling an image needs a library the toolchain does not already
# carry, and making that a prerequisite for compiling a mod loader costs more than a 15 KB artifact
# in the tree. assets/make_icon.py regenerates it when the logo changes.
set(FCSE_ICON           "${CMAKE_CURRENT_SOURCE_DIR}/assets/fcse.ico")

# The address library. Committed rather than built, for the same reason as the icon and unlike the
# layouts: regenerating it needs Ghidra plus a copy of both Dunia.dll builds, which is not something
# a build of this project can require. tools/FCSE/tools/addrlib rebuilds it when the mapping changes.
set(FCSE_ADDRLIB        "${CMAKE_CURRENT_SOURCE_DIR}/assets/address_table.bin")
if (NOT EXISTS "${FCSE_ADDRLIB}")
  message(FATAL_ERROR "assets/address_table.bin is missing. It maps each address ID to its RVA in "
                      "each supported Dunia.dll build, and without it FCSE cannot resolve a single "
                      "engine address. Regenerate it with tools/FCSE/tools/addrlib "
                      "(python build_addrlib.py) - see that directory's README.md.")
endif()

configure_file("assets/fcse.rc.in" "${CMAKE_CURRENT_BINARY_DIR}/fcse.rc" @ONLY)

# rc.exe does not report what a resource statement pulled in and CMake does not scan .rc files for
# the files they name, so without this, editing an .mgb.xml would relink the previous build's bytes
# and silently ship stale layout data. (configure_file already re-runs CMake when the .rc.in template
# changes - it says nothing about the files that template names.) Same staleness class as the
# POST_BUILD problem the staging rule this replaced was written to avoid.
#
# It is also what pulls the two layouts into the build at all: they are custom-command outputs, so
# naming them here is what makes the resource compile wait for - and re-run after - fcse_build_mgb.
set_property(SOURCE "${CMAKE_CURRENT_BINARY_DIR}/fcse.rc"
             APPEND PROPERTY OBJECT_DEPENDS "${FCSE_MGB}" "${FCSE_MGB_WIDESCREEN}"
                                            "${FCSE_LUA_RUNTIME}" "${FCSE_ICON}"
                                            "${FCSE_ADDRLIB}")

# The sources that mean something without a live Dunia.dll: config file, address-library decode,
# build identification, logging, pattern matching, settings. Split out as a library so the test
# targets link the same objects the loader does instead of recompiling them from ../src.
add_library(fcse_core STATIC
  "src/log.cpp"
  "src/log.h"
  "src/caller_identity.cpp"
  "src/caller_identity.h"
  "src/ini_file.cpp"
  "src/ini_file.h"
  "src/loader_paths.cpp"
  "src/loader_paths.h"
  "src/engine/build_id.cpp"
  "src/engine/build_id.h"
  "src/engine/address_library.cpp"
  "src/engine/address_library.h"
  "src/engine/address_symbols.h"
  "src/engine/dunia_api.cpp"
  "src/engine/dunia_api.h"
  "src/api/pattern_scan.cpp"
  "src/api/pattern_scan.h"
  "src/api/settings_registry.cpp"
  "src/api/settings_registry.h"
  "src/util/dir_walk.cpp"
  "src/util/dir_walk.h"
  "src/util/member_fn.h"
  "src/util/pe_image.cpp"
  "src/util/pe_image.h"
  "src/util/resource.cpp"
  "src/util/resource.h"
  "src/util/seh.h"
  "src/util/win_string.cpp"
  "src/util/win_string.h"
)

target_include_directories(fcse_core PUBLIC
  "${CMAKE_CURRENT_SOURCE_DIR}/src"
  "${CMAKE_CURRENT_SOURCE_DIR}/include"
)

if (CMAKE_VERSION VERSION_GREATER 3.12)
  set_property(TARGET fcse_core PROPERTY CXX_STANDARD 20)
endif()

# Grouped by what a file talks to: engine/ reaches into the running game, api/ is what plugins and
# Lua scripts both call, ui/ is FCSE's own settings page, lua/ is the script host. What is left at
# the top of src/ is the loader entry point and the utilities every group uses.
add_executable(FCSE WIN32
  "${CMAKE_CURRENT_BINARY_DIR}/fcse.rc"
  "src/main.cpp"
  "src/crash_log.cpp"
  "src/crash_log.h"
  "src/engine/stock_constants.cpp"
  "src/engine/stock_constants.h"
  "src/engine/debug_commands.cpp"
  "src/engine/debug_commands.h"
  "src/engine/splash.cpp"
  "src/engine/splash.h"
  "src/api/plugin_api.cpp"
  "src/api/plugin_api.h"
  "src/api/plugin_loader.cpp"
  "src/api/plugin_loader.h"
  "src/api/function_registry.cpp"
  "src/api/function_registry.h"
  "src/api/hook.cpp"
  "src/api/hook.h"
  "src/api/patch.cpp"
  "src/api/patch.h"
  "src/ui/engine_page_abi.h"
  "src/ui/fcse_page.cpp"
  "src/ui/fcse_page.h"
  "src/ui/magma_package.cpp"
  "src/ui/magma_package.h"
  "src/ui/menu_item_handler.h"
  "src/ui/page_internal.h"
  "src/ui/page_rows.cpp"
  "src/ui/page_slots.cpp"
  "src/ui/page_vtable.cpp"
  "src/ui/mods_tab.cpp"
  "src/ui/mods_tab.h"
  "src/ui/page_assets.cpp"
  "src/ui/page_assets.h"
  "src/lua/lua_api.cpp"
  "src/lua/lua_api.h"
  "src/lua/lua_host.cpp"
  "src/lua/lua_host.h"
  "src/lua/tick_source.cpp"
  "src/lua/tick_source.h"
  "src/lua/runtime/fcse.lua"
  "include/fcse_api.h"
)

# Both source roots on the include path, so every #include is written from the root of the tree it
# lives in - "engine/hook.h", never "../engine/hook.h" - and a file moving between folders doesn't
# rewrite the includes of everything that reaches it. include/ is here for the same reason a plugin
# gets it: fcse_api.h is spelled "fcse_api.h" from inside FCSE exactly as it is from outside.
target_include_directories(FCSE PRIVATE
  "${CMAKE_CURRENT_SOURCE_DIR}/src"
  "${CMAKE_CURRENT_SOURCE_DIR}/include"
)

# Listed in the target so it shows up in an IDE next to the code that embeds it, but it is data for
# rc.exe, not something to hand the C++ compiler.
set_source_files_properties("${FCSE_LUA_RUNTIME}" PROPERTIES HEADER_FILE_ONLY TRUE)

target_link_libraries(FCSE PRIVATE fcse_core minhook luajit)

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

# Tests for the pieces that are pure logic and can run without the game attached - see
# tests/CMakeLists.txt for what is covered and what each suite is built out of. enable_testing() has
# to be called from here rather than from there: ctest looks for its test list in the top of the
# build tree, which is this directory's.
enable_testing()
add_subdirectory(tests)
