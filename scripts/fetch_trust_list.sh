#!/usr/bin/env bash
set -euo pipefail

URL="${1:-https://raw.githubusercontent.com/c2pa-org/conformance-public/refs/heads/main/trust-list/C2PA-TRUST-LIST.pem}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEST="$ROOT/detector/c2pa_trust/C2PA-TRUST-LIST.pem"

mkdir -p "$(dirname "$DEST")"
curl -fsSL -o "$DEST" "$URL"
echo "updated $(wc -c < "$DEST") bytes from $URL"
