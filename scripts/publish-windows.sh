#!/bin/bash
# Windows용 배포 빌드를 만든다. .NET이 안 깔린 PC에서도 바로 실행되도록
# self-contained 단일 exe로 퍼블리시하고, 배포하기 편하게 zip으로 묶는다.
set -euo pipefail
cd "$(dirname "$0")/.."

CONFIG="Release"
RID="win-x64"
OUT="publish/$RID"
ZIP="publish/GrinLauncher-$RID.zip"

rm -rf "$OUT"
dotnet publish -c "$CONFIG" -r "$RID" --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:DebugType=none \
  -o "$OUT"

# Skia/HarfBuzzSharp 등 네이티브 패키지가 자기 pdb를 content로 강제 복사해서
# DebugType=none으로도 안 빠진다 - 배포엔 필요 없으니 지운다.
find "$OUT" -iname "*.pdb" -delete

rm -f "$ZIP"
(cd "publish" && zip -r -q "$(basename "$ZIP")" "$RID")

echo "빌드 완료: $OUT"
echo "배포용 zip: $ZIP"
