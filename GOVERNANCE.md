# Governance and data handling

This document describes, for the specific architecture in this repository, how an emailed image
is handled and what can be stated to the client. Every claim below is tied to a service and a
deployment setting that exists in `infra/main.bicep`. Items that depend on the client's tenant or
subscription are listed explicitly under "Must be confirmed in-tenant".

## Purpose and scope

Prove two things only:

1. An image arriving by email can be automatically analysed for signs of AI generation or alteration.
2. That can be done with a data-handling story that can be explained and evidenced.

There is no business process in this solution: no recommendation, no return or fraud decision, no
case, no routing, no approval, no customer response.

## What is processed

- Email envelope and metadata: subject, sender address, received date/time, internet message id.
- The image attachment bytes, for the duration of the analysis.
- Derived artefacts: a SHA-256 of the image, a downscaled JPEG thumbnail (longest edge 512 px), a
  detection result record, and audit log entries.

## Data flow

```
 Exchange Online shared mailbox (M365 tenant, internal use)
        |  polled by Microsoft Office 365 Outlook connector (first-party connector)
        v
 Azure Logic App  <prefix>-mail-pickup            [Azure region, e.g. uksouth]
        |  HTTPS POST, API-key protected, image bytes + email metadata headers
        v
 Azure Container App  <prefix>-detector           [same Azure region]
        |  in-memory only: decode, SHA-256, C2PA verify, metadata read, classifier inference
        +--> Azure Blob Storage  thumbnails/       [private container, no public access]
        +--> Azure Table Storage detections        [result record, no image pixels]
        +--> Application Insights / Log Analytics  [audit event]
        v
 Azure Container App  <prefix>-web                [same Azure region]
        |  reads results and streams thumbnails via managed identity
        v
 Browser (results table)
```

The original image is decoded and analysed **in memory** and is not written to disk or to storage.
`PERSIST_ORIGINAL_IMAGE=false` is set on the detector container app. Only the thumbnail is stored.

## Step-by-step handling

| Step | Component | Where | Data present | Persisted |
|---|---|---|---|---|
| Mail arrives | Exchange Online | M365 tenant geography | Full email + attachment | In mailbox, per M365 retention |
| Mail polled | Logic App (`<prefix>-mail-pickup`) + Office 365 connector | Azure region | Email metadata + attachment bytes | Run history with secure inputs/outputs (payload masked and excluded from Log Analytics), retained 7 days |
| Analysis | Container App (`<prefix>-detector`) | Azure region | Image bytes in memory | Nothing (original not persisted) |
| Thumbnail | Blob container `thumbnails` | Azure Storage, region | Downscaled JPEG | Yes, deleted by lifecycle policy |
| Result | Table `detections` | Azure Storage, region | Metadata, score, evidence (no pixels) | Yes, removed by purge job |
| Display | Container App (`<prefix>-web`) | Azure region | Result + thumbnail | No additional storage |
| Audit | Application Insights + Log Analytics | Log Analytics workspace region | Processing events, hashes, model id | Yes, per workspace retention |

## Statements that can be made to the client

These follow from the selected services and settings, subject to the in-tenant confirmations below.

- **Used for model training?** No. The classifier is a frozen, pre-trained open-weights model used
  for inference only. No customer image is used to train or fine-tune any model, and there is no
  feedback loop. The model was trained by its publisher on public data, independently of the client.
  Azure Storage, Container Apps, Logic Apps and Azure Monitor do not use customer data to train
  Microsoft AI models.
- **Sent to an external or third-party model provider?** No. Inference runs inside the client's own
  Azure subscription. Model weights are downloaded from Hugging Face once at image build time and
  baked into the container image; the runtime sets `HF_HUB_OFFLINE=1` and `TRANSFORMERS_OFFLINE=1`,
  so no image or derived data is sent to Hugging Face or any external model API. C2PA verification
  runs locally using the official C2PA trust list, which is pinned into the detector image at build
  time, and the image is read from memory rather than a temporary file. No runtime egress to a model
  provider is required, and egress can be blocked.
