#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP_PORT="${APP_PORT:-8093}"
STORE="${LOCAL_STORE_DIR:-/tmp/aidetect-dotnet-store}"
MODEL="${DETECTOR_MODEL_PATH:-$ROOT/models/community_forensics_frontier_fp16.onnx}"
MODEL_REVISION="${DETECTOR_MODEL_REVISION:-16db135220b318d811b207db576d90368980b595}"
MODEL_URL="https://huggingface.co/Thermostatic/community-forensics-frontier-detector-2026-08/resolve/$MODEL_REVISION/community_forensics_frontier_fp16.onnx"
MODEL_SHA256="d75791ba2fa59146025d342cfaafa9ddeab24af117642a94f752ee4c1619375d"

export ASPNETCORE_URLS="http://127.0.0.1:$APP_PORT"
export STORE_BACKEND=file
export LOCAL_STORE_DIR="$STORE"
export AZURE_REGION="${AZURE_REGION:-local}"
export DETECTOR_CLASSIFIER="${DETECTOR_CLASSIFIER:-onnx}"
export DETECTOR_MODEL_PATH="$MODEL"

if [ "$DETECTOR_CLASSIFIER" = "onnx" ] && [ ! -s "$MODEL" ]; then
  echo "Downloading the pinned Community Forensics ONNX model (first run only)..."
  mkdir -p "$(dirname "$MODEL")"
  MODEL_TEMP="$MODEL.download.$$"
  trap 'rm -f "${MODEL_TEMP:-}"' EXIT
  curl --fail --location --retry 3 "$MODEL_URL" --output "$MODEL_TEMP"
  printf '%s  %s\n' "$MODEL_SHA256" "$MODEL_TEMP" | sha256sum --check --status
  mv "$MODEL_TEMP" "$MODEL"
  trap - EXIT
fi

if [ "$DETECTOR_CLASSIFIER" = "onnx" ]; then
  printf '%s  %s\n' "$MODEL_SHA256" "$MODEL" | sha256sum --check --status || {
    echo "The ONNX model at $MODEL does not match the pinned SHA-256." >&2
    exit 1
  }
fi

if [ "${RESET_LOCAL_STORE:-0}" = "1" ]; then
  rm -rf "$STORE"
fi
mkdir -p "$STORE"

cleanup() {
  kill "${APP_PID:-}" 2>/dev/null || true
}
trap cleanup EXIT

dotnet run --project "$ROOT/src/AiDetection.Web/AiDetection.Web.csproj" --no-launch-profile --urls "$ASPNETCORE_URLS" &
APP_PID=$!

for _ in $(seq 1 120); do
  if curl -sf "http://127.0.0.1:$APP_PORT/health" >/dev/null 2>&1; then
    break
  fi
  if ! kill -0 "$APP_PID" 2>/dev/null; then
    echo "The .NET application stopped before becoming ready." >&2
    wait "$APP_PID"
  fi
  sleep 1
done
curl -sf "http://127.0.0.1:$APP_PORT/health" >/dev/null

if [ "${SEED_SAMPLE:-1}" = "1" ] && [ ! -s "$STORE/detections.jsonl" ] && [ -f "$ROOT/samples/IMG_5615.jpeg" ]; then
  echo "Analysing samples/IMG_5615.jpeg..."
  curl --fail --silent --show-error -o /dev/null \
    -X POST "http://127.0.0.1:$APP_PORT/upload" \
    -F "file=@$ROOT/samples/IMG_5615.jpeg" \
    -F "subject=Local regression image" \
    -F "sender=local-demo"
fi
curl -sf "http://127.0.0.1:$APP_PORT/" >/dev/null

echo
echo "Results and upload page: http://127.0.0.1:$APP_PORT"
echo "Result store: $STORE"
echo "Classifier: $DETECTOR_CLASSIFIER"
if command -v "${C2PATOOL_PATH:-c2patool}" >/dev/null 2>&1; then
  echo "C2PA: enabled"
else
  echo "C2PA: c2patool not installed; classification still works and provenance will be reported as unavailable"
fi

if [ -n "${DEMO_SECONDS:-}" ]; then
  echo "Stopping after ${DEMO_SECONDS}s."
  sleep "$DEMO_SECONDS"
else
  echo "Press Ctrl+C to stop."
  wait "$APP_PID"
fi
