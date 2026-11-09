# Unit tests for a plugin's pure logic. Include this, then
#   fcse_add_tests(<name> tests/<file>_tests.cpp ... SOURCES src/<pure file>.cpp ...)
# A test links only the sources it names, so code reaching into Dunia.dll fails to link rather than
# being stubbed. FCSE's own tests use the same GoogleTest; the plugin's build.ps1 -Tests runs ctest.
include_guard(GLOBAL)

include(FetchContent)
FetchContent_Declare(
  googletest
  GIT_REPOSITORY https://github.com/google/googletest.git
  GIT_TAG        v1.17.0
  GIT_SHALLOW    TRUE
)
set(BUILD_GMOCK OFF CACHE BOOL "" FORCE)
set(INSTALL_GTEST OFF CACHE BOOL "" FORCE)
# gtest_force_shared_crt stays OFF, so GoogleTest inherits the static CRT FCSE and every plugin build with.
FetchContent_MakeAvailable(googletest)
include(GoogleTest)
enable_testing()

function(fcse_add_tests name)
  cmake_parse_arguments(TEST "" "" "SOURCES" ${ARGN})
  add_executable(${name} ${TEST_UNPARSED_ARGUMENTS} ${TEST_SOURCES})
  target_link_libraries(${name} PRIVATE GTest::gtest_main)
  target_include_directories(${name} PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}/src")
  set_property(TARGET ${name} PROPERTY CXX_STANDARD 20)
  gtest_discover_tests(${name})
endfunction()
