# Solution components and Azure monthly cost estimate

## Executive summary

This solution receives image attachments from a Microsoft 365 shared mailbox, submits each image to
an ASP.NET Core application hosted in Azure Container Apps, runs a local ONNX classifier plus C2PA
and metadata checks, stores a thumbnail and result record, and emails a screening summary to the
sender.

For **10 qualifying emails per day, with one image per email**, the estimated Azure run cost of the
deployment as currently configured is:

> **Approximately £29.30 per 30-day month, excluding VAT**

The estimate uses UK South pay-as-you-go retail pricing and the default Bicep configuration, including
one continuously warm Container Apps replica (`appMinReplicas = 1`). It is an estimate rather than a
quote. Actual invoices depend on the subscription agreement, attachment size, telemetry volume,
retries, shared free allowances, and the exchange rate used by Microsoft for the billing month.

The largest estimated charges are:

1. Azure Container Apps: **£21.51/month**
2. Azure Logic Apps Consumption: **£4.08/month**
3. Azure Container Registry Basic: **£3.68/month**
4. Storage, monitoring, identity, and other deployed resources: **about £0.01/month** under the
   stated workload and available free allowances

No Azure-hosted generative-AI model is called. The Community Forensics ONNX model and `c2patool` are
included in the application container, so there is no Azure OpenAI, Azure AI Foundry, or other
per-image model-inference charge.

## Scope and assumptions

| Item | Assumption used |
|---|---:|
| Azure region | UK South |
| Purchase model | Pay as you go; no reservation or savings plan |
| Billing month | 30 days / 720 hours |
| Emails | 10 qualifying emails/day = 300/month |
| Images | One image/email = 300 images/month |
| Average source image | 5 MB; the application limit is 20 MB |
| Processing time | 30 seconds of active container time/image (conservative allowance) |
| Logic App polling | Once per minute = 43,200 polls/month |
| Reply messages | One result email per successfully processed email |
| Container scale | Default `minReplicas: 1`, `maxReplicas: 3`; one replica assumed |
| Container resources | 1 vCPU and 2 GiB RAM |
| Thumbnail retention | 7 days |
| Result-record retention | 30 days |
| Logic App run-history retention | 7 days |
| Log Analytics retention | 90 days |
| Failures and retries | None included |
| Traffic | Normal service-to-service traffic within UK South; negligible public dashboard traffic |
| Tax | **VAT is excluded** |

Free allowances are shared, not dedicated to this solution. The headline assumes that the relevant
Container Apps and Logic Apps monthly grants and the first 5 GB/month Azure Monitor allowance have
not already been consumed by other workloads in the same subscription or billing account.

## End-to-end processing flow

```text
Sender
  -> Microsoft 365 shared mailbox
  -> Office 365 Outlook managed API connection
  -> Logic App Consumption workflow (polls every minute)
       -> checks subject, sender, attachment and image content type
       -> HTTP POST of image bytes to /analyze
  -> Azure Container App
       -> validates and decodes the image
       -> extracts metadata indicators
       -> verifies C2PA Content Credentials with c2patool
       -> runs the bundled Community Forensics ONNX classifier on CPU
       -> creates a 512 px JPEG thumbnail
       -> writes thumbnail to Blob Storage
       -> writes audit/result data to Table Storage
       -> emits application telemetry
  -> Logic App sends one result-summary email to the original sender
  -> Public Container Apps endpoint serves the results dashboard and thumbnails
```

Only a downscaled thumbnail and the result/audit record are retained. The original attachment is
processed in memory, with a temporary file used by `c2patool`, and is not deliberately persisted by
the detector application. The Logic App may retain attachment-bearing trigger/action data in its run
history for the configured seven days.

## Application and repository components

