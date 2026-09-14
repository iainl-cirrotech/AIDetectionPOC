#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VENV="${VENV:-$ROOT/.venv-local}"
STORE="${LOCAL_STORE_DIR:-/tmp/aidetect-store}"
DET_PORT="${DET_PORT:-8094}"
WEB_PORT="${WEB_PORT:-8093}"

export STORE_BACKEND=file
export LOCAL_STORE_DIR="$STORE"
export AZURE_REGION="${AZURE_REGION:-uksouth}"
export DETECTOR_CLASSIFIER="${DETECTOR_CLASSIFIER:-mock}"

cleanup() {
  kill "${DET_PID:-}" "${WEB_PID:-}" 2>/dev/null || true
}
trap cleanup EXIT

wait_for() {
  local url="$1" name="$2"
  for _ in $(seq 1 90); do
    if curl -sf "$url" >/dev/null 2>&1; then
      return 0
    fi
    sleep 1
  done
  echo "$name did not become ready at $url" >&2
  return 1
}

if [ ! -x "$VENV/bin/uvicorn" ]; then
  echo "creating local virtualenv..."
  python3 -m venv "$VENV"
  "$VENV/bin/pip" -q install --disable-pip-version-check -r "$ROOT/requirements-local.txt"
fi

rm -rf "$STORE"
mkdir -p "$STORE"

"$VENV/bin/uvicorn" app:app --app-dir "$ROOT/detector" --host 127.0.0.1 --port "$DET_PORT" --log-level warning &
DET_PID=$!
"$VENV/bin/uvicorn" app:app --app-dir "$ROOT/web" --host 127.0.0.1 --port "$WEB_PORT" --log-level warning &
WEB_PID=$!

wait_for "http://127.0.0.1:$DET_PORT/health" "detector"
wait_for "http://127.0.0.1:$WEB_PORT/health" "web"

"$VENV/bin/python" "$ROOT/scripts/make_sample_images.py"

post() {
  curl -s -o /dev/null -w "detector http=%{http_code} subject='$2'\n" \
    -X POST "http://127.0.0.1:$DET_PORT/analyze" \
    -F "file=@$1" \
    -H "x-email-subject: $2" \
    -H "x-email-sender: $3" \
    -H "x-email-received: $4"
}

post "$ROOT/samples/sample-plain.png" "Holiday photo" "alice@example.com" "2026-09-14T10:00:00Z"
post "$ROOT/samples/sample-edited.jpg" "Product image" "bob@example.com" "2026-09-14T11:00:00Z"

echo
echo "Results page: http://127.0.0.1:$WEB_PORT"
if [ "$DETECTOR_CLASSIFIER" = "mock" ]; then
  echo "Classifier mode: mock (deterministic placeholder, NOT real detection)"
else
  echo "Classifier mode: $DETECTOR_CLASSIFIER (real model inference on CPU)"
fi
if [ -n "${DEMO_SECONDS:-}" ]; then
  echo "Stopping after ${DEMO_SECONDS}s."
  sleep "$DEMO_SECONDS"
else
  echo "Press Ctrl+C to stop."
  wait
fi
