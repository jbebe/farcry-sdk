@echo off
@rem Builds LuaJIT as a static x86 library for FCSE. Invoked from cmake\luajit.cmake, never by hand.
@rem
@rem   %1  LuaJIT src directory (the one holding msvcbuild.bat)
@rem
@rem Upstream ships msvcbuild.bat instead of CMake, so this wraps it rather than reimplementing
@rem LuaJIT's build - which is a real bootstrap (minilua -> buildvm_arch.h -> buildvm -> the lj_*def.h
@rem tables and lj_vm.obj), not just a list of .c files. Same reasoning as the hand-rolled MinHook
@rem target in CMakeLists.txt: vendor upstream's own build, don't re-derive it.
setlocal
set SRC=%~1
if "%SRC%"=="" (echo [luajit] no source directory passed & exit /b 1)
if not exist "%SRC%\msvcbuild.bat" (echo [luajit] msvcbuild.bat not found in "%SRC%" & exit /b 1)

@rem msvcbuild.bat needs a Visual Studio environment and only checks INCLUDE to decide. build.ps1
@rem already wraps the whole cmake invocation in vcvarsall x86, and Ninja passes that environment
@rem down to this command, so there is nothing to set up here - but say so clearly if it is missing,
@rem because msvcbuild.bat's own message ("You must open a Visual Studio Command Prompt") gives no
@rem hint about which layer failed to pass it along.
if not defined INCLUDE (
  echo [luajit] no Visual Studio environment - INCLUDE is unset.
  echo [luajit] Build through tools\FCSE\build.ps1, which runs vcvarsall.bat x86 first.
  exit /b 1
)

@rem msvcbuild.bat runs the minilua.exe and buildvm.exe it has just built by bare name, relying on
@rem cmd resolving them from the current directory. That resolution is disabled when
@rem NoDefaultCurrentDirectoryInExePath is set (it is, on some Windows installs and on hardened CI
@rem images), and the build then fails midway with a bare "'minilua' is not recognized" - after
@rem having compiled cleanly, which makes it read like a LuaJIT bug rather than an environment one.
@rem Putting the directory on PATH explicitly makes it work either way.
set PATH=%SRC%;%PATH%

cd /d "%SRC%" || (echo [luajit] could not enter "%SRC%" & exit /b 1)

@rem "static" gives lua51.lib for linking straight into FCSE.exe rather than a lua51.dll beside it -
@rem FCSE ships as a single file. That path compiles with msvcbuild.bat's base flags, which name no
@rem CRT, so cl defaults to /MT and the result is LIBCMT - exactly the static CRT CMakeLists.txt
@rem requires. Verified with `dumpbin /directives lua51.lib`; the release workflow re-checks the
@rem final exe for dynamic-CRT imports anyway.
call "%SRC%\msvcbuild.bat" static
if errorlevel 1 (echo [luajit] msvcbuild.bat failed & exit /b 1)
if not exist "%SRC%\lua51.lib" (echo [luajit] msvcbuild.bat reported success but lua51.lib is missing & exit /b 1)

echo [luajit] built %SRC%\lua51.lib
exit /b 0