| Component | Location | Responsibility |
|---|---|---|
| ASP.NET Core application | `src/AiDetection.Web/` | API, results dashboard, orchestration, validation, and retention worker |
| ONNX classifier | `ImageClassifier.cs` and model embedded during the container build | CPU inference and calibrated AI-generation probability |
| C2PA verifier | `C2paVerifier.cs` plus bundled `c2patool` and trust list | Cryptographic provenance inspection |
| Metadata analyser | `MetadataAnalyzer.cs` | EXIF/XMP, camera/software, and bounded byte-marker indicators |
| Result store | `ResultStore.cs` | Azure Blob and Table Storage access through managed identity |
| Logic App workflow | `logicapp/workflow.json` | Shared-mailbox trigger, attachment loop, detector call, and reply email |
| Azure infrastructure | `infra/main.bicep` | All deployed Azure resources, roles, configuration, and retention settings |
| Deployment automation | `scripts/deploy.sh`, `scripts/build_push.sh` | Two-phase infrastructure deployment and ACR remote image build |
| Legacy implementation | `detector/`, `web/` | Retained Python migration reference; not deployed by the current Bicep path |

## Azure components created by the deployment

### Compute and hosting

| Azure resource | Configuration | Purpose | Direct charge |
|---|---|---|---:|
| Azure Container Apps managed environment | Consumption environment, UK South, logs sent to Log Analytics | Hosts and scales the application | No separate environment fee in this configuration |
| Azure Container App | 1 vCPU, 2 GiB, external HTTPS ingress, min 1/max 3 replicas | Runs API, dashboard, ONNX inference, C2PA checks, and retention worker | Yes, active/idle resource seconds and requests |
| Azure Container Registry | Basic, UK South | Stores the private application image and supports the remote deployment build | Yes, fixed daily charge; first 10 GiB storage included |

The image build performed by `az acr build` creates a small one-off ACR Tasks compute charge on each
deployment. It is a deployment cost, not a recurring monthly running cost, and is not included in the
headline total.

### Workflow and email integration

| Azure resource | Configuration | Purpose | Direct charge |
|---|---|---|---:|
| Logic App | Multitenant Consumption workflow; disabled immediately after deployment; 7-day run history | Polls the mailbox, calls the detector, and sends the result | Yes, executions and retained run data |
| Office 365 Outlook API connection | Shared-mailbox connector requiring interactive authorization | Supplies the email trigger and send-mail action | No fixed resource fee; connector executions are billed through Logic Apps |

The workflow must be authorized and enabled after deployment. Its trigger polls every minute. Azure
meters a polling managed-connector trigger even when it finds no email, which makes polling frequency
more important to cost than the number of received messages at this volume.

### Data storage

| Azure resource | Configuration | Purpose | Direct charge |
|---|---|---|---:|
| Storage account | StorageV2, Standard LRS, Hot, shared-key access disabled | Parent account for thumbnails and records | Usage based |
| Blob container `thumbnails` | Private; lifecycle deletion after 7 days | Stores 512 px JPEG thumbnails | Capacity and operations |
| Table `detections` | Table Storage | Stores result and audit fields | Capacity and operations |
| Storage lifecycle policy | Deletes old thumbnail blobs | Enforces thumbnail retention | No separate fee; generated storage operations can be charged |
| Blob diagnostic setting | Read/write/delete categories to Log Analytics | Storage audit telemetry | Log ingestion charge, subject to allowance |

The application retention worker scans Table Storage daily and deletes result records older than 30
days. Blob expiry and table-record expiry are therefore implemented by different components.

### Monitoring and telemetry

| Azure resource | Configuration | Purpose | Direct charge |
|---|---|---|---:|
| Log Analytics workspace | Pay-as-you-go (`PerGB2018`), 90-day workspace retention | Receives Container Apps and storage logs and backs Application Insights | Ingestion and chargeable retention, subject to allowances |
| Workspace-based Application Insights | Connected to the Log Analytics workspace | ASP.NET Core request, dependency, trace, and exception telemetry | Billed through Log Analytics |

At this workload, expected telemetry is comfortably below the default 5 GB/month Log Analytics
allowance if that allowance is available. Workspace-based Application Insights data includes 90 days
of retention; other Analytics log tables normally include 31 days, so the workspace's 90-day setting
can create a small extended-retention charge for non-Application-Insights logs.

