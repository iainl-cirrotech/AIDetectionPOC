#!/usr/bin/env bash
set -euo pipefail

ACR="${1:?usage: build_push.sh <acr-name> [tag]}"
TAG="${2:-v1}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

az acr build --registry "$ACR" --image "ai-image-detector:$TAG" "$ROOT/detector"
az acr build --registry "$ACR" --image "ai-image-results:$TAG" "$ROOT/web"

echo "pushed ai-image-detector:$TAG and ai-image-results:$TAG to $ACR"
