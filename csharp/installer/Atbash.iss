; ==================================================================
;  C# 버전 (9.x) 설치 스크립트. csharp\release.bat 이 사용함.
;  Java 버전(target\Atbash.iss)과 AppId·exe 이름이 같아서, Java 7.x~8.x 위에
;  덮어 설치하면 같은 앱의 업데이트로 처리됨 (Java 8.1.0 자동 업데이트로도 받을 수 있음).
;  설치 화면 그림·아이콘·스플래시는 Java 버전과 같은 파일을 씀 (target\, startup.png).
; ==================================================================
[Setup]
; 업데이트할 때 "같은 앱"으로 알아보는 고유 이름. 절대 바꾸지 말 것.
; (7.1.0~8.0.0 은 AppId 를 따로 쓰지 않아 AppName 이 대신 쓰였으므로 같은 값으로 고정)
AppId=Atbash Cipher Tool
AppName=Atbash Cipher Tool
AppVersion=9.0.0
DefaultDirName={autopf}\Atbash Cipher Tool
DefaultGroupName=Atbash Cipher Tool
OutputDir=Output
OutputBaseFilename=Setup_9.0.0
Compression=lzma
SolidCompression=yes
WizardImageFile=..\..\target\installer.bmp
WizardSmallImageFile=..\..\target\logo.bmp
SetupIconFile=..\..\target\iconf.ico
; C# 앱은 64비트로 빌드되므로 64비트 Windows 에만 설치
ArchitecturesAllowed=x64compatible
; 설치 폴더는 예전 버전과 같은 곳을 그대로 씀 (Java 버전은 32비트 모드로 설치되었음)
UsePreviousAppDir=yes
; 테마: dynamic = Windows 다크/라이트 모드를 자동으로 따라감
; 내장 스타일: polar, slate, zircon, stellar, windows11
WizardStyle=modern dynamic polar includetitlebar
; 창 크기 (기본 120%)
WizardSizePercent=130

; 필요 없는 페이지 숨기기
DisableWelcomePage=no
DisableProgramGroupPage=yes
DisableDirPage=auto

; Windows 언어와 맞는 언어가 있으면 언어 선택 창을 건너뜀
ShowLanguageDialog=auto

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
korean.WelcomeLabel1=Atbash Cipher Tool 설치를 시작합니다

; [Code]에서 쓰는 언어별 문구. 코드에서는 CustomMessage('이름')으로 불러옴
[CustomMessages]
korean.FeatureTitle=주요 기능
korean.Feature1=· 아트배쉬 · 카이사르 · 비즈네르 · 한글 자모 · AES 암호화
korean.Feature2=· 이미지·캡처 글자 인식(OCR), QR 코드 내보내기
korean.Feature3=· 한국어 · English · 日本語 · 中文 지원
english.FeatureTitle=Key Features
english.Feature1=· Atbash · Caesar · Vigenere · Hangul · AES encryption
english.Feature2=· Text recognition from images and screenshots (OCR), QR export
english.Feature3=· Korean · English · Japanese · Chinese UI
korean.OptionTitle=추가 옵션
korean.OptionDesc=원하는 항목을 선택하세요
korean.OptionDesktop=바탕화면에 바로가기 만들기
korean.OptionStartMenu=시작 메뉴에 바로가기 만들기
english.OptionTitle=Additional Options
english.OptionDesc=Select the options you want
english.OptionDesktop=Create a desktop shortcut
english.OptionStartMenu=Create a Start Menu shortcut
korean.SplashText=설치 준비 중...
english.SplashText=Preparing to install...

; Java 버전(7.x~8.x)이 설치한 Java 런타임(jre 폴더)은 C# 버전에서 쓰지 않으므로 지움
[InstallDelete]
Type: filesandordirs; Name: "{app}\jre"

[Files]
; dotnet publish 결과 (실행 파일, .NET, Avalonia, OCR 엔진·데이터 전부)
Source: "..\publish\*"; \
        DestDir: "{app}"; \
        Flags: ignoreversion recursesubdirs createallsubdirs
; 스플래시 이미지 (설치되지 않고 설치 중에만 임시로 씀)
Source: "..\..\startup.png"; Flags: dontcopy

[Icons]
Name: "{group}\Atbash Cipher Tool"; Filename: "{app}\AtbashCipherTool.exe"; Check: WantStartMenuIcon
Name: "{autodesktop}\Atbash Cipher Tool"; Filename: "{app}\AtbashCipherTool.exe"; Check: WantDesktopIcon


[Run]
Filename: "{app}\AtbashCipherTool.exe"; Description: "{cm:LaunchProgram,Atbash Cipher Tool}"; Flags: nowait postinstall skipifsilent
; 앱의 자동 업데이트(/SILENT 설치)가 끝나면 새 버전을 다시 실행.
; runasoriginaluser: 관리자 권한이 아닌 원래 사용자 권한으로 실행 (드래그 앤 드롭 등이 정상 동작)
Filename: "{app}\AtbashCipherTool.exe"; Flags: nowait runasoriginaluser; Check: WizardSilent

[Code]
const
  SplashMs = 2500;      { 스플래시 표시 시간 (밀리초) }
  SplashW = 640;        { 이미지 표시 크기. startup.png(1000x250)와 같은 4:1 비율 }
  SplashH = 160;
  SplashTextH = 36;     { 이미지 아래 문구 영역 높이 }

var
  OptionPage: TInputOptionWizardPage;

