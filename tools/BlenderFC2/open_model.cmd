@echo off
rem Open a Far Cry 2 model pack in Blender, optionally animated.
rem
rem   open_model.cmd path\to\model.fc2model [lod] [clip]
rem
rem Build a pack first with: jackall-cli fc2model export <model.xbg> --clips
rem Set BLENDER to override the executable.

setlocal
if "%BLENDER%"=="" set "BLENDER=C:\Programs\Blender 5.2\blender.exe"

if "%~1"=="" (
    echo usage: open_model.cmd ^<model.fc2model^> [lod] [clip]
    exit /b 1
)
if not exist "%BLENDER%" (
    echo Blender not found at "%BLENDER%" - set the BLENDER variable to its path.
    exit /b 1
)

rem Quote each argument only when it is present, so an absent one is not passed
rem as an empty string the script would then have to filter out.
set "ARGS=%~1"
if not "%~2"=="" set "ARGS=%ARGS% %~2"
if not "%~3"=="" set "ARGS=%ARGS% %~3"

"%BLENDER%" --python "%~dp0open_model.py" -- %ARGS%
