@echo off
rem Open a Far Cry 2 model or bundle in Blender, optionally animated.
rem
rem   open_model.cmd path\to\model.xbg [lod] [clip.mab]
rem
rem Set BLENDER to override the executable.

setlocal
if "%BLENDER%"=="" set "BLENDER=C:\Programs\Blender 5.2\blender.exe"

if "%~1"=="" (
    echo usage: open_model.cmd ^<model.xbg or .fc2model^> [lod] [clip.mab]
    exit /b 1
)
if not exist "%BLENDER%" (
    echo Blender not found at "%BLENDER%" - set the BLENDER variable to its path.
    exit /b 1
)

rem Quote each argument only when it is present, so an absent one is not passed
rem through as an empty string.
set "ARGS="%~f1""
if not "%~2"=="" set "ARGS=%ARGS% "%~2""
if not "%~3"=="" set "ARGS=%ARGS% "%~f3""

start "" "%BLENDER%" --python "%~dp0open_model.py" -- %ARGS%
endlocal
