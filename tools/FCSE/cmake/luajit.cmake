# LuaJIT, built from source as a static x86 library and linked into FCSE.exe.
#
# Why LuaJIT and not PUC Lua: the FFI. It lets a script lay a struct over engine memory and call a
# Dunia function by address - `ffi.cast("void(__thiscall*)(void*,int)", addr)` - with no C++ and no
# FCSE release in between. With stock Lua every new address would need a binding compiled and
# shipped here first, which is the exact bottleneck Lua support exists to remove. The 5.1 dialect it
# implements is also what every comparable modding framework speaks.
#
# Pinned to a commit, not a tag: LuaJIT ships rolling releases off the v2.1 branch and has no
# release tags to track. Bump this deliberately.
include(FetchContent)
FetchContent_Declare(
  luajit
  GIT_REPOSITORY https://github.com/LuaJIT/LuaJIT.git
  GIT_TAG        1edc3e52b67eaf6ce5f809be8e17d6862594b8bc # v2.1 branch, LuaJIT 2.1.1785763465
)
# MakeAvailable, not Populate: Populate is deprecated as of CMake 4.x (CMP0169) and emits a warning
# on stderr, which build.ps1 - running under $ErrorActionPreference = "Stop" - turns into a hard
# failure. MakeAvailable is a straight substitute here because LuaJIT ships no CMakeLists.txt, so
# there is no subdirectory for it to add: it populates and stops, exactly like Populate did.
FetchContent_MakeAvailable(luajit)

set(LUAJIT_SRC_DIR "${luajit_SOURCE_DIR}/src")
set(LUAJIT_LIBRARY "${LUAJIT_SRC_DIR}/lua51.lib")

# Built in LuaJIT's own source tree rather than the CMake build tree because msvcbuild.bat writes
# its intermediates and its output there and takes no output path. The tree is FetchContent-owned
# and disposable, so that is contained - deleting out/ or the _deps folder resets it.
#
# The OUTPUT is lua51.lib alone, with no DEPENDS on LuaJIT's sources: this is a pinned dependency at
# a fixed commit, so the only thing that invalidates the library is the library going missing. That
# does mean changing GIT_TAG above needs a clean _deps folder (or a deleted lua51.lib) to take
# effect - the same one-time reset the CMP0091 note in CMakeLists.txt already calls for.
add_custom_command(
  OUTPUT "${LUAJIT_LIBRARY}"
  COMMAND "${CMAKE_CURRENT_SOURCE_DIR}/cmake/build_luajit.cmd" "${LUAJIT_SRC_DIR}"
  WORKING_DIRECTORY "${LUAJIT_SRC_DIR}"
  COMMENT "Building LuaJIT (x86, static, /MT)"
  VERBATIM
)

add_custom_target(luajit_build DEPENDS "${LUAJIT_LIBRARY}")

add_library(luajit STATIC IMPORTED GLOBAL)
set_target_properties(luajit PROPERTIES
  IMPORTED_LOCATION "${LUAJIT_LIBRARY}"
  INTERFACE_INCLUDE_DIRECTORIES "${LUAJIT_SRC_DIR}"
)

# In Debug, FCSE compiles /MTd while lua51.lib is always /MT, so the link warns LNK4098 (LIBCMT vs
# LIBCMTD). It is benign here and deliberately left visible rather than suppressed: LuaJIT allocates
# through its own lj_alloc (VirtualAlloc-backed), not CRT malloc, so no Lua-owned memory is ever
# freed by the other CRT - the same "never free across modules" rule plugin_api.h already documents
# for /MT plugins. Both configurations were verified to build, run a chunk and close the state.
#
# The tempting fix, /NODEFAULTLIB:LIBCMT, is the wrong one: it would bind objects compiled against
# the release CRT's headers to the debug CRT's implementation, and lib_io.c passes FILE* across that
# boundary. Making Debug genuinely single-CRT means building LuaJIT per configuration, which
# msvcbuild.bat's static path cannot express - it would take replacing this wrapper with a native
# CMake target that runs the minilua/buildvm bootstrap itself.
