@echo off
setlocal

echo ==================================================
echo      Atbash Cipher Application JAR Builder
echo ==================================================
echo.

echo [0/2] Java 및 Maven 버전 확인...
java -version >nul 2>&1
if %errorlevel% neq 0 (
    echo [오류] Java를 찾을 수 없습니다. JDK가 설치되어 있고 PATH에 등록되어 있는지 확인하세요.
    goto :error
)

call mvn -version >nul 2>&1
if %errorlevel% neq 0 (
    echo [오류] Maven을 찾을 수 없습니다. Maven이 설치되어 있고 PATH에 등록되어 있는지 확인하세요.
    goto :error
)

echo Java 및 Maven 확인 완료.
echo.

echo [1/2] Maven을 사용하여 빌드 시작...
call mvn clean package

if %errorlevel% neq 0 (
    echo ERROR: Maven 빌드에 실패했습니다.
    goto :error
)

echo ==================================================
echo SUCCESS!
echo 'target/AtbashCipherTool.jar' 가 생성되었습니다.
echo ==================================================
goto :end

:error
echo.
echo !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
echo                  BUILD FAILED
echo !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
echo Please check the error messages above.
:end
echo.
pause