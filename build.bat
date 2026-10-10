@echo off
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"

echo ========================================
echo   GitConfigTool Build
echo ========================================
echo.

echo [1/3] Checking environment ...
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

echo [2/3] Building Release ...
dotnet build -c Release --nologo
if errorlevel 1 (
  echo.
  echo BUILD FAILED
  echo.
  pause
  exit /b 1
)
echo.

echo [3/3] Locating output ...
set "ARTIFACT=%CD%\bin\Release\GitConfigTool.exe"

if not exist "%ARTIFACT%" (
  echo.
  echo ERROR: Build succeeded but GitConfigTool.exe not found.
  echo Check bin\Release directory.
  echo.
  pause
  exit /b 1
)

for %%A in ("%ARTIFACT%") do set "SIZE=%%~zA"
set /a "SIZE_KB=!SIZE!/1024"

echo.
echo BUILD OK  -^> %ARTIFACT%  (!SIZE_KB! KB)
echo.
timeout /t 2 /nobreak >nul
endlocal
exit /b 0
