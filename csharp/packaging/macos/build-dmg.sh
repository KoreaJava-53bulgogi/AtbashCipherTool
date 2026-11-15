#!/usr/bin/env bash
# macOS (Apple Silicon) 용 .app 과 .dmg 를 만듭니다. macOS 에서만 실행할 수 있습니다.
#
#   csharp/packaging/macos/build-dmg.sh [출력 폴더]
#
# 결과: AtbashCipherTool-<버전>-osx-arm64.dmg  (+ 같은 이름의 .sha256)
# 필요: macOS 13+, Xcode Command Line Tools (swiftc, codesign, hdiutil, sips, iconutil), .NET SDK
#
# Apple 개발자 계정이 없으므로 "임시 서명(ad-hoc)"만 합니다. 처음 열 때 macOS 가 막으므로
# 받는 사람은 csharp/packaging/macos/TESTERS.md 의 방법으로 한 번 허용해야 합니다.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
csharp="$(cd "$here/../.." && pwd)"
out="${1:-$here/out}"
mkdir -p "$out"
out="$(cd "$out" && pwd)"

rid=osx-arm64
version="$(dotnet msbuild "$csharp/src/Atbash.App/Atbash.App.csproj" -getProperty:Version -nologo | tr -d '[:space:]')"
build="${version%%-*}" # CFBundleVersion 은 숫자만 (9.1.0-beta → 9.1.0)
name="AtbashCipherTool-$version-$rid.dmg"
echo "Building $name"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
app="$work/dmg/Atbash Cipher Tool.app"
macos="$app/Contents/MacOS"
resources="$app/Contents/Resources"
mkdir -p "$macos" "$resources"

# 1) 앱 (.NET 포함)
dotnet publish "$csharp/src/Atbash.App/Atbash.App.csproj" -c Release -r "$rid" -o "$macos" --nologo
test -x "$macos/AtbashCipherTool"
# macOS 는 Apple Vision 으로 글자를 읽으므로 Tesseract 데이터는 필요 없음
rm -rf "$macos/tessdata"

# 2) Apple Vision OCR 도우미
swiftc -O -target arm64-apple-macos13.0 "$here/VisionOcr.swift" -o "$macos/atbash-vision-ocr"

# 3) 앱 정보와 아이콘
sed -e "s/@VERSION@/$version/" -e "s/@BUILD@/$build/" "$here/Info.plist" > "$app/Contents/Info.plist"
plutil -lint "$app/Contents/Info.plist"
iconset="$work/AtbashCipherTool.iconset"
mkdir -p "$iconset"
png="$csharp/packaging/linux/atbash-cipher-tool.png" # 256x256
for size in 16 32 64 128 256; do
  sips -z "$size" "$size" "$png" --out "$iconset/icon_${size}x${size}.png" > /dev/null
done
for size in 16 32 128; do
  cp "$iconset/icon_$((size * 2))x$((size * 2)).png" "$iconset/icon_${size}x${size}@2x.png"
done
rm -f "$iconset/icon_64x64.png"
iconutil -c icns "$iconset" -o "$resources/AtbashCipherTool.icns"

# 4) 임시 서명 (Apple Silicon 은 서명이 없으면 실행 자체가 안 됨)
codesign --force --deep --sign - "$app"
codesign --verify --deep --strict "$app"

# 5) .dmg: 앱 + "응용 프로그램" 폴더 바로가기 (끌어다 놓아 설치)
ln -s /Applications "$work/dmg/Applications"
rm -f "$out/$name"
hdiutil create -volname "Atbash Cipher Tool" -srcfolder "$work/dmg" -ov -format UDZO "$out/$name" > /dev/null

(cd "$out" && shasum -a 256 "$name" > "$name.sha256")
echo "Done: $out/$name"
cat "$out/$name.sha256"
