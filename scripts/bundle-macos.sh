#!/bin/bash
# GrinLauncher.app 번들을 만든다 (Dock 아이콘/메뉴바 이름은 .app 번들의 Info.plist +
# .icns에서만 나오고, dotnet run/dotnet build 결과물(맨 실행 파일)에는 안 붙는다).
set -euo pipefail
cd "$(dirname "$0")/.."

CONFIG="${1:-Debug}"
TFM="net10.0"
OUT="bin/$CONFIG/$TFM"
APP="bin/$CONFIG/GrinLauncher.app"
ICON_SRC="Assets/grin_square.png"

dotnet build -c "$CONFIG"

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$OUT/." "$APP/Contents/MacOS/"
chmod +x "$APP/Contents/MacOS/GrinLauncher"

# framework-dependent 빌드라 NuGet 패키지가 win/linux용 네이티브 런타임까지 전부
# runtimes/ 밑에 같이 복사해 넣는다(500MB+). macOS 앱에 불필요한 무게일 뿐 아니라,
# 이 안의 .dll/.so를 codesign이 서명 가능한 네이티브 코드로 오인해 서명 자체가
# 실패하는 원인이었다(전체 번들 서명 시 "code object is not signed at all"). osx만
# 남기고 지운다.
find "$APP/Contents/MacOS/runtimes" -mindepth 1 -maxdepth 1 ! -name osx -exec rm -rf {} +

if [ -f ".env" ]; then
  cp ".env" "$APP/Contents/MacOS/.env"
fi

ICONSET=$(mktemp -d)/AppIcon.iconset
mkdir -p "$ICONSET"
for size in 16 32 128 256 512; do
  sips -z "$size" "$size" "$ICON_SRC" --out "$ICONSET/icon_${size}x${size}.png" >/dev/null
  double=$((size * 2))
  sips -z "$double" "$double" "$ICON_SRC" --out "$ICONSET/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns"
rm -rf "$(dirname "$ICONSET")"

cat > "$APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>Grin Launcher</string>
    <key>CFBundleDisplayName</key>
    <string>Grin Launcher</string>
    <key>CFBundleIdentifier</key>
    <string>com.thisisyousam.grinlauncher</string>
    <key>CFBundleVersion</key>
    <string>1.0.0</string>
    <key>CFBundleShortVersionString</key>
    <string>1.0.0</string>
    <key>CFBundleExecutable</key>
    <string>GrinLauncher</string>
    <key>CFBundleIconFile</key>
    <string>AppIcon</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>LSMinimumSystemVersion</key>
    <string>11.0</string>
    <key>NSHighResolutionCapable</key>
    <true/>
</dict>
</plist>
PLIST

# 실행 파일에는 dotnet 빌드가 최소 ad-hoc 서명을 붙여주지만, 이후 여기서 추가한
# Info.plist/아이콘까지 포함한 번들 전체는 서명이 안 된 상태로 남는다. 서명 안 된
# 번들을 브라우저로 받으면(quarantine 플래그) Apple Silicon에서 "Unidentified
# Developer" 경고 대신 "손상되어 열 수 없음" 에러로 뜬다. Apple Developer ID가 없어
# 정식 서명/공증은 못 하지만, 번들 전체를 ad-hoc으로 다시 서명해두면 최소한 "손상됨"은
# 사라지고 "확인되지 않은 개발자" 경고(우클릭 열기로 우회 가능)로 바뀐다.
codesign --force --deep --sign - "$APP"

echo "빌드 완료: $APP"
