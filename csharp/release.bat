@echo off
setlocal
cd /d "%~dp0"

rem ==================================================
rem  C# version (9.x) release build.
rem  1) dotnet test     : stop if any test fails
rem  2) dotnet publish  : csharp\publish  (self-contained, no Java/.NET install needed)
rem  3) Inno Setup      : csharp\installer\Output\Setup_<version>.exe
rem  4) zip + version.json (repo root) for the auto-update
rem  Optional code signing: set SIGN_PFX and SIGN_PASSWORD before running.
rem
rem  The Java version (8.x) is still built by ..\release.bat.
rem  Keep this file ASCII only with CRLF line endings.
rem ==================================================

set "DOTNET=C:\Program Files\dotnet\dotnet.exe"
set "ISCC=C:\Program Files\Inno Setup 7\ISCC.exe"
set "TAR=%SystemRoot%\System32\tar.exe"
set "SIGNTOOL=C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe"
set "TIMESTAMP_URL=http://timestamp.digicert.com"
set "ISS=installer\Atbash.iss"
set "CSPROJ=src\Atbash.App\Atbash.App.csproj"

echo [0/5] Checking tools and versions...
if not exist "%DOTNET%" ( echo [ERROR] .NET SDK not found. & goto :error )
if not exist "%ISCC%" ( echo [ERROR] Inno Setup not found. Check the ISCC path. & goto :error )
if not exist "%TAR%" ( echo [ERROR] tar.exe not found. & goto :error )
if not exist "%ISS%" ( echo [ERROR] %ISS% is missing. & goto :error )
set "APPVERSION="
for /f "tokens=2 delims==" %%a in ('findstr /b /c:"AppVersion=" "%ISS%"') do set "APPVERSION=%%a"
set "SETUPNAME="
for /f "tokens=2 delims==" %%a in ('findstr /b /c:"OutputBaseFilename=" "%ISS%"') do set "SETUPNAME=%%a"
if not defined APPVERSION ( echo [ERROR] AppVersion not found in %ISS%. & goto :error )
if not defined SETUPNAME ( echo [ERROR] OutputBaseFilename not found in %ISS%. & goto :error )
rem The app's own version (shown in the title and used for update checks) must match the installer.
findstr /c:"<Version>%APPVERSION%</Version>" "%CSPROJ%" >nul
if errorlevel 1 ( echo [ERROR] Version mismatch: %ISS% says %APPVERSION%, but %CSPROJ% has a different Version. & goto :error )
echo OK. Version %APPVERSION%
echo.

echo [1/5] Running tests...
"%DOTNET%" test AtbashCipher.sln --nologo -v q
if errorlevel 1 ( echo [ERROR] Tests failed. Fix them before releasing. & goto :error )
echo.

echo [2/5] Publishing the app...
if exist "publish" rmdir /s /q "publish"
"%DOTNET%" publish "%CSPROJ%" -c Release -o publish --nologo -v q
if errorlevel 1 ( echo [ERROR] dotnet publish failed. & goto :error )
if not exist "publish\AtbashCipherTool.exe" ( echo [ERROR] publish\AtbashCipherTool.exe was not created. & goto :error )
if not defined SIGN_PFX goto :nosign_exe
echo Signing exe...
"%SIGNTOOL%" sign /f "%SIGN_PFX%" /p "%SIGN_PASSWORD%" /fd SHA256 /tr "%TIMESTAMP_URL%" /td SHA256 "publish\AtbashCipherTool.exe"
if errorlevel 1 ( echo [ERROR] Signing the exe failed. & goto :error )
:nosign_exe
echo.

echo [3/5] Creating installer with Inno Setup...
"%ISCC%" /Q "%ISS%"
if errorlevel 1 ( echo [ERROR] Inno Setup compile failed. & goto :error )
if not defined SIGN_PFX goto :nosign_setup
echo Signing installer...
"%SIGNTOOL%" sign /f "%SIGN_PFX%" /p "%SIGN_PASSWORD%" /fd SHA256 /tr "%TIMESTAMP_URL%" /td SHA256 "installer\Output\%SETUPNAME%.exe"
if errorlevel 1 ( echo [ERROR] Signing the installer failed. & goto :error )
:nosign_setup
echo.

echo [4/5] Creating zip...
if exist "installer\Output\%SETUPNAME%.zip" del /q "installer\Output\%SETUPNAME%.zip"
"%TAR%" -a -c -f "installer\Output\%SETUPNAME%.zip" -C "installer\Output" "%SETUPNAME%.exe"
if errorlevel 1 ( echo [ERROR] zip failed. & goto :error )
echo.

echo [5/5] Updating version.json...
rem Only the win-x64 entry is changed (macOS / Linux entries stay as they are).
rem The GitHub tag for Windows is the plain version, for example 9.1.0 (see csharp\RELEASING.md).
rem Push version.json to GitHub only AFTER the installer is uploaded to that release.
rem (do not add --nologo here: dotnet run would hand it to the tool as its first argument)
"%DOTNET%" run --project tools\ReleaseTool -c Release -v q -- merge --manifest "%~dp0..\version.json" --platform win-x64 --version "%APPVERSION%" --file "%~dp0installer\Output\%SETUPNAME%.exe"
if errorlevel 1 ( echo [ERROR] version.json update failed. & goto :error )
echo.

echo ==================================================
echo  DONE! Version %APPVERSION%
echo  csharp\installer\Output\%SETUPNAME%.exe
echo  csharp\installer\Output\%SETUPNAME%.zip
echo  version.json (updated - push it after uploading the installer; see the tag name above)
if not defined SIGN_PFX echo  (not code-signed: SIGN_PFX is not set)
echo ==================================================
goto :end

:error
echo.
echo ==================  FAILED  ==================
echo Check the error message above.

:end
echo.
pause
