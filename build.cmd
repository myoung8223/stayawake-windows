@echo off
rem Builds Stay Awake and its MSI.  Usage:  build.cmd [version]   e.g.  build.cmd 1.0.2
rem Needs the .NET SDK (dotnet). No Visual Studio, no WiX.
setlocal
set "VERSION=%~1"
if "%VERSION%"=="" set "VERSION=1.0.1"
set "ROOT=%~dp0"
set "OUT=%ROOT%out"

if exist "%OUT%" rmdir /s /q "%OUT%"

echo.
echo === Building Stay Awake %VERSION% ===
dotnet build "%ROOT%src\Agent\StayAwake.csproj" -c Release -o "%OUT%\bin" -p:Version=%VERSION% -nologo -v minimal || goto :fail
dotnet build "%ROOT%src\Service\StayAwakeService.csproj" -c Release -o "%OUT%\bin" -p:Version=%VERSION% -nologo -v minimal || goto :fail
dotnet build "%ROOT%src\MsiBuilder\MsiBuilder.csproj" -c Release -o "%OUT%\tools" -nologo -v minimal || goto :fail

echo.
echo === Building MSI ===
"%OUT%\tools\MsiBuilder.exe" "%OUT%\bin" "%OUT%\StayAwake-%VERSION%.msi" %VERSION% || goto :fail

echo.
echo Done: %OUT%\StayAwake-%VERSION%.msi
exit /b 0

:fail
echo.
echo BUILD FAILED
exit /b 1
