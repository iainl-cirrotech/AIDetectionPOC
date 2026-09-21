#!/usr/bin/env bash
set -euo pipefail

RESOURCE_GROUP="${RESOURCE_GROUP:?set RESOURCE_GROUP}"
SHARED_MAILBOX="${SHARED_MAILBOX:?set SHARED_MAILBOX to the internal-use mailbox address}"
LOCATION="${LOCATION:-uksouth}"
PREFIX="${PREFIX:-aidetect}"
TAG="${TAG:-v1}"
PLACEHOLDER="mcr.microsoft.com/azuredocs/containerapps-helloworld:latest"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

deploy() {
  az deployment group create \
    --resource-group "$RESOURCE_GROUP" \
    --template-file "$ROOT/infra/main.bicep" \
    --parameters namePrefix="$PREFIX" location="$LOCATION" sharedMailboxAddress="$SHARED_MAILBOX" \
      appImage="$1" \
    --query 'properties.outputs' -o json
}

echo "Phase 1: create platform resources (placeholder images)..."
deploy "$PLACEHOLDER" >/dev/null

ACR_NAME="$(az acr list -g "$RESOURCE_GROUP" --query "[?starts_with(name, '$PREFIX')].name | [0]" -o tsv)"
LOGIN_SERVER="$(az acr show -n "$ACR_NAME" --query loginServer -o tsv)"

echo "Phase 2: build images into $ACR_NAME..."
"$ROOT/scripts/build_push.sh" "$ACR_NAME" "$TAG"

echo "Phase 3: deploy the combined .NET application image..."
OUT="$(deploy "$LOGIN_SERVER/ai-image-detection:$TAG")"

echo "Phase 4: outputs"
echo "$OUT"

cat <<EOF

Next steps:
1. In the Azure portal, open the API connection '$PREFIX-office365' and authorise it with the account that can read the shared mailbox.
2. Enable the Logic App '$PREFIX-mail-pickup' and confirm the shared mailbox address.
3. Send a test email with an image attachment to $SHARED_MAILBOX.
EOF
