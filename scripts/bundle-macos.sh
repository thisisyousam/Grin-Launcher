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

echo "빌드 완료: $APP"
