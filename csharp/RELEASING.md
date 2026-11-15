# 배포 방법 (태그 · version.json · 업데이트 알림)

## 1. 태그 이름 규칙

| 플랫폼 | 태그 | 설치 파일 이름 (권장) | 자동 설치 |
|---|---|---|---|
| Windows x64 | `9.1.0` | `Setup_9.1.0.exe` | ✅ |
| macOS (Apple Silicon) | `a_9.1.0` | `AtbashCipherTool-9.1.0-osx-arm64.dmg` | 아직 없음 (다운로드 페이지 열기) |
| Linux x64 | `l_9.1.0` | `AtbashCipherTool-9.1.0-x86_64.AppImage` | 아직 없음 (다운로드 페이지 열기) |
| Windows ARM64 | 정해지지 않음 | | `PlatformInfo.cs` 에 접두사를 정해서 추가 |

- 베타는 `9.1.0-beta`, `9.1.0-beta.2`, `a_9.1.0-beta` 처럼 뒤에 `-이름`을 붙이고, GitHub 에서 **Pre-release** 로 올립니다.
  정식 `9.1.0` 이 베타보다 새 버전으로 비교되므로, 베타를 쓰는 사람에게도 정식 버전 알림이 갑니다.
- **예전 Windows 태그 `v8.0.0`, `v8.1.0`, `v9.0.0` 은 바꾸지 마세요.** `version.json` 의 설치 파일 주소가 이 태그를 가리킵니다.
  새 Windows 태그부터는 `v` 를 붙이지 않습니다.
- 태그 이름은 `ReleaseTool tag --platform osx-arm64 --version 9.1.0` 으로 확인할 수 있습니다.

## 2. 앱은 태그가 아니라 version.json 을 읽습니다

`https://raw.githubusercontent.com/KoreaJava-53bulgogi/AtbashCipherTool/main/version.json` 을 하루에 한 번 읽습니다.
태그는 GitHub 릴리스를 구분하고 설치 파일 주소를 만드는 데만 쓰입니다.
(`version.json` 을 `main` 에 올리는 순간이 "사용자에게 알림이 나가는 순간"입니다.)

```json
{
  "version": "9.0.0",                       ← 맨 위는 항상 Windows x64 (이미 배포된 Windows 앱 8.x / 9.0.0 이 읽는 부분)
  "url": ".../releases/tag/v9.0.0",
  "installer": ".../releases/download/v9.0.0/Setup_9.0.0.exe",
  "sha256": "52329f53...",
  "notes": "사용자에게 보이는 변경 내용",
  "platforms": {
    "win-x64":   { "version": "9.0.0", "tag": "v9.0.0", "url": "...", "installer": "...", "sha256": "...", "notes": "..." },
    "osx-arm64": { "version": "9.1.0", "tag": "a_9.1.0", ... },
    "linux-x64": { "version": "9.1.0", "tag": "l_9.1.0", ... }
  }
}
```

지켜야 할 것 (테스트 `UpdateCompatibilityTests` 가 확인합니다):

1. **맨 위 항목은 Windows x64 만.** 예전 앱은 파일에서 "맨 처음 나오는 값"을 읽습니다. Mac/Linux 항목은 `platforms` 안에만 둡니다.
2. 그래서 **Mac/Linux 릴리스는 Windows 사용자에게 알림을 보내지 않습니다.**
3. 앱은 자기 플랫폼 항목만 봅니다. 항목이 아직 없으면 "최신 버전"으로 처리합니다.
4. 다운로드 페이지(`url`)는 그 릴리스의 태그 페이지입니다. `releases/latest` 는 쓰지 않습니다
   (다른 플랫폼 릴리스가 "최신"이 되면 엉뚱한 페이지가 열리기 때문).

## 3. 릴리스 순서 (플랫폼 공통)

1. 설치 파일을 빌드합니다. Windows 는 `csharp\release.bat` 입니다.
2. `ReleaseTool merge` 로 `version.json` 에 그 플랫폼 항목만 반영합니다. (`release.bat` 이 Windows 에 대해 자동으로 함)
   ```
   dotnet run --project csharp\tools\ReleaseTool -c Release -- merge --manifest version.json ^
       --platform osx-arm64 --version 9.1.0 --file AtbashCipherTool-9.1.0-osx-arm64.dmg [--notes "변경 내용"]
   ```
   도구가 **만들어야 할 태그, 올릴 파일 이름, "최신 릴리스" 체크 여부**를 알려줍니다.
3. GitHub 에서 **그 태그로 Release** 를 만들고 **설치 파일을 올립니다.** (다시 빌드하지 말고 그 파일 그대로)
   - **Windows:** `Set as the latest release` 체크 ✅
   - **Mac / Linux:** `Set as the latest release` **체크하지 않기** ❌ (베타는 Pre-release)
4. 올린 파일의 SHA-256 이 `version.json` 과 같은지 확인합니다.
5. `version.json` 을 커밋해서 `main` 에 올립니다. **← 이때부터 사용자에게 알림이 갑니다.**

## 3-1. macOS 릴리스는 자동입니다

1. `csharp/src/Atbash.App/Atbash.App.csproj` 의 `<Version>` 을 정합니다 (예: `9.1.0-beta.1`).
2. 그 커밋에 **`a_` 태그**를 붙여 올립니다: `git tag a_9.1.0-beta.1` → `git push origin a_9.1.0-beta.1`
3. GitHub Actions(`.github/workflows/release-macos.yml`)가 Mac 서버에서 알아서 합니다:
   태그와 버전이 맞는지 확인 → 테스트 → `.dmg` 만들기 → 실제로 켜 보기 → 릴리스 만들고 `.dmg` 올리기.
   버전에 `-beta` 같은 `-` 가 있으면 Pre-release, 어떤 경우에도 "Latest" 로 표시하지 않습니다.
4. `version.json` 은 건드리지 않습니다. 정식 Mac 출시 때만 `ReleaseTool merge --platform osx-arm64` 로 반영합니다.

받는 사람용 안내(처음 열 때 macOS 가 막는 문제 포함)는 Mac 베타 안내서를 함께 보냅니다.

## 4. 베타를 소수에게만 나눌 때 (예: Discord DM)

- 2번(`version.json`)과 5번을 **하지 않습니다.** Pre-release 로 올리고 링크만 DM 으로 보냅니다. 그러면 일반 사용자에게는 알림이 가지 않습니다.
- 베타를 쓰는 사람은 나중에 정식 버전을 `version.json` 에 올리면 알림을 받습니다.
- 모든 사용자에게 베타를 알리고 싶을 때만 `version.json` 에 베타 버전을 넣습니다.

## 5. Java 8.x 도구에 대한 주의

저장소 맨 위의 `release.bat`, `tools\make-version-json.ps1` 은 Java 8.x 용입니다.
**`version.json` 전체를 새로 쓰기 때문에 `platforms` 가 지워집니다.** Java 8.x 핫픽스를 낼 일이 생기면
이 도구 대신 `ReleaseTool merge` 를 쓰세요.
