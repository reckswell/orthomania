@echo off
rem One click to start orthomania from this clone: builds the patcher as one file, orthomania.exe, and the score server it
rem can start for development, then opens the patcher. Every run builds first, so it always runs this clone's code: the first
rem run takes a minute or two, later runs only rebuild what changed (a few seconds when nothing did). Needs the .NET 10 SDK.
rem The build goes to artifacts\launch (never committed). orthomania.exe there is the file to release: the .NET runtime and
rem everything else it needs are inside it, so players need nothing else.
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

rem Before orthomania.exe was one file, its DLLs were built beside it here. Nothing uses them now, so that folder starts again.
if exist "%OUT%\osu.Framework.dll" rmdir /s /q "%OUT%"

rem Published for win-x64, the patcher's project makes it one self-contained file (Orthomania.Patcher.csproj).
echo Building orthomania.exe...
dotnet publish patcher\src\Orthomania.Patcher -c Release -r win-x64 -o "%OUT%" -nologo -v q
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

echo Starting %OUT%\orthomania.exe
start "" "%OUT%\orthomania.exe"
exit /b 0

:failed
echo.
echo The build failed; the lines above say why.
if not defined ORTHOMANIA_BUILD_ONLY pause
exit /b 1
