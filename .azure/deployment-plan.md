# Azure Deployment Plan

> **Status:** Deployed

Generated: 2026-09-22

---

## 1. Project Overview

**Goal:** Deploy the existing .NET AI-image-screening POC to Azure, monitor the authenticated
Microsoft 365 mailbox `iainl@byoma.com` for image messages whose subject contains `[AI-CHECK]`,
analyse each image, retain a thumbnail and audit record, and email a cautious summary to the sender.

**Path:** Modify an existing Azure-ready application. The application, Dockerfile, Bicep and Azure
CLI deployment scripts already exist; this plan records and validates that deployment rather than
replacing it with `azd`.

## 2. Requirements

| Attribute | Value |
|---|---|
| Classification | POC |
| Scale | Small; one warm Container App replica, maximum three |
| Budget | Cost-optimized POC SKUs, while keeping one warm replica for demo responsiveness |
| Subscription | Cirrotech Limited (`22065b91-c0db-4279-8972-34e906e6499b`) — confirmed by user and Azure CLI |
| Location | UK South (`uksouth`) — confirmed by user |
| Resource group | `rg-aidetect-demo` — created successfully |
| Mailbox | `iainl@byoma.com`, authenticated interactively after deployment |
| Data handling | Original image processed transiently; thumbnail and result retained under documented policies |

### Policy constraints

The subscription policy query returned only the Microsoft Defender for Cloud default assignment;
no location, SKU, tagging, networking, storage-key or resource-type deny policy was found. Azure
what-if completed successfully against the target subscription and resource group.

## 3. Components Detected

| Component | Type | Technology | Path |
|---|---|---|---|
| Combined detector and results UI | Web/API | ASP.NET Core .NET 10 | `src/AiDetection.Web/` |
| Image classifier | In-process inference | ONNX Runtime, pinned Community Forensics model | `src/AiDetection.Web/`, `models/` |
| Provenance verifier | In-process executable | `c2patool` with pinned trust list | `src/AiDetection.Web/Dockerfile`, `detector/c2pa_trust/` |
| Mail workflow | Integration workflow | Azure Logic Apps Consumption + Office 365 Outlook connector | `logicapp/workflow.json` |
| Persistence | Object/key-value storage | Azure Blob Storage and Table Storage | `src/AiDetection.Web/ResultStore.cs` |
| Infrastructure | IaC | Bicep | `infra/main.bicep` |
| Deployment | Orchestration | Bash + Azure CLI/ACR remote build | `scripts/deploy.sh`, `scripts/build_push.sh` |

## 4. Recipe Selection

**Selected:** AZCLI with existing Bicep.

**Rationale:** The repository already has a purpose-built, three-phase Azure CLI deployment that
provisions Bicep infrastructure, builds the image remotely in ACR, and replaces the bootstrap image.
There is no `azure.yaml`; introducing `azd` would duplicate and destabilize an already validated POC
deployment path.

## 5. Architecture

**Stack:** Containers plus managed integration.

| Component | Azure service | SKU/configuration |
|---|---|---|
| .NET web/API and inference | Azure Container Apps | Consumption, 1 vCPU/2 GiB, min 1/max 3 |
| Container image/build | Azure Container Registry | Basic; ACR Tasks remote build |
| Results and thumbnails | Storage account | StorageV2 Standard_LRS, shared keys disabled |
| Thumbnail storage | Private Blob container | 7-day lifecycle deletion |
| Result records | Table Storage | Application purge after 30 days |
| Mail intake and response | Logic Apps Consumption | 1-minute polling, `[AI-CHECK]` subject filter |
| Mail connection | Office 365 Outlook managed connection | Interactive authorization as `iainl@byoma.com` |
| Runtime identity | User-assigned managed identity | AcrPull, Blob Data Contributor, Table Data Contributor |
| Telemetry | Application Insights + Log Analytics | Workspace-based, 90-day retention |

The detector endpoint is externally reachable but API-key protected; browser upload is disabled for
the Azure storage backend. The deployment script generates a cryptographically random 256-bit API
key and stores it as a Container App secret and Logic App secure parameter. Explicit startup,
liveness and readiness probes use `/health`. Private networking and Key Vault are documented
production hardening items rather than POC scope.

## 6. Provisioning Limit Checklist

Azure Quota CLI 1.0.0 was queried first for `Microsoft.App` and `Microsoft.Storage` in UK South; the
subscription returned no count-based rows for either provider. The fallback inventory used Azure
Resource Manager resource listings and Microsoft service-limit documentation. The full Azure
deployment what-if succeeded, confirming provider registration, names, policy and authorization.