{ 설치 창이 뜨기 전에 startup.png 를 잠깐 보여줌 }
procedure ShowSplash;
var
  Form: TSetupForm;
  Img: TBitmapImage;
  Txt, Ver: TNewStaticText;
begin
  ExtractTemporaryFile('startup.png');

  { True, True = WizardSizePercent(130%)로 창이 커지지 않게 크기 고정 }
  Form := CreateCustomForm(ScaleX(SplashW), ScaleY(SplashH + SplashTextH), True, True);
  try
    Form.BorderStyle := bsNone;
    Form.ClientWidth := ScaleX(SplashW);
    Form.ClientHeight := ScaleY(SplashH + SplashTextH);
    Form.Position := poScreenCenter;

    Img := TBitmapImage.Create(Form);
    Img.Parent := Form;
    Img.SetBounds(0, 0, ScaleX(SplashW), ScaleY(SplashH));
    Img.Stretch := True;
    Img.PngImage.LoadFromFile(ExpandConstant('{tmp}\startup.png'));

    Txt := TNewStaticText.Create(Form);
    Txt.Parent := Form;
    Txt.Caption := CustomMessage('SplashText');
    Txt.Left := (Form.ClientWidth - Txt.Width) div 2;
    Txt.Top := ScaleY(SplashH) + (ScaleY(SplashTextH) - Txt.Height) div 2;

    { 버전: 문구 줄 오른쪽 끝. [Setup]의 AppVersion 값을 컴파일할 때 자동으로 넣음 }
    Ver := TNewStaticText.Create(Form);
    Ver.Parent := Form;
    Ver.Font.Size := 8;
    Ver.Caption := 'v{#SetupSetting("AppVersion")}';
    Ver.Left := Form.ClientWidth - Ver.Width - ScaleX(12);
    Ver.Top := ScaleY(SplashH) + (ScaleY(SplashTextH) - Ver.Height) div 2;

    Form.Show;
    Form.Refresh;
    Sleep(SplashMs);
  finally
    Form.Free;
  end;
end;

{ 설치 창보다 먼저 한 번 호출됨 }
function InitializeSetup: Boolean;
begin
  if not WizardSilent then   { 자동 설치(/SILENT)일 때는 스플래시 생략 }
    ShowSplash;
  Result := True;
end;

{ 설치 중 화면(진행 막대 아래)에 글자 한 줄을 올리는 도우미 함수.
  Top 은 다음 줄 위치로 자동으로 내려감 }
procedure AddInstallingLabel(const Text: String; var Top: Integer; Size: Integer; Bold: Boolean);
var
  L: TNewStaticText;
begin
  L := TNewStaticText.Create(WizardForm);
  L.Parent := WizardForm.InstallingPage;
  L.Left := WizardForm.ProgressGauge.Left;
  L.Top := Top;
  L.Font.Size := Size;
  if Bold then L.Font.Style := [fsBold];
  L.Caption := Text;
  Top := Top + L.Height + ScaleY(6);
end;

{ 설치 중 화면에 주요 기능 소개 표시 }
procedure CreateInstallingFeatures;
var
  Top: Integer;
begin
  Top := WizardForm.ProgressGauge.Top + WizardForm.ProgressGauge.Height + ScaleY(28);
  AddInstallingLabel(CustomMessage('FeatureTitle'), Top, 11, True);
  AddInstallingLabel(CustomMessage('Feature1'), Top, 10, False);
  AddInstallingLabel(CustomMessage('Feature2'), Top, 10, False);
  AddInstallingLabel(CustomMessage('Feature3'), Top, 10, False);
end;

{ 시작 화면(wpWelcome) 다음에 옵션 페이지 추가 }
procedure CreateOptionPage;
begin
  OptionPage := CreateInputOptionPage(wpWelcome,
    CustomMessage('OptionTitle'), CustomMessage('OptionDesc'), '', False, False);
  OptionPage.Add(CustomMessage('OptionDesktop'));
  OptionPage.Add(CustomMessage('OptionStartMenu'));
  { 처음 설치는 둘 다 체크. 업데이트 때는 지난번에 고른 값을 그대로 씀
    (자동 업데이트는 이 페이지를 보여주지 않으므로, 지운 바로가기가 다시 생기지 않게) }
  OptionPage.Values[0] := GetPreviousData('DesktopIcon', '1') = '1';
  OptionPage.Values[1] := GetPreviousData('StartMenuIcon', '1') = '1';
end;

function BoolTo01(B: Boolean): String;
begin
  if B then Result := '1' else Result := '0';
end;

{ 설치할 때 고른 옵션을 저장해 두었다가 다음 설치(업데이트) 때 GetPreviousData 로 읽음 }
procedure RegisterPreviousData(PreviousDataKey: Integer);
begin
  SetPreviousData(PreviousDataKey, 'DesktopIcon', BoolTo01(OptionPage.Values[0]));
  SetPreviousData(PreviousDataKey, 'StartMenuIcon', BoolTo01(OptionPage.Values[1]));
end;

function WantDesktopIcon: Boolean;
begin
  Result := OptionPage.Values[0];
end;

function WantStartMenuIcon: Boolean;
begin
  Result := OptionPage.Values[1];
end;

{ 설치 창을 만들 때 한 번 호출됨: 직접 만든 화면은 여기서 불러야 나옴.
  Pascal은 위에 먼저 적힌 함수만 부를 수 있으므로 이 함수는 맨 아래에 둠 }
procedure InitializeWizard;
begin
  CreateOptionPage;
  CreateInstallingFeatures;
end;





