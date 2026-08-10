# Tests for the pieces that are pure logic and can run without the game attached: the config file's
# reader/writer, whose failure mode (silently mangling the player's fcse.ini) is worth catching at
# build time rather than in-game, and the Lua runtime, whose failure mode is worse - a mistake in
# fcse.lua breaks every script at once, and the only place that would otherwise surface is a
# player's log.
#
# Everything else in src/ needs a live Dunia.dll to mean anything, so there is deliberately nothing
# here that stubs the engine out to test around it.

# GoogleTest, vendored the same way MinHook is (see the note in ../CMakeLists.txt) - a pinned tag
# fetched at configure time, so the suite needs no system-wide install to build.
include(FetchContent)
FetchContent_Declare(
  googletest
  GIT_REPOSITORY https://github.com/google/googletest.git
  GIT_TAG        v1.17.0
)

# GMock is not used, and INSTALL_GTEST would add install rules for a dependency that never ships.
set(BUILD_GMOCK OFF CACHE BOOL "" FORCE)
set(INSTALL_GTEST OFF CACHE BOOL "" FORCE)

# gtest_force_shared_crt stays OFF, which is what lets GoogleTest inherit the /MT (/MTd in Debug)
# CMAKE_MSVC_RUNTIME_LIBRARY the root CMakeLists sets before project() - see the CMP0091 note there
# for why the whole build is static-CRT. Turning it ON here would build GoogleTest /MD and the test
# exe would fail to link, loudly, which is the right failure for a mismatch of this kind.
FetchContent_MakeAvailable(googletest)

include(GoogleTest)

# Built against the real src/ini_file.cpp - no stubs, and no dependency on Dunia.dll, so it runs
# anywhere the project builds. The rest of src/ is not linked in because nothing in it is reachable
# without the game.
add_executable(ini_file_tests
  "ini_file_tests.cpp"
  "../src/ini_file.cpp"
  "../src/ini_file.h"
)
target_include_directories(ini_file_tests PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}/../src")
target_link_libraries(ini_file_tests PRIVATE GTest::gtest_main)

if (CMAKE_VERSION VERSION_GREATER 3.12)
  set_property(TARGET ini_file_tests PROPERTY CXX_STANDARD 20)
endif()

# One ctest entry per TEST_F rather than one for the whole exe, so a failure names the case that
# failed in ctest's own output without having to go read the exe's.
gtest_discover_tests(ini_file_tests)

# The address-library decoder, run against the very table assets/address_table.bin ships. The
# encoder is Python in tools/addrlib and the decoder is C++ here, so nothing else in the build would
# notice the two drifting apart - and the failure would not announce itself at runtime either: a
# mis-decoded entry is a perfectly plausible address that happens to be the wrong one.
#
# Needs no Dunia.dll: the tests decode into memory and never dereference what they resolve, so a
# fabricated module base is enough to check the arithmetic. log.cpp is linked in because the decoder
# reports failures through it, and it no-ops safely when Log::Init was never called.
add_executable(address_library_tests
  "address_library_tests.cpp"
  "../src/engine/address_library.cpp"
  "../src/engine/build_id.cpp"
  "../src/log.cpp"
  "../src/caller_identity.cpp"
)
target_include_directories(address_library_tests PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}/../src")
target_link_libraries(address_library_tests PRIVATE GTest::gtest_main)

# ctest runs from the build tree, so the relative paths the test tries first will not resolve;
# this is the one that always does.
target_compile_definitions(address_library_tests PRIVATE
  FCSE_TEST_ADDRESS_TABLE="${FCSE_ADDRLIB}")

if (CMAKE_VERSION VERSION_GREATER 3.12)
  set_property(TARGET address_library_tests PROPERTY CXX_STANDARD 20)
endif()

gtest_discover_tests(address_library_tests)

# Runs the real src/lua/runtime/fcse.lua against a stubbed native table, in the very interpreter
# this build produced - so the dialect, the FFI behaviour and the runtime under test are all the
# ones that will ship, not an approximation of them. Not wrapped in GoogleTest: the thing under test
# is Lua, and a C++ process that only shells out to luajit.exe would add a layer without a check.
#
# Uses luajit.exe out of the LuaJIT build rather than a system Lua: there is no system Lua to rely
# on here, and a different interpreter would not be a meaningful test of a LuaJIT-only runtime.
add_test(NAME lua_runtime_tests
         COMMAND "${LUAJIT_SRC_DIR}/luajit.exe"
                 "${CMAKE_CURRENT_SOURCE_DIR}/lua_runtime_tests.lua"
                 "${FCSE_LUA_RUNTIME}")
