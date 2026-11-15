# Tests for the pieces that are pure logic and can run without the game attached: the config file's
# reader/writer, whose failure mode (silently mangling the player's fcse.ini) is worth catching at
# build time rather than in-game, and the Lua runtime, whose failure mode is worse - a mistake in
# fcse.lua breaks every script at once, and the only place that would otherwise surface is a
# player's log.
#
# Everything else in src/ needs a live Dunia.dll to mean anything, so there is deliberately nothing
# here that stubs the engine out to test around it.

# GoogleTest, pinned and fetched at configure time by the setup plugins share, so the suite needs no
# system-wide install to build.
include("${CMAKE_CURRENT_SOURCE_DIR}/../include/fcse_tests.cmake")

# Built against the real code through fcse_core - no stubs, and no dependency on Dunia.dll, so it
# runs anywhere the project builds. The rest of src/ is not linked in because nothing in it is
# reachable without the game.
fcse_add_tests(ini_file_tests ini_file_tests.cpp)
target_link_libraries(ini_file_tests PRIVATE fcse_core)

# The address-library decoder, run against the very table assets/address_table.bin ships. The
# encoder is Python in tools/addrlib and the decoder is C++ here, so nothing else in the build would
# notice the two drifting apart - and the failure would not announce itself at runtime either: a
# mis-decoded entry is a perfectly plausible address that happens to be the wrong one.
#
# Needs no Dunia.dll: the tests decode into memory and never dereference what they resolve, so a
# fabricated module base is enough to check the arithmetic. log.cpp is linked in because the decoder
# reports failures through it, and it no-ops safely when Log::Init was never called.
fcse_add_tests(address_library_tests address_library_tests.cpp)
target_link_libraries(address_library_tests PRIVATE fcse_core)

# ctest runs from the build tree, so the relative paths the test tries first will not resolve;
# this is the one that always does.
target_compile_definitions(address_library_tests PRIVATE
  FCSE_TEST_ADDRESS_TABLE="${FCSE_ADDRLIB}")

# The byte-pattern compiler and search. Pure logic over buffers, so no game is needed - and worth
# testing precisely because both halves fail quietly: a compiler that reads "??" as two wildcards
# shifts every byte after it, and a search that mis-anchors reports matches at the wrong offset.
# Neither shows up as a crash anywhere near the mistake.
fcse_add_tests(pattern_scan_tests pattern_scan_tests.cpp)
target_link_libraries(pattern_scan_tests PRIVATE fcse_core)

# The settings store: declaration validation, the file<->value parse and the change path. Worth
# testing for the same reason ini_file is - it owns the player's config, and both a value that
# stops round-tripping and a rejected value that gets stored anyway fail silently.
fcse_add_tests(settings_registry_tests settings_registry_tests.cpp)
target_link_libraries(settings_registry_tests PRIVATE fcse_core)

# The settings page's scroll window - the only part of that page that means anything without a
# running game, and the part that decides which plugin's row a control ends up bound to.
fcse_add_tests(page_window_tests page_window_tests.cpp)
target_link_libraries(page_window_tests PRIVATE fcse_core)

# The hook manager, with real safetyhook hooks on code the test assembles in its own process. A
# wrong claim rule or teardown order otherwise only shows up as a crash in someone's game.
fcse_add_tests(hook_tests hook_tests.cpp)
target_link_libraries(hook_tests PRIVATE fcse_core)

# The entity-data store and the CRC-32 its keys and class id are hashed with. A hash that drifts
# from the engine's names nothing the engine can find, and a layering mistake freezes data edits
# into old saves - both silent until someone notices in game.
fcse_add_tests(entity_data_tests entity_data_tests.cpp)
target_link_libraries(entity_data_tests PRIVATE fcse_core)

# The channels plugins send to each other on, under the threading the API promises.
fcse_add_tests(messaging_tests messaging_tests.cpp)
target_link_libraries(messaging_tests PRIVATE fcse_core)

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
