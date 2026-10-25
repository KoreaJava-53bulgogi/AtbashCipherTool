@echo off
setlocal
cd /d "%~dp0"

rem ==================================================
rem  Build everything for release in one go.
rem  1) Maven      : target\AtbashCipherTool.jar
rem  2) jlink      : target\runtime  (slim Java runtime, ~48MB)
rem  3) Launch4j   : target\Release\AtbashCipherTool.exe
rem  4) Inno Setup : target\Output\Setup_<version>.exe
rem  5) zip        : target\Output\Setup_<version>.zip  (+ version.json for updates)
rem
rem  Note: "mvn clean" is NOT used, because it would delete the
rem  installer files in target (Atbash.iss, icons...).
rem  Only target\classes and the jar are removed before building.
rem
rem  Keep this file ASCII only with CRLF line endings,
rem  otherwise cmd.exe misreads the lines.
rem ==================================================

set "JDK=C:\Program Files\Java\jdk-25.0.3"
set "LAUNCH4J=C:\Program Files (x86)\Launch4j\launch4jc.exe"
set "ISCC=C:\Program Files\Inno Setup 7\ISCC.exe"
set "TAR=%SystemRoot%\System32\tar.exe"

rem Optional code signing. Skipped unless SIGN_PFX is set, e.g.
rem   set SIGN_PFX=C:\certs\mycert.pfx
rem   set SIGN_PASSWORD=(certificate password)
rem   release.bat
rem Do not write the password in this file.
rem sign4j (from Launch4j) is needed so signing does not break the jar inside the exe.
set "SIGNTOOL=C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe"
set "SIGN4J=C:\Program Files (x86)\Launch4j\sign4j\sign4j.exe"
set "TIMESTAMP_URL=http://timestamp.digicert.com"

rem Java modules the app needs. To re-check after adding features, run:
rem   jdeps --ignore-missing-deps --print-module-deps target\AtbashCipherTool.jar
rem then add any new module here.
set "MODULES=java.base,java.desktop,java.prefs,java.sql,java.logging,jdk.unsupported,jdk.charsets,jdk.crypto.ec"

echo [0/5] Checking tools and files...
call mvn -version >nul 2>&1
if errorlevel 1 ( echo [ERROR] Maven not found. & goto :error )
if not exist "%JDK%\bin\jlink.exe" ( echo [ERROR] JDK not found. Check the JDK path. & goto :error )
if not exist "%LAUNCH4J%" ( echo [ERROR] Launch4j not found. Check the LAUNCH4J path. & goto :error )
if not exist "%ISCC%" ( echo [ERROR] Inno Setup not found. Check the ISCC path. & goto :error )
if not exist "%TAR%" ( echo [ERROR] tar.exe not found. & goto :error )
if not exist "target\Atbash.iss" ( echo [ERROR] target\Atbash.iss is missing. & goto :error )
if not exist "target\icon.ico" ( echo [ERROR] target\icon.ico is missing. & goto :error )
rem Read the installer file name (e.g. Setup_7.1.0) from Atbash.iss
set "SETUPNAME="
for /f "tokens=2 delims==" %%a in ('findstr /b /c:"OutputBaseFilename=" "target\Atbash.iss"') do set "SETUPNAME=%%a"
if not defined SETUPNAME ( echo [ERROR] OutputBaseFilename not found in Atbash.iss. & goto :error )
rem Read the version (e.g. 8.1.0) from Atbash.iss for version.json
set "APPVERSION="
for /f "tokens=2 delims==" %%a in ('findstr /b /c:"AppVersion=" "target\Atbash.iss"') do set "APPVERSION=%%a"
if not defined APPVERSION ( echo [ERROR] AppVersion not found in Atbash.iss. & goto :error )
echo OK.
echo.

echo [1/5] Building jar with Maven...
if exist "target\classes" rmdir /s /q "target\classes"
if exist "target\AtbashCipherTool.jar" del /q "target\AtbashCipherTool.jar"
call mvn package
if errorlevel 1 ( echo [ERROR] Maven build failed. & goto :error )
if not exist "target\AtbashCipherTool.jar" ( echo [ERROR] jar was not created. & goto :error )
echo.

echo [2/5] Creating slim Java runtime with jlink...
if exist "target\runtime" rmdir /s /q "target\runtime"
"%JDK%\bin\jlink.exe" --add-modules %MODULES% --strip-debug --no-header-files --no-man-pages --compress=zip-6 --output "target\runtime"
if errorlevel 1 ( echo [ERROR] jlink failed. & goto :error )
echo.

echo [3/5] Creating exe with Launch4j...
if not exist "target\Release" mkdir "target\Release"
"%LAUNCH4J%" "%~dp0launch4j.xml"
if errorlevel 1 ( echo [ERROR] Launch4j failed. & goto :error )
if not defined SIGN_PFX goto :nosign_exe
echo Signing exe...
"%SIGN4J%" --verbose "%SIGNTOOL%" sign /f "%SIGN_PFX%" /p "%SIGN_PASSWORD%" /fd SHA256 /tr "%TIMESTAMP_URL%" /td SHA256 "target\Release\AtbashCipherTool.exe"
if errorlevel 1 ( echo [ERROR] Signing the exe failed. & goto :error )
:nosign_exe
echo.

echo [4/5] Creating installer with Inno Setup...
"%ISCC%" "%~dp0target\Atbash.iss"
if errorlevel 1 ( echo [ERROR] Inno Setup compile failed. & goto :error )
if not defined SIGN_PFX goto :nosign_setup
echo Signing installer...
"%SIGNTOOL%" sign /f "%SIGN_PFX%" /p "%SIGN_PASSWORD%" /fd SHA256 /tr "%TIMESTAMP_URL%" /td SHA256 "target\Output\%SETUPNAME%.exe"
if errorlevel 1 ( echo [ERROR] Signing the installer failed. & goto :error )
:nosign_setup
echo.

echo [5/5] Creating zip and version.json...
if exist "target\Output\%SETUPNAME%.zip" del /q "target\Output\%SETUPNAME%.zip"
"%TAR%" -a -c -f "target\Output\%SETUPNAME%.zip" -C "target\Output" "%SETUPNAME%.exe"
if errorlevel 1 ( echo [ERROR] zip failed. & goto :error )
rem version.json: version, download link and SHA-256 of the (signed) installer.
rem Push it to GitHub only AFTER the installer is uploaded to the v%APPVERSION% release.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\make-version-json.ps1" -Version "%APPVERSION%" -Installer "%~dp0target\Output\%SETUPNAME%.exe" -Out "%~dp0version.json"
if errorlevel 1 ( echo [ERROR] version.json update failed. & goto :error )
echo.

echo ==================================================
echo  DONE!
echo  target\Output\%SETUPNAME%.exe
echo  target\Output\%SETUPNAME%.zip
echo  version.json (updated - push it after uploading the installer)
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
