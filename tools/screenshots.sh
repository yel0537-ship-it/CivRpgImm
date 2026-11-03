#!/usr/bin/env bash
# Снимки для проверки читаемости: собирает Standalone и запускает сценарии.
#   UNITY_EDITOR=/путь/к/Unity tools/screenshots.sh [сценарий|all]
# Сценарии: menu hud dialogue combat smoke map ruler city chronicle end
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/tools/out/screenshots"; mkdir -p "$OUT"
SCENARIO="${1:-all}"
: "${UNITY_EDITOR:?Задайте UNITY_EDITOR — путь к Unity 6}"

case "$(uname -s)" in
  Linux*)  BUILD="$ROOT/Build/Immortal.x86_64"; TARGET=Linux64 ;;
  Darwin*) BUILD="$ROOT/Build/Immortal.app/Contents/MacOS/Immortal"; TARGET=OSXUniversal ;;
  *)       BUILD="$ROOT/Build/Immortal.exe"; TARGET=Win64 ;;
esac

echo "== Сборка ($TARGET) =="
"$UNITY_EDITOR" -batchmode -nographics -quit -projectPath "$ROOT" -buildTarget "$TARGET" \
  -executeMethod Immortal.EditorTools.BuildScript.BuildStandalone -logFile "$ROOT/tools/out/build.log"

echo "== Снимки: $SCENARIO =="
"$BUILD" -screen-width 1600 -screen-height 900 -screen-fullscreen 0 -logFile "$ROOT/tools/out/player.log" \
  -scenario "$SCENARIO" -shotdir "$OUT" -shot "$OUT/$SCENARIO.png"
ls -la "$OUT"
