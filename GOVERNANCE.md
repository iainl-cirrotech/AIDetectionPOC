# Governance and data handling

This document describes, for the specific architecture in this repository, how an emailed image
is handled and what can be stated to the client. Every claim below is tied to a service and a
deployment setting that exists in `infra/main.bicep`. Items that depend on the client's tenant or
subscription are listed explicitly under "Must be confirmed in-tenant".

## Purpose and scope

Prove two things only:

1. An image arriving by email can be automatically analysed for signs of AI generation or alteration.
2. That can be done with a data-handling story that can be explained and evidenced.

There is no business decision process in this solution: no recommendation, return or fraud decision,
case, routing, or approval. The only outbound action is a fixed-format informational email containing
the screening result and its limitations.

## What is processed

- Email envelope and metadata: subject, sender address, received date/time, internet message id.
- The image attachment bytes, for the duration of the analysis.
- Derived artefacts: a SHA-256 of the image, a downscaled JPEG thumbnail (longest edge 512 px), a
  detection result record, and audit log entries.
- The recipient address and fixed-format result summary used for the outbound response email.

## Data flow

```
 Exchange Online POC mailbox (M365 tenant)
        |  polled by Microsoft Office 365 Outlook connector (first-party connector)
        v
 Azure Logic App  <prefix>-mail-pickup            [Azure region, e.g. uksouth]
        |  HTTPS POST, API-key protected, image bytes + email metadata headers
        v
 Azure Container App  <prefix>-detector           [same Azure region]
        |  decode, SHA-256, C2PA verify, metadata read, ONNX classifier inference
        +--> Azure Blob Storage  thumbnails/       [private container, no public access]
        +--> Azure Table Storage detections        [result record, no image pixels]
        +--> Application Insights / Log Analytics  [audit event]
        +--> Browser (results table served by the same Container App)
        |
        v
 Azure Logic App -> Office 365 Outlook connector -> original sender (result summary)
```

The original image is decoded and classified **in memory** and is not written to durable result
storage. Because the official `c2patool` interface accepts a file path, provenance verification uses
a randomly named operating-system temporary file which is deleted immediately in a `finally` block.
Only the thumbnail is retained by the application.

## Step-by-step handling

| Step | Component | Where | Data present | Persisted |
|---|---|---|---|---|
| Mail arrives | Exchange Online | M365 tenant geography | Full email + attachment | In mailbox, per M365 retention |
| Mail polled | Logic App (`<prefix>-mail-pickup`) + Office 365 connector | Azure region | Email metadata + attachment bytes | Run history with secure inputs/outputs (payload masked and excluded from Log Analytics), retained 7 days |
| Analysis | Container App (`<prefix>-detector`) | Azure region | Image bytes in memory and transient C2PA temp file | Nothing (original not retained) |
| Thumbnail | Blob container `thumbnails` | Azure Storage, region | Downscaled JPEG | Yes, deleted by lifecycle policy |
| Result | Table `detections` | Azure Storage, region | Metadata, score, evidence (no pixels) | Yes, removed by purge job |
| Display | Container App (`<prefix>-detector`) | Azure region | Result + thumbnail | No additional storage |
| Response | Logic App + Office 365 connector | Azure region / M365 tenant | Sender address and fixed-format screening summary; no attachment | Sent-mail and mailbox retention; workflow inputs/outputs masked |
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
  baked into the container image, so no image or derived data is sent to Hugging Face or any external
  model API. C2PA verification runs locally using `c2patool` and the official C2PA trust list pinned
  into the image; it uses a short-lived operating-system temporary file which is deleted immediately.
  No runtime egress to a model provider is required, and egress can be blocked.
- **Where processing takes place.** In the client's Azure subscription in the region chosen at
  deployment (`location`, default `uksouth`). Container Apps, Storage and Log Analytics are all
  created in that region. The mailbox itself is in the client's Microsoft 365 tenant.
- **Where data is stored.** Thumbnails in Azure Blob Storage (private container, no anonymous
  access) and result records in Azure Table Storage, both in the chosen region, encrypted at rest
  with Azure Storage service-side encryption and in transit with TLS 1.2 minimum. Customer-managed
  keys via Key Vault can be added. The original image is not stored.
- **Retention and deletion.** Thumbnails are deleted automatically by the storage lifecycle policy
  after `thumbnailRetentionDays` (default 7). The .NET application removes result records after
  `RESULTS_RETENTION_DAYS` (default 30). Log Analytics retention is `logsRetentionDays`
  (default 90). Logic App run history is retained for `logicAppRetentionDays` (default 7, the
  minimum), and the connector trigger, detector call and response-email action have secure
  inputs/outputs enabled so their payloads are masked in run history and are not sent to Log
  Analytics. Deleting the resource group removes the Azure-hosted thumbnails, result records and
  telemetry; it does not remove the original mailbox item or already-sent result email, which remain
  subject to Microsoft 365 and recipient-mailbox retention.
- **Client control.** The client owns the subscription and resource groups. Access is via a
  user-assigned managed identity with least-privilege roles (`Storage Blob Data Contributor`,
  `Storage Table Data Contributor`, `AcrPull`) and no stored storage keys
  (`allowSharedKeyAccess: false`). The client can disable the Logic App, revoke the Office 365
  connection, rotate the detector API key, apply Azure Policy, add private endpoints and delete the
  resources at any time.
- **Audit and logging.** Every processed image writes a structured audit event containing the
  unique per-attachment correlation id, processing timestamp, image SHA-256, model
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
- The Office 365 connection is authorised as the configured POC mailbox account. The trigger only
  processes messages whose subject contains `[AI-CHECK]`; this filter and the account's connector
  access should be reviewed before any use beyond the POC.
- The exact deployment region and that it satisfies the client's residency requirement.
- Whether customer-managed keys and private endpoints are required. The demo currently uses
  Microsoft-managed keys and public endpoints; the combined Container App uses external ingress and
  the analysis API is protected by an API key. Browser upload is disabled in Azure. Hardening options:
  internal ingress, VNet integration, private endpoints, and Azure Front Door/WAF.
- Final retention values, and whether immutable logging is needed.
- The chosen model's licence and suitability. The default is the MIT-licensed
  `Thermostatic/community-forensics-frontier-detector-2026-08`, an independently fine-tuned
  Community Forensics checkpoint with published calibration and robustness reports. Its own report
  says that three robustness gates failed and that it remains weak on very small synthetic regions,
  very low resolutions and some heavily laundered images. Dataset terms remain source-specific.
  Validate it on representative genuine and generated claim images before the demo. The model is
  configurable via `DETECTOR_MODEL_ID` / `DETECTOR_MODEL_REVISION`.

## Detection limitations (state these alongside any result)

- Detection is probabilistic. The score is an indicator, not a determination.
- Classifier accuracy varies by generator and drops on models newer than its training data.
  False positives and false negatives are expected.
- Metadata can be stripped or forged; absence of EXIF is not evidence of AI generation.
- C2PA only helps when the creating tool embedded a manifest and it survived any re-encoding.
- Thresholds for Low/Medium/High are configurable and should be tuned and documented per use case.
- "Low indication" does not mean that an image is certified real. Medium and High results should be
  treated as review signals, not automatic fraud findings.
- This is not a legal, forensic or identity determination.

## Out of scope

No free-form customer service actions, returns or eligibility decisions, fraud decisions, refunds,
customer portal, case management, routing, approvals, ERP/Business Central integration, automated
business decisions or production-scale architecture. The fixed-format screening email is explicitly
in scope but must not be presented as a claim decision.
