@echo off
rem One click to start orthomania from this clone: builds the patcher (orthomania.exe) and the score server it can start
rem for development, then opens the patcher. Needs the .NET 10 SDK. The build goes to artifacts\launch (never committed);
rem the first run takes a minute or two, later runs only rebuild what changed.
setlocal
cd /d "%~dp0"
set "OUT=%~dp0artifacts\launch"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo orthomania needs the .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0
    pause
    exit /b 1
)

rem An open patcher keeps its files in use, so it cannot be rebuilt under it: leave that one open.
tasklist /fi "imagename eq orthomania.exe" 2>nul | "%SystemRoot%\System32\find.exe" /i "orthomania.exe" >nul
if not errorlevel 1 (
    echo orthomania is already open.
    "%SystemRoot%\System32\timeout.exe" /t 3 >nul
    exit /b 0
)

echo Building the patcher...
dotnet publish patcher\src\Orthomania.Patcher -c Release -r win-x64 --self-contained false -o "%OUT%" -nologo -v q
if errorlevel 1 goto failed

rem Beside the patcher, where its dev option ("Start the score server with osu!") looks for it.
echo Building the score server...
dotnet publish server\OrthoServer -c Release -r win-x64 --self-contained false -o "%OUT%\server" -nologo -v q
if errorlevel 1 (
    if not exist "%OUT%\server\OrthoServer.exe" goto failed
    echo The score server could not be rebuilt ^(is it running?^); using the last build of it.
)

rem CI runs this file to check it builds, without opening the window.
if defined ORTHOMANIA_BUILD_ONLY exit /b 0

start "" "%OUT%\orthomania.exe"
exit /b 0

:failed
echo.
echo The build failed; the lines above say why.
if not defined ORTHOMANIA_BUILD_ONLY pause
exit /b 1
