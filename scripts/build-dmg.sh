#!/bin/bash
# GrinLauncher.app을 만들고, Applications 폴더로 드래그 설치하는 표준 dmg로 감싼다.
set -euo pipefail
cd "$(dirname "$0")/.."

CONFIG="${1:-Release}"
APP="bin/$CONFIG/GrinLauncher.app"
DMG="publish/GrinLauncher.dmg"

./scripts/bundle-macos.sh "$CONFIG"

STAGE=$(mktemp -d)
cp -R "$APP" "$STAGE/GrinLauncher.app"
ln -s /Applications "$STAGE/Applications"

mkdir -p publish
rm -f "$DMG"
hdiutil create -volname "Grin Launcher" -srcfolder "$STAGE" -ov -format UDZO "$DMG"
rm -rf "$STAGE"

echo "빌드 완료: $DMG"
