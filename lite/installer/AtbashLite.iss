; ==================================================================
;  Atbash Cipher Tool Lite 설치 스크립트. lite\release.bat 이 사용함.
;  버전은 release.bat 이 Cargo.toml 에서 읽어 /DAppVer=9.1.0 처럼 넘겨줌.
;  정식 버전(AppId "Atbash Cipher Tool")과 다른 AppId 라서 둘을 함께 설치할 수 있음.
; ==================================================================
#ifndef AppVer
  #define AppVer "0.0.0"
#endif

[Setup]
; "같은 앱"으로 알아보는 고유 이름. 절대 바꾸지 말 것 (바꾸면 업데이트가 아니라 새 앱으로 설치됨)
AppId=Atbash Cipher Tool Lite
AppName=Atbash Cipher Tool Lite
AppVersion={#AppVer}
AppPublisher=June
AppSupportURL=https://github.com/KoreaJava-53bulgogi/AtbashCipherTool
DefaultDirName={autopf}\Atbash Cipher Tool Lite
DefaultGroupName=Atbash Cipher Tool Lite
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=Setup_Lite_{#AppVer}
Compression=lzma2/max
SolidCompression=yes
SetupIconFile=..\..\csharp\src\Atbash.App\Assets\app.ico
UninstallDisplayIcon={app}\AtbashCipherToolLite.exe
UninstallDisplayName=Atbash Cipher Tool Lite
; 64비트로 빌드한 앱
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 다크/라이트 모드를 따라감
WizardStyle=modern dynamic
; 설치 중 앱이 켜져 있으면 닫기를 제안함
CloseApplications=yes
; Windows 언어와 맞는 언어가 있으면 언어 선택 창을 건너뜀
ShowLanguageDialog=auto

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\target\release\AtbashCipherToolLite.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Atbash Cipher Tool Lite"; Filename: "{app}\AtbashCipherToolLite.exe"
Name: "{autodesktop}\Atbash Cipher Tool Lite"; Filename: "{app}\AtbashCipherToolLite.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\AtbashCipherToolLite.exe"; Description: "{cm:LaunchProgram,Atbash Cipher Tool Lite}"; Flags: nowait postinstall skipifsilent
