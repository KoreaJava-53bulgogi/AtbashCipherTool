#!/usr/bin/env bash
# Linux 용 AppImage 를 만듭니다 (파일 하나로 실행되는 Linux 배포 형식).
#
#   csharp/packaging/linux/build-appimage.sh [출력 폴더]
#
# 결과: AtbashCipherTool-<버전>-x86_64.AppImage  (+ 같은 이름의 .sha256)
# 필요: Linux x64, .NET SDK (csharp/global.json), curl
# 만드는 도구(appimagetool)와 실행기(runtime)는 버전을 고정하고 SHA-256 을 확인한 뒤에만 씁니다.
set -euo pipefail

APPIMAGETOOL_URL="https://github.com/AppImage/appimagetool/releases/download/1.9.1/appimagetool-x86_64.AppImage"
APPIMAGETOOL_SHA256="ed4ce84f0d9caff66f50bcca6ff6f35aae54ce8135408b3fa33abfc3cb384eb0"
RUNTIME_URL="https://github.com/AppImage/type2-runtime/releases/download/20251108/runtime-x86_64"
RUNTIME_SHA256="2fca8b443c92510f1483a883f60061ad09b46b978b2631c807cd873a47ec260d"

here="$(cd "$(dirname "$0")" && pwd)"
csharp="$(cd "$here/../.." && pwd)"
out="${1:-$here/out}"
mkdir -p "$out"
out="$(cd "$out" && pwd)"

version="$(dotnet msbuild "$csharp/src/Atbash.App/Atbash.App.csproj" -getProperty:Version -nologo | tr -d '[:space:]')"
name="AtbashCipherTool-$version-x86_64.AppImage"
echo "Building $name"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
appdir="$work/AtbashCipherTool.AppDir"
lib="$appdir/usr/lib/atbash-cipher-tool"

# 1) 앱 (.NET 포함)
dotnet publish "$csharp/src/Atbash.App/Atbash.App.csproj" -c Release -r linux-x64 -o "$lib" --nologo
test -x "$lib/AtbashCipherTool"
test -f "$lib/tessdata/kor.traineddata"

# 2) AppImage 구조: 실행 진입점, 바탕화면 항목, 아이콘
install -m 755 "$here/AppRun" "$appdir/AppRun"
install -m 644 "$here/atbash-cipher-tool.desktop" "$appdir/atbash-cipher-tool.desktop"
install -m 644 "$here/atbash-cipher-tool.png" "$appdir/atbash-cipher-tool.png"
install -D -m 644 "$here/atbash-cipher-tool.desktop" "$appdir/usr/share/applications/atbash-cipher-tool.desktop"
install -D -m 644 "$here/atbash-cipher-tool.png" "$appdir/usr/share/icons/hicolor/256x256/apps/atbash-cipher-tool.png"
ln -s atbash-cipher-tool.png "$appdir/.DirIcon"

# 3) 고정 버전의 도구를 받아 지문 확인
fetch() { # url sha256 file
  curl -fsSL --retry 3 -o "$3" "$1"
  echo "$2  $3" | sha256sum -c -
}
fetch "$APPIMAGETOOL_URL" "$APPIMAGETOOL_SHA256" "$work/appimagetool"
fetch "$RUNTIME_URL" "$RUNTIME_SHA256" "$work/runtime"
chmod +x "$work/appimagetool"

# 4) 묶기 (FUSE 가 없는 빌드 서버에서도 되게 도구를 풀어서 실행)
rm -f "$out/$name"
ARCH=x86_64 APPIMAGE_EXTRACT_AND_RUN=1 "$work/appimagetool" --no-appstream --runtime-file "$work/runtime" "$appdir" "$out/$name"

(cd "$out" && sha256sum "$name" > "$name.sha256")
echo "Done: $out/$name"
cat "$out/$name.sha256"
