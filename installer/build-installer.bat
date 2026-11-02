@echo off
REM ============================================================================
REM ProWeigh Fusion - Installer Builder
REM Publishes the single-file executable and compiles the Inno Setup installer
REM using the Fusion branding profile (name, AppId, version).
REM ============================================================================

setlocal
set "SCRIPT_DIR=%~dp0"
set "ROOT=%SCRIPT_DIR%.."
set "PROFILE=%ROOT%\branding\fusion\branding.json"
set "VERSION_PROPS=%ROOT%\branding\fusion\Version.props"
set "PUBLISH_DIR=%SCRIPT_DIR%Publish"
set "ISC=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"

if not exist "%PROFILE%" ( echo [ERROR] Branding profile not found: %PROFILE% & exit /b 1 )

for /f "usebackq delims=" %%V in (`powershell -NoProfile -Command "$p = ConvertFrom-Json -InputObject (Get-Content -Raw -LiteralPath '%PROFILE%'); $p.installerName"`) do set "APPNAME=%%V"
for /f "usebackq delims=" %%V in (`powershell -NoProfile -Command "$p = ConvertFrom-Json -InputObject (Get-Content -Raw -LiteralPath '%PROFILE%'); $p.issAppId"`) do set "APPID=%%V"
if "%APPNAME%"=="" set "APPNAME=ProWeigh Fusion"
if "%APPID%"=="" ( echo [ERROR] issAppId missing from %PROFILE% & exit /b 1 )

for /f "usebackq delims=" %%V in (`powershell -NoProfile -Command "([xml](Get-Content -Raw -LiteralPath '%VERSION_PROPS%')).Project.PropertyGroup.Version"`) do set "APPVER=%%V"
if "%APPVER%"=="" set "APPVER=1.1.6"

echo [1/3] Publishing %APPNAME% v%APPVER% ...
call "%SCRIPT_DIR%publish-release.bat"
if errorlevel 1 ( echo [ERROR] Publish failed! & exit /b 1 )

echo [2/3] Compiling installer ...
if not exist "%ISC%" ( echo [ERROR] ISCC not found. Install Inno Setup 6. & exit /b 1 )
"%ISC%" /DMyAppName="%APPNAME%" /DMyAppVersion="%APPVER%" /DMyAppId="%APPID%" "%SCRIPT_DIR%ProWeighSuitePlatforms.iss"
if errorlevel 1 ( echo [ERROR] Installer failed! & exit /b 1 )

echo [3/3] Installer created in %SCRIPT_DIR%installer
endlocal