| Resource type | New | UK South total after | Limit/quota evidence | Result |
|---|---:|---:|---|---|
| `Microsoft.App/managedEnvironments` | 1 | 1 | Subscription-specific Managed Environment Count; quota API returned no row; what-if succeeded | Pass |
| `Microsoft.App/containerApps` | 1 | 1 | Environment/app quota not exposed before environment creation; 1 vCPU initial use; what-if succeeded | Pass |
| `Microsoft.ContainerRegistry/registries` Basic | 1 | 1 | No create-count quota exposed; Basic request limits materially exceed one POC build/app | Pass |
| `Microsoft.Storage/storageAccounts` | 1 | 5 | 250 standard-endpoint accounts per subscription/region by default | Pass |
| `Microsoft.OperationalInsights/workspaces` | 1 | 3 | No limit for non-legacy-free workspaces beyond ARM limits | Pass |
| `Microsoft.Insights/components` | 1 | 6 | No relevant create-count quota exposed; 100 GB/day default ingestion per component | Pass |
| `Microsoft.Logic/workflows` | 1 | 8 | No relevant create-count quota exposed; one low-volume Consumption workflow; what-if succeeded | Pass |
| `Microsoft.Web/connections` | 1 | Existing + 1 | No relevant create-count quota exposed; what-if succeeded | Pass |
| `Microsoft.ManagedIdentity/userAssignedIdentities` | 1 | Existing + 1 | No regional capacity quota exposed; what-if and RBAC validation succeeded | Pass |

**Status:** All planned resources are within available limits for this small POC.

## 7. Execution Checklist

### Phase 1: Planning

- [x] Analyze workspace
- [x] Gather requirements from the conversation
- [x] Confirm subscription and location
- [x] Query applicable subscription policies
- [x] Prepare resource inventory
- [x] Invoke Azure quota workflow and validate capacity
- [x] Scan codebase
- [x] Select AZCLI/Bicep recipe
- [x] Plan architecture
- [x] User approved this written plan

### Phase 2: Preparation

- [x] Existing Bicep, Dockerfile and deployment scripts reviewed
- [x] Container Apps, Storage, Logic Apps, Application Insights and AZCLI references reviewed
- [x] Application Insights OpenTelemetry instrumentation confirmed
- [x] Random 256-bit detector API key generation and explicit health probes added
- [x] Mailbox workflow updated for a standard Microsoft 365 account and `[AI-CHECK]` filter
- [x] Documentation updated for mailbox response and deployment prerequisites
- [x] Resource group created and required providers registered
- [x] Required deployment RBAC activated with a condition excluding privileged role grants
- [x] Mark plan Ready for Validation after approval

### Phase 3: Validation

- [x] Invoke the formal `azure-validate` workflow
- [x] All validation checks pass
  - [x] Core validation: Azure CLI, authentication, Bicep build, ARM validation and what-if
  - [x] Container build inputs and Release publish validated; ACR remote build is the deployment path (local Docker socket unavailable)
  - [x] Azure Policy validation
  - [x] .NET tests, Logic App JSON, shell syntax and diff checks
  - [x] RBAC permission and planned managed-identity role checks
- [x] Record validation proof and status

### Phase 4: Deployment

- [x] Invoke the formal `azure-deploy` workflow
- [x] Provision bootstrap infrastructure
- [x] Build the production container remotely in ACR
- [x] Deploy the production image
- [x] Verify Azure resources, RBAC, health endpoint and application URL
- [x] Interactively authorize the Office 365 connection and enable/test the Logic App

## 8. Validation Proof

Formal validation completed on 2026-09-22:

- Azure CLI installed and authenticated to **Cirrotech Limited**.
- Bicep compiled cleanly, ARM resource-group validation passed, and what-if passed with 19 creates,
  zero modifications and zero deletions.
- 13/13 .NET tests passed and the web project published successfully in Release configuration.
- Logic App JSON parsing, Bash syntax, and `git diff --check` passed.
- The subscription has only the Defender for Cloud default policy assignment; no blocking location,
  SKU, resource-type, networking or tagging policy was found.
- The user-assigned identity has least-privilege roles at resource scope: AcrPull on ACR, Storage Blob
  Data Contributor on the storage account, and Storage Table Data Contributor on the storage account.
  These match the container image pull and the Blob/Table operations in `ResultStore.cs`.
- Docker is installed but the local user cannot access its socket. The application build inputs were
  validated with `dotnet publish`; the production image is built by the existing ACR Task during the
  deployment, which is the documented and intended build path.

## 8.1 Deployment Verification

Deployment completed on 2026-09-22:

- ACR remote build `db1` succeeded and pushed `ai-image-detection:v1` with digest
  `sha256:1bc53bddb9737c0be24b3bb2237e059f94dd9bbdf4360fb1b117937ccc384ab1`.