### Identity, access, and configuration resources

| Azure resource | Purpose | Direct charge |
|---|---|---:|
| User-assigned managed identity | Authenticates the Container App to ACR, Blob Storage, and Table Storage | None |
| `AcrPull` role assignment | Permits private image pulls | None |
| Storage Blob Data Contributor role assignment | Permits thumbnail operations | None |
| Storage Table Data Contributor role assignment | Permits result-record operations | None |
| Container Apps secret | Holds the detector API key | No separate charge |

No Key Vault, NAT Gateway, private endpoint, VNet, Front Door, Application Gateway, database, Azure
OpenAI deployment, or GPU resource is created by `infra/main.bicep`.

## Monthly cost calculation

### Cost breakdown

| Service | Monthly usage and calculation | Estimated monthly cost |
|---|---|---:|
| Azure Container Apps | One warm 1-vCPU/2-GiB replica for 2,592,000 seconds; mostly idle, plus 9,000 active seconds; applicable monthly grants deducted | **£21.51** |
| Azure Container Registry Basic | £0.1227/day x 30 days; image storage assumed below included 10 GiB | **£3.68** |
| Logic Apps connector executions | 43,200 Outlook trigger polls + 300 send-mail actions = 43,500 Standard connector executions | **£4.00** |
| Logic Apps built-in operations | About 2,100 executions (conditions, loop, HTTP, variable operations), below the shared 4,000/month grant | **£0.00** |
| Logic Apps retained run data | Approximately 0.8 GB-month allowance for seven days of attachment-bearing run history at 5 MB/image | **£0.08** |
| Blob and Table Storage | Roughly 7 MB steady-state thumbnails, about 1.5 MB result data, and low transaction counts | **£0.01** |
| Log Analytics and Application Insights | Assumed about 0.1 GB/month and within the shared first 5 GB/month allowance | **£0.00** |
| Managed identity, RBAC, API connection, lifecycle policy, diagnostic setting | No fixed charge | **£0.00** |
| Same-region transfer and Container Apps requests | 300 detector calls and low dashboard traffic; under 2 million request grant; same-region service traffic | **£0.00** |
| **Estimated total** | | **£29.28, rounded to £29.30/month** |

All amounts are **GBP, excluding VAT**.

### Container Apps detail

Azure's USD retail feed exposes sub-penny per-second meters more precisely than its GBP feed. For the
calculation, the UK South USD meters were converted using the same Azure GBP/USD price ratio visible
on the Container Apps request meter (0.73625):

| Meter | Source retail rate | Approximate GBP rate |
|---|---:|---:|
| Active vCPU | US$0.000034/vCPU-second | £0.00002503/vCPU-second |
| Idle vCPU | US$0.000004/vCPU-second | £0.000002945/vCPU-second |
| Active or idle memory | US$0.000004/GiB-second | £0.000002945/GiB-second |

The estimate deducts the monthly 180,000 vCPU-second and 360,000 GiB-second Consumption grants. The
300 external detector calls are far below the two-million-request monthly grant. Health-probe requests
are not billable requests. The estimate assumes the warm replica remains below the idle thresholds
between messages; sustained CPU above the idle threshold would increase the charge.

### Logic Apps detail

| Execution type | Monthly executions | Rate used | Estimated cost |
|---|---:|---:|---:|
| Outlook polling trigger | 43,200 | About £0.00009203/Standard connector execution | £3.98 |
| Outlook send-mail action | 300 | About £0.00009203/Standard connector execution | £0.03 |
| Built-in operations | About 2,100 | Within first 4,000/month shared grant | £0.00 |
| Retained workflow data | About 0.8 GB-month | £0.0883/GB-month | £0.08 |

The exact retained-data amount is uncertain because Logic Apps internally stores workflow state and
can retain more than one representation of an attachment. The estimate therefore allows for more than
the raw seven-day attachment footprint. Retries, pagination, failures, or emails with more than one
attachment increase execution and retention usage.

