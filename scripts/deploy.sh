#!/usr/bin/env bash
set -euo pipefail

RESOURCE_GROUP="${RESOURCE_GROUP:?set RESOURCE_GROUP}"
MAILBOX_ADDRESS="${MAILBOX_ADDRESS:?set MAILBOX_ADDRESS to the Microsoft 365 mailbox address}"
LOCATION="${LOCATION:-uksouth}"
PREFIX="${PREFIX:-aidetect}"
TAG="${TAG:-v1}"
DETECTOR_API_KEY="${DETECTOR_API_KEY:-$(openssl rand -hex 32)}"
PLACEHOLDER="mcr.microsoft.com/azuredocs/containerapps-helloworld:latest"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

command -v az >/dev/null 2>&1 || { echo "Azure CLI (az) is required" >&2; exit 1; }
az account show >/dev/null 2>&1 || { echo "Sign in first with: az login" >&2; exit 1; }
az group show --name "$RESOURCE_GROUP" >/dev/null 2>&1 || {
  echo "Resource group '$RESOURCE_GROUP' does not exist. Create it before deploying." >&2
  exit 1
}

deploy() {
  az deployment group create \
    --resource-group "$RESOURCE_GROUP" \
    --template-file "$ROOT/infra/main.bicep" \
    --parameters namePrefix="$PREFIX" location="$LOCATION" mailboxAddress="$MAILBOX_ADDRESS" \
      detectorApiKey="$DETECTOR_API_KEY" \
      appImage="$1" \
    --query 'properties.outputs' -o json
}

echo "Phase 1: create platform resources (placeholder images)..."
deploy "$PLACEHOLDER" >/dev/null

ACR_NAME="$(az acr list -g "$RESOURCE_GROUP" --query "[?starts_with(name, '$PREFIX')].name | [0]" -o tsv)"
LOGIN_SERVER="$(az acr show -n "$ACR_NAME" --query loginServer -o tsv)"
ACR_ID="$(az acr show -n "$ACR_NAME" --query id -o tsv)"
PRINCIPAL_ID="$(az identity show -g "$RESOURCE_GROUP" -n "$PREFIX-id" --query principalId -o tsv)"

echo "Waiting for the managed identity's AcrPull role to become visible..."
for attempt in {1..10}; do
  ROLE="$(az role assignment list \
    --scope "$ACR_ID" \
    --assignee-object-id "$PRINCIPAL_ID" \
    --query "[?roleDefinitionName=='AcrPull'].roleDefinitionName" \
    -o tsv 2>/dev/null || true)"

  if [[ "$ROLE" == "AcrPull" ]]; then
    echo "AcrPull role confirmed."
    break
  fi

  if [[ "$attempt" -eq 10 ]]; then
    echo "AcrPull role was not visible after five minutes; stop before deploying the private image." >&2
    exit 1
  fi

  echo "AcrPull is still propagating (attempt $attempt/10); retrying in 30 seconds..."
  sleep 30
done

echo "Phase 2: build images into $ACR_NAME..."
"$ROOT/scripts/build_push.sh" "$ACR_NAME" "$TAG"

echo "Phase 3: deploy the combined .NET application image..."
OUT="$(deploy "$LOGIN_SERVER/ai-image-detection:$TAG")"

echo "Phase 4: outputs"
echo "$OUT"

cat <<EOF

Next steps:
1. In the Azure portal, open the API connection '$PREFIX-office365' and authorise it as $MAILBOX_ADDRESS.
2. Enable the Logic App '$PREFIX-mail-pickup'.
3. Send a test email with an image attachment to $MAILBOX_ADDRESS and include [AI-CHECK] in the subject.
EOF