- Container App `aidetect-detector` is Running; revision `aidetect-detector--0000001` is Healthy,
  active, and receives 100% of traffic.
- `GET /health` returned `status: ok`, with the pinned ONNX classifier available and Azure storage
  enabled. The application root returned HTTP 200 and an unauthenticated `POST /analyze` returned
  HTTP 401.
- Live RBAC verification passed: `AcrPull` on ACR, plus `Storage Blob Data Contributor` and
  `Storage Table Data Contributor` on the storage account, all assigned to `aidetect-id` at resource
  scope.
- The Logic App provisioned successfully but remains Disabled because the Office 365 connection is
  intentionally unauthenticated until `iainl@byoma.com` completes interactive authorization.
- The initial deployment exposed a Logic Apps ARM schema placement error for run-history retention.
  `runtimeConfiguration` was moved from the workflow definition to workflow resource properties,
  revalidated, and the idempotent retry succeeded.

## 8.2 Classifier Wording Update

Application-only update validated on 2026-09-22:

- Replaced classifier-only findings with manual-review language; explicit AI-generated wording is
  retained only when supported by C2PA provenance.
- Added display compatibility so previously stored classifier results also render with the new
  wording without rewriting audit records.
- 14/14 .NET tests passed, including a new legacy-result rendering test; Release publish, Logic App
  JSON parsing, shell syntax and `git diff --check` passed.
- Bicep compilation, ARM validation and Azure what-if passed. Deployment is intentionally limited to
  an ACR image build and Container App image update so the portal-authorized `office365-1`
  connection and enabled Logic App are preserved.
- ACR build `db2` pushed `ai-image-detection:v2` with digest
  `sha256:1f67df824ddd816db7a0ff180f6c80532ba783c672d39eb6b63080bba508238d`.
- Container App revision `aidetect-detector--0000002` is Healthy, active, and receives 100% of
  traffic. The health endpoint passed, the existing result page contains the new wording and no old
  high-indication labels, and live RBAC verification passed.
- Logic App `aidetect-mail-pickup` remained Enabled and its `office365-1` connection remained
  Connected as `iainl@byoma.com` throughout the application-only deployment.

## 8.3 Cirrotech Copyright Footer

Update validated on 2026-09-22:

- Added `© 2026 Cirrotech Ltd. All rights reserved.` to the results page and outbound result-email
  HTML, including the empty-results page state.
- 15/15 .NET tests passed; Release publish, Logic App JSON/template assertion, Bash syntax, and
  `git diff --check` passed.
- Bicep compilation, ARM validation, and Azure what-if passed. Deployment will update the Container
  App image and patch only the current live workflow definition, preserving the portal-authorized
  `office365-1` connection and Enabled state.
- ACR build `db3` pushed `ai-image-detection:v3` with digest
  `sha256:cf992c66023d9a8c95fc1540eb34969133fde48d571b1c074a2d125db53b517b`.
- Container App revision `aidetect-detector--0000003` is Healthy, active, and receives 100% of
  traffic. The live page contains one copyright footer and the health endpoint passed.
- The live Logic App email body contains the copyright footer. Its state remains Enabled, the
  detector API-key parameter remains present, and `office365-1` remains Connected as
  `iainl@byoma.com`. Live RBAC verification also passed.

## 9. Research and Functional Verification

- Container Apps: one warm replica suits the POC; managed identity, ACR and Log Analytics are wired;
  startup/liveness/readiness probes now target the existing `/health` endpoint.
- Storage: Standard_LRS is appropriate for non-critical POC data; anonymous blob access and shared
  keys are disabled, and the app uses resource-scoped managed-identity roles.
- Application Insights: `Azure.Monitor.OpenTelemetry.AspNetCore` is installed and initialized only
  when `APPLICATIONINSIGHTS_CONNECTION_STRING` is supplied by the Container App environment.
- Logic Apps: Consumption plus the Office 365 managed connection matches the low-volume integration
  use case; attachment and response payloads are masked in run history.
- Functional verification: the local UI/API and real ONNX sample path were previously exercised;
  final test run on 2026-09-22 passed 13/13 tests. Logic App JSON, shell syntax, Bicep compilation and
  `git diff --check` also passed.

## 10. Files

| File | Purpose | Status |
|---|---|---|
| `.azure/deployment-plan.md` | Deployment source of truth | Deployed and verified |
| `infra/main.bicep` | Azure infrastructure | Deployed and verified |
| `logicapp/workflow.json` | Mail intake, analysis call and response | Existing and JSON validated |
| `src/AiDetection.Web/Dockerfile` | Production container build | Existing |
| `scripts/deploy.sh` | Azure CLI deployment orchestration | Existing and syntax validated |

## 11. Approval Gate

Current phase: deployed and verified, including the application-only classifier wording update.
