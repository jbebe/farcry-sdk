# CMakeList.txt : CMake project for ModPatcher, include source and define
# project specific logic here.
#
cmake_minimum_required (VERSION 3.10)

# Enable Hot Reload for MSVC compilers if supported.
if (POLICY CMP0141)
  cmake_policy(SET CMP0141 NEW)
  set(CMAKE_MSVC_DEBUG_INFORMATION_FORMAT "$<IF:$<AND:$<C_COMPILER_ID:MSVC>,$<CXX_COMPILER_ID:MSVC>>,$<$<CONFIG:Debug,RelWithDebInfo>:EditAndContinue>,$<$<CONFIG:Debug,RelWithDebInfo>:ProgramDatabase>>")
endif()

project ("ModPatcher")

if (NOT CMAKE_SIZEOF_VOID_P EQUAL 4)
  message(FATAL_ERROR "ModPatcher must be built 32-bit (x86) - it proxies a DLL loaded into "
                       "Far Cry 2's 32-bit process. Select the x86-debug/x86-release preset.")
endif()

include(FetchContent)
FetchContent_Declare(
  minhook
  GIT_REPOSITORY https://github.com/TsudaKageyu/minhook.git
  GIT_TAG        v1.3.3
)
FetchContent_Populate(minhook)

# The upstream MinHook repo ships only Visual Studio project files (build/VC*/*.vcxproj), no
# CMakeLists.txt - define our own minimal static-library target over the fetched sources rather
# than depending on an unofficial CMake-ified fork.
add_library(minhook STATIC
  "${minhook_SOURCE_DIR}/src/buffer.c"
  "${minhook_SOURCE_DIR}/src/hook.c"
  "${minhook_SOURCE_DIR}/src/trampoline.c"
  "${minhook_SOURCE_DIR}/src/hde/hde32.c"
)
target_include_directories(minhook PUBLIC "${minhook_SOURCE_DIR}/include")
target_include_directories(minhook PRIVATE "${minhook_SOURCE_DIR}/src")

# Add source to this project's DLL.
add_library (ModPatcher SHARED
  "src/dllmain.cpp"
  "src/dunia.cpp"
  "src/dunia.h"
  "src/hook_vfs.cpp"
  "src/hook_vfs.h"
  "src/hook_findandopen.cpp"
  "src/hook_findandopen.h"
  "src/log.cpp"
  "src/log.h"
  "src/loose_index.cpp"
  "src/loose_index.h"
  "src/proxy_exports.cpp"
  "src/proxy_exports.h"
)

target_link_libraries(ModPatcher PRIVATE minhook)

set_target_properties(ModPatcher PROPERTIES OUTPUT_NAME "dinput8")

# Export forwarding to REALDINPUT8.dll (a renamed, bundled copy of the real system dinput8.dll -
# see README.md). MSVC's linker cannot create true cross-DLL PE export forwarders from source -
# neither ".def EXPORTS Name=Module.Symbol" nor "/EXPORT:Name=Module.Symbol" work (both fail with
# LNK2001; the right-hand side must resolve to a real symbol in our own objects). Instead each
# public name is aliased (via /EXPORT:Public=Internal, using the UNDECORATED internal name - the
# linker applies its own C-decoration search, an explicitly-decorated name here fails to resolve)
# to a naked JMP-thunk symbol defined in proxy_exports.cpp - see that file for why a JMP thunk
# works regardless of the real function's actual signature.
set(LOOSEMODS_FORWARDED_EXPORTS
  DirectInput8Create
  DllCanUnloadNow
  DllGetClassObject
  DllRegisterServer
  DllUnregisterServer
  GetdfDIJoystick
)
foreach(export_name IN LISTS LOOSEMODS_FORWARDED_EXPORTS)
  target_link_options(ModPatcher PRIVATE "/EXPORT:${export_name}=${export_name}_Thunk")
endforeach()

# Defensive: these thunks are never called from our own code (only reached via the export
# table), so make sure the linker's dead-symbol-stripping (/OPT:REF, on by default in Release)
# can never discard them before /EXPORT gets a chance to alias them.
target_link_options(ModPatcher PRIVATE "/OPT:NOREF")

if (CMAKE_VERSION VERSION_GREATER 3.12)
  set_property(TARGET ModPatcher PROPERTY CXX_STANDARD 20)
endif()

# TODO: Add tests and install targets if needed.
