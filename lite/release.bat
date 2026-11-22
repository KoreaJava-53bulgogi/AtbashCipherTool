@echo off
setlocal
cd /d "%~dp0"

rem ==================================================
rem  Atbash Cipher Tool Lite (Rust) release build.
rem  1) cargo test          : stop if any test fails
rem  2) cargo build         : lite\target\release\AtbashCipherToolLite.exe
rem  3) Inno Setup          : lite\installer\Output\Setup_Lite_<version>.exe
rem  4) version.json        : only the "win-x64-lite" entry is changed
rem  The version comes from Cargo.toml (keep it the same as the full version).
rem  GitHub tag for the Lite version: lite_<version>, for example lite_9.1.0
rem  Keep this file ASCII only with CRLF line endings.
rem ==================================================

set "ISCC=C:\Program Files\Inno Setup 7\ISCC.exe"
set "DOTNET=C:\Program Files\dotnet\dotnet.exe"

echo [0/4] Checking tools and version...
where cargo >nul 2>nul
if errorlevel 1 ( echo [ERROR] cargo not found. Install Rust from https://rustup.rs & goto :error )
if not exist "%ISCC%" ( echo [ERROR] Inno Setup not found. Check the ISCC path. & goto :error )
if not exist "%DOTNET%" ( echo [ERROR] .NET SDK not found - needed for the release tool. & goto :error )
set "APPVERSION="
for /f tokens^=2^ delims^=^" %%a in ('findstr /b /c:"version = " Cargo.toml') do if not defined APPVERSION set "APPVERSION=%%a"
if not defined APPVERSION ( echo [ERROR] version not found in Cargo.toml. & goto :error )
set "SETUPNAME=Setup_Lite_%APPVERSION%"
echo OK. Version %APPVERSION%
echo.

echo [1/4] Running tests...
cargo test --release
if errorlevel 1 ( echo [ERROR] Tests failed. Fix them before releasing. & goto :error )
echo.

echo [2/4] Building the app...
cargo build --release
if errorlevel 1 ( echo [ERROR] cargo build failed. & goto :error )
if not exist "target\release\AtbashCipherToolLite.exe" ( echo [ERROR] AtbashCipherToolLite.exe was not created. & goto :error )
echo.

echo [3/4] Creating installer with Inno Setup...
"%ISCC%" /Q /DAppVer=%APPVERSION% "installer\AtbashLite.iss"
if errorlevel 1 ( echo [ERROR] Inno Setup compile failed. & goto :error )
echo.

echo [4/4] Updating version.json...
rem Only the win-x64-lite entry is changed. Push version.json only AFTER the installer is uploaded.
"%DOTNET%" run --project ..\csharp\tools\ReleaseTool -c Release -v q -- merge --manifest "%~dp0..\version.json" --platform win-x64-lite --version "%APPVERSION%" --file "%~dp0installer\Output\%SETUPNAME%.exe"
if errorlevel 1 ( echo [ERROR] version.json update failed. & goto :error )

echo.
echo ==================  DONE  ==================
echo  lite\installer\Output\%SETUPNAME%.exe
echo  version.json (updated - push it after uploading the installer; see the tag name above)
echo ==================================================
goto :end

:error
echo.
echo ==================  FAILED  ==================
echo Check the error message above.

:end
echo.
pause