- **Where processing takes place.** In the client's Azure subscription in the region chosen at
  deployment (`location`, default `uksouth`). Container Apps, Storage and Log Analytics are all
  created in that region. The mailbox itself is in the client's Microsoft 365 tenant.
- **Where data is stored.** Thumbnails in Azure Blob Storage (private container, no anonymous
  access) and result records in Azure Table Storage, both in the chosen region, encrypted at rest
  with Azure Storage service-side encryption and in transit with TLS 1.2 minimum. Customer-managed
  keys via Key Vault can be added. The original image is not stored.
- **Retention and deletion.** Thumbnails are deleted automatically by the storage lifecycle policy
  after `thumbnailRetentionDays` (default 7). Result records are removed by `scripts/purge_results.py`
  after `RESULTS_RETENTION_DAYS` (default 30). Log Analytics retention is `logsRetentionDays`
  (default 90). Logic App run history is retained for `logicAppRetentionDays` (default 7, the
  minimum), and the connector trigger and the call to the detector have secure inputs/outputs
  enabled so the attachment payload is masked in run history and is not sent to Log Analytics.
  Deleting the resource group removes all stored data.
- **Client control.** The client owns the subscription and resource groups. Access is via a
  user-assigned managed identity with least-privilege roles (`Storage Blob Data Contributor`,
  `Storage Table Data Contributor`, `AcrPull`) and no stored storage keys
  (`allowSharedKeyAccess: false`). The client can disable the Logic App, revoke the Office 365
  connection, rotate the detector API key, apply Azure Policy, add private endpoints and delete the
  resources at any time.
- **Audit and logging.** Every processed image writes a structured audit event containing the
  correlation id (from the email's internet message id), processing timestamp, image SHA-256, model
  name and version, band, region and whether a thumbnail was stored. Application Insights and Log
  Analytics hold these events; Azure Activity Log records control-plane changes; storage diagnostic
  logs record blob read/write/delete. Log retention and optional immutable/WORM retention are set on
  the workspace. Because the Logic App trigger and detector call are secured, the image payload is
  not written to Log Analytics through the Logic App.

## Must be confirmed in-tenant before these are stated externally

- The client's Microsoft 365 agreement/DPA and tenant settings covering Exchange Online and the
  Microsoft first-party Office 365 Outlook connector used by the Logic App. If the client requires
  no connector-mediated access, replace the Logic App connector with a Microsoft Graph change
  notification plus an Azure Function using an app registration with `Mail.Read` on the mailbox.
- The exact deployment region and that it satisfies the client's residency requirement.
- Whether customer-managed keys and private endpoints are required. The demo currently uses
  Microsoft-managed keys and public (but authenticated) endpoints; the detector and web Container
  Apps use external ingress with the detector protected by an API key. Hardening options:
  internal ingress, VNet integration, private endpoints, and Azure Front Door/WAF.
- Final retention values, and whether immutable logging is needed.
- The chosen model's licence and suitability. The default is
  `capcheck/ai-human-generated-image-detection` (Apache-2.0 lineage); confirm the licence and
  validate accuracy on representative images before the demo. The model is configurable via
  `DETECTOR_MODEL_ID` / `DETECTOR_MODEL_REVISION`.

## Detection limitations (state these alongside any result)

- Detection is probabilistic. The score is an indicator, not a determination.
- Classifier accuracy varies by generator and drops on models newer than its training data.
  False positives and false negatives are expected.
- Metadata can be stripped or forged; absence of EXIF is not evidence of AI generation.
- C2PA only helps when the creating tool embedded a manifest and it survived any re-encoding.
- Thresholds for Low/Medium/High are configurable and should be tuned and documented per use case.
- This is not a legal, forensic or identity determination.

## Out of scope

No customer service actions, returns or eligibility decisions, fraud decisions, suggested responses,
refunds, customer portal, case management, routing, approvals, ERP/Business Central integration,
automated business decisions or production-scale architecture.
