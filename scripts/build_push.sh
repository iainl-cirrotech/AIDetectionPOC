#!/usr/bin/env bash
set -euo pipefail

ACR="${1:?usage: build_push.sh <acr-name> [tag]}"
TAG="${2:-v1}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

az acr build --registry "$ACR" --image "ai-image-detection:$TAG" \
  --file src/AiDetection.Web/Dockerfile "$ROOT"

echo "pushed ai-image-detection:$TAG to $ACR"