## Sensitivity and alternative configuration

| Scenario | Approximate monthly total | Effect |
|---|---:|---|
| Current defaults and stated assumptions | **£29.30** | One warm replica and a one-minute mailbox poll |
| Images average the full 20 MB limit | **About £29.50-£29.60** | Mainly increases Logic App run-history retention |
| Relevant shared free grants already consumed | **About £31.10** | Adds Container Apps grant value, built-in actions, and an assumed 0.1 GB of log ingestion |
| Set `appMinReplicas` to 0 | **About £7.80** | Intermittent compute should fit within Container Apps grants, but introduces cold-start latency for the .NET process and ONNX model |

The clearest saving is changing `appMinReplicas` from 1 to 0. At only ten messages per day, expected
active compute remains inside the monthly Container Apps grants. The trade-off is cold start: the
container must start and initialize the ONNX runtime before the Logic App HTTP action times out. This
should be load-tested before changing the production setting.

Reducing the one-minute mailbox polling frequency would also reduce the recurring Logic Apps charge,
but it increases the delay before an email is processed. A webhook-style trigger would avoid polling
cost if the selected mailbox connector and operational design support it.

## Deployment observations

- The Logic App resource is deployed disabled and the Office 365 connection requires interactive
  authorization. There is no email processing until both steps are completed.
- The result email currently contains a hard-coded Container Apps dashboard hostname in
  `logicapp/workflow.json`. Deploying under another prefix, resource group, or subscription can leave
  the email pointing at the wrong application. The dashboard URL should be supplied as a workflow
  parameter derived from the deployed Container App FQDN.
- Container Apps ingress is public. The `/analyze` endpoint is protected with an API key, but the
  results dashboard, stored-result listing, and thumbnail route are publicly reachable through the
  application. Authentication should be added before storing or exposing sensitive operational data.
- The detector API key is an Azure deployment parameter, Container Apps secret, and Logic App
  parameter. This is workable for a POC, but Key Vault-backed secret management and rotation would be
  preferable for an operational service.
- The Storage account disables public blob access and shared-key authentication; the application uses
  its managed identity, which is the appropriate access pattern.
- The current estimate does not include Microsoft 365 licensing. A work account with access to the
  shared mailbox is an external prerequisite, not an Azure consumption resource.

## Pricing sources and limitations

Prices were checked on **23 September 2026** using the public Azure Retail Prices API for UK South and
GBP wherever the feed exposed sufficient precision. USD meter values were used for sub-penny
Container Apps and Logic Apps execution rates, then converted using Azure's contemporaneous GBP price
ratio. Microsoft states that USD is the base pricing currency and non-USD Retail Prices API results
are estimates for budgeting.

Official references:

- [Azure Retail Prices API](https://learn.microsoft.com/en-us/rest/api/cost-management/retail-prices/azure-retail-prices)
- [Azure Container Apps pricing](https://azure.microsoft.com/en-gb/pricing/details/container-apps/)
- [Container Apps billing behavior](https://learn.microsoft.com/en-us/azure/container-apps/billing)
- [Azure Logic Apps pricing](https://azure.microsoft.com/en-gb/pricing/details/logic-apps/)
- [Logic Apps metering and billing](https://learn.microsoft.com/en-us/azure/logic-apps/logic-apps-pricing)
- [Azure Container Registry pricing](https://azure.microsoft.com/en-gb/pricing/details/container-registry/)
- [Azure Monitor pricing](https://azure.microsoft.com/en-gb/pricing/details/monitor/)
- [Application Insights pricing FAQ](https://learn.microsoft.com/en-us/azure/azure-monitor/app/application-insights-faq)
- [Azure Storage cost model](https://learn.microsoft.com/en-us/azure/storage/common/storage-plan-manage-costs)

This document describes the repository and pricing available on the date above. Azure rates and free
allowances can change, and existing subscription usage can consume shared allowances. For a deployed
environment, validate the estimate after one complete month with Azure Cost Management grouped by
resource and meter.
