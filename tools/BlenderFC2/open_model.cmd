@echo off
rem Open a Far Cry 2 model in Blender.
rem
rem   open_model.cmd path\to\model.xbg [lod]
rem
rem Set BLENDER to override the executable.

setlocal
if "%BLENDER%"=="" set "BLENDER=C:\Programs\Blender 5.2\blender.exe"

if "%~1"=="" (
    echo usage: open_model.cmd ^<model.xbg^> [lod]
    exit /b 1
)
if not exist "%BLENDER%" (
    echo Blender not found at "%BLENDER%" - set the BLENDER variable to its path.
    exit /b 1
)

start "" "%BLENDER%" --python "%~dp0open_model.py" -- "%~f1" %2
endlocal
