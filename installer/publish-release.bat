@echo off
REM ============================================================================
REM ProWeigh Fusion - Single-File Publisher
REM Publishes ProWeighFusion.csproj as a self-contained win-x64
REM single-file executable into installer\Publish.
REM ============================================================================

setlocal
set "SCRIPT_DIR=%~dp0"
set "ROOT=%SCRIPT_DIR%.."
set "PROJECT_FILE=%ROOT%\ProWeighFusion.csproj"
set "PUBLISH_DIR=%SCRIPT_DIR%Publish"

if not exist "%PROJECT_FILE%" (
    echo [ERROR] Project file not found: %PROJECT_FILE%
    exit /b 1
)

echo [1/3] Publishing single-file executable...
if exist "%PUBLISH_DIR%" rd /s /q "%PUBLISH_DIR%"
dotnet publish "%PROJECT_FILE%" ^
    --configuration Release ^
    --runtime win-x64 ^
    --self-contained true ^
    --output "%PUBLISH_DIR%" ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:DebugType=None ^
    -p:DebugSymbols=false
if errorlevel 1 ( echo [ERROR] Publish failed! & exit /b 1 )

echo [2/3] Verifying output...
if not exist "%PUBLISH_DIR%\ProWeighSuitePlatforms.exe" (
    echo [ERROR] Expected executable not found in %PUBLISH_DIR%
    exit /b 1
)

echo [3/3] Published files:
dir /b "%PUBLISH_DIR%"

echo.
echo SUCCESS: %PUBLISH_DIR%\ProWeighSuitePlatforms.exe
endlocal
