#!/usr/bin/env bash
# Проверки проекта Immortal.
#   tools/check.sh          — компиляция в Unity + EditMode-тесты (нужен UNITY_EDITOR=/путь/к/Unity)
#   tools/check.sh --core   — только ядро и тесты через .NET SDK, без Unity (быстро, для CI)
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/tools/out"; mkdir -p "$OUT"

if [[ "${1:-}" == "--core" ]]; then
  export IMMORTAL_DATA="$ROOT/Assets/Resources/Data"
  dotnet run -c Release --project "$ROOT/tools/dotnet/CoreTests.csproj" -- --noresult --labels=Off
  exit $?
fi

: "${UNITY_EDITOR:?Задайте UNITY_EDITOR — путь к исполняемому файлу Unity 6 (например, /opt/Unity/Editor/Unity)}"

echo "== Компиляция =="
"$UNITY_EDITOR" -batchmode -nographics -quit -projectPath "$ROOT" -logFile "$OUT/compile.log" \
  -executeMethod Immortal.EditorTools.BuildScript.ValidateContent || { echo "Компиляция/валидация упала, см. $OUT/compile.log"; grep -E "error CS|\[Контент\]" "$OUT/compile.log" | head -40; exit 1; }
echo "OK"

echo "== EditMode-тесты =="
# Важно: без -quit, иначе раннер тестов не успеет отработать.
"$UNITY_EDITOR" -batchmode -nographics -runTests -testPlatform editmode -testResults "$OUT/tests.xml" \
  -projectPath "$ROOT" -logFile "$OUT/tests.log" || true
if grep -q 'result="Passed"' "$OUT/tests.xml" 2>/dev/null && ! grep -q 'result="Failed"' "$OUT/tests.xml"; then
  echo "Все тесты зелёные ($OUT/tests.xml)"
else
  echo "Есть упавшие тесты, см. $OUT/tests.xml и $OUT/tests.log"; exit 1
fi
