@echo off
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"

echo ========================================
echo   GitConfigTool Build
echo ========================================
echo.

echo [1/4] Checking environment ...
where dotnet >nul 2>&1
if errorlevel 1 (
  echo.
  echo ERROR: dotnet not found on PATH.
  echo Install .NET SDK and add it to PATH.
  echo.
  pause
  exit /b 1
)

for /f "tokens=*" %%V in ('dotnet --version 2^>nul') do set "DOTNET_VER=%%V"
echo       dotnet %DOTNET_VER%

if not exist "GitConfigTool.csproj" (
  echo.
  echo ERROR: GitConfigTool.csproj not found.
  echo Run this script from the project root.
  echo.
  pause
  exit /b 1
)
echo       project file OK
echo.

echo [2/4] Reading version.ini ...
set "APPNAME="
set "APPVER="
if exist "version.ini" (
  for /f "usebackq tokens=1,* delims== " %%A in ("version.ini") do (
    if /i "%%A"=="name"    set "APPNAME=%%B"
    if /i "%%A"=="version" set "APPVER=%%B"
  )
)
if defined APPNAME set "APPNAME=!APPNAME: =!"
if defined APPVER  set "APPVER=!APPVER: =!"
if defined APPNAME echo       name    : !APPNAME!
if defined APPVER  echo       version : !APPVER!
if not defined APPNAME echo       WARNING: name not found in version.ini
if not defined APPVER  echo       WARNING: version not found in version.ini
echo.

echo [3/4] Building Release ...
dotnet build -c Release --nologo
if errorlevel 1 (
  echo.
  echo BUILD FAILED
  echo.
  pause
  exit /b 1
)
echo.

echo [4/4] Packing artifact ...
set "ARTIFACT=%CD%\bin\Release\GitConfigTool.exe"

if not exist "%ARTIFACT%" (
  echo.
  echo ERROR: Build succeeded but GitConfigTool.exe not found.
  echo Check bin\Release directory.
  echo.
  pause
  exit /b 1
)

set "FINAL=%ARTIFACT%"

if defined APPNAME if defined APPVER (
  set "RELEASE=%CD%\bin\Release\!APPNAME!_!APPVER!.exe"
  move /y "%ARTIFACT%" "!RELEASE!" >nul
  if errorlevel 1 (
    echo       WARNING: rename failed, keeping GitConfigTool.exe
  ) else (
    set "FINAL=!RELEASE!"
  )
) else (
  echo       WARNING: version.ini missing or incomplete, keeping GitConfigTool.exe
)

for %%A in ("!FINAL!") do set "SIZE=%%~zA"
set /a "SIZE_KB=!SIZE!/1024"

echo.
echo BUILD OK  -^> !FINAL!  (!SIZE_KB! KB)
echo.
%SystemRoot%\System32\timeout.exe /t 3 /nobreak >nul
endlocal
exit /b 0
