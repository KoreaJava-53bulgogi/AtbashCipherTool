@echo off
chcp 65001 >nul
echo Atbash Cipher Tool을 실행하는 중...

if exist target\AtbashCipherTool.jar (
    rem FAT JAR는 라이브러리가 포함되어 있으므로 -jar 명령어로 실행합니다.
    java -jar target\AtbashCipherTool.jar
) else (
    echo [오류] target\AtbashCipherTool.jar 파일을 찾을 수 없습니다.
    echo 먼저 build.bat를 실행해서 프로그램을 빌드해주세요.
)

pause