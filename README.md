# AI Image Detection – proof of capability

A .NET demonstration that accepts an image from a browser, API call, or shared-mailbox workflow,
analyses it for signs of AI generation or alteration, displays the result with audit evidence, and
can email a cautious summary back to the original sender. It is a screening aid, not a determination
that a claim is genuine or fraudulent.

## What it checks

| Signal | .NET implementation | Output |
|---|---|---|
| AI-generation classifier | Community Forensics ViT-S/16 ONNX model through ONNX Runtime | Probability mapped to Low / Medium / High |
| C2PA Content Credentials | Optional official `c2patool` executable and pinned trust list | Present/absent, validation state, source type, tamper codes |
| Metadata indicators | ImageSharp plus bounded byte-marker inspection | Camera, software, EXIF/XMP and C2PA markers |

The classifier is the pinned August 2026
`Thermostatic/community-forensics-frontier-detector-2026-08` artifact. The app applies its published
resize, centre crop, ImageNet normalisation and logit calibration. The model file is downloaded from
the pinned revision and checked against its published SHA-256.

Cryptographic provenance takes precedence over the classifier: a valid C2PA declaration of trained
algorithmic media produces an AI-generated or AI-modified provenance result. Otherwise the
probabilistic classifier drives the overall indicator. Absence of C2PA or EXIF is not evidence that an
image is fake.

## Run locally

Requirements: .NET 10 SDK and `curl`. The first real-model run downloads about 42 MB once.

```bash
./scripts/local_demo.sh
```

Open `http://127.0.0.1:8093`. The page has an upload control; selecting an image and pressing
**Analyse image** is what causes processing. Copying a file into the repository or result folder does
not automatically analyse it.

Local results are retained in `/tmp/aidetect-dotnet-store` by default. The store contains only JSON
result records and downscaled thumbnails. Set `RESET_LOCAL_STORE=1` for a clean demonstration run.

Useful alternatives:

```bash
# Run without the helper after the model has been downloaded
STORE_BACKEND=file \
DETECTOR_MODEL_PATH=models/community_forensics_frontier_fp16.onnx \
dotnet run --project src/AiDetection.Web/AiDetection.Web.csproj

# API upload
curl -X POST http://127.0.0.1:8093/analyze \
  -F "file=@samples/IMG_5615.jpeg" \
  -H "x-email-subject: Local test" \
  -H "x-email-sender: local@example.com"

# Fast pipeline/UI demonstration without real inference
DETECTOR_CLASSIFIER=mock ./scripts/local_demo.sh
```

`c2patool` is optional locally. If it is not on `PATH`, classifier and metadata analysis still run and
the evidence explicitly says provenance verification was unavailable. In the production container it
is included and configured against `detector/c2pa_trust/C2PA-TRUST-LIST.pem`.

## Test

```bash
dotnet test AIDetectionPOC.slnx
```

## Architecture

```text
Browser upload or Exchange Online mailbox
  -> ASP.NET Core Container App
       -> image validation and metadata indicators
       -> ONNX Runtime classifier
       -> c2patool provenance verification
       -> Blob Storage (thumbnail only) + Table Storage (result)
       -> results page and API response
  -> Logic App sends one result-summary email to the original sender
```

The application is one ASP.NET Core process. Local mode uses a JSON Lines file and thumbnail folder;
Azure mode uses Blob and Table Storage through managed identity. The Logic App remains the mailbox
adapter and calls the compatible `POST /analyze` endpoint.

## Repository layout

```text
src/AiDetection.Web/       Combined ASP.NET Core API, UI and analysis pipeline
tests/AiDetection.Tests/   .NET policy, mapping and preprocessing tests
models/                    Local ONNX model location (model itself is git-ignored)
logicapp/                  Shared-mailbox intake workflow
infra/main.bicep           Azure Container App, storage, identity and monitoring
scripts/local_demo.sh      One-command local real-model demonstration
scripts/build_push.sh      Build the single .NET container in Azure Container Registry
scripts/deploy.sh          Provision and deploy to Azure
detector/, web/            Retained legacy Python implementation for migration reference
```

## Deploy to Azure

Prerequisites: Azure CLI, an Azure subscription, permission to create resources and role assignments
(Owner, or Contributor plus User Access Administrator), and a Microsoft 365 work mailbox.

```bash
az login
az account set --subscription '<subscription-id-or-name>'
az group create --name rg-aidetect-demo --location uksouth

export RESOURCE_GROUP=rg-aidetect-demo
export MAILBOX_ADDRESS=iainl@byoma.com
export LOCATION=uksouth
./scripts/deploy.sh
```

The deployment creates one Container App, builds the .NET image in Azure Container Registry, embeds
the pinned ONNX model and `c2patool`, and configures Blob/Table access through a user-assigned managed
identity. The deployment script generates a random 256-bit detector API key unless
`DETECTOR_API_KEY` is explicitly supplied. Afterwards, authorise the generated Office 365 API
connection and enable the Logic App.
Authorise the connection using the same Microsoft 365 account configured in `MAILBOX_ADDRESS`. The
POC only processes messages with `[AI-CHECK]` in the subject, preventing unrelated image attachments
in the mailbox from being submitted. For an email containing multiple images, the workflow sends one
summary after all attachments have been processed. It sends nothing when no image succeeds. Each
attachment request uses a unique correlation reference, preventing multiple images in one email from
overwriting one another in the result store.

After the script finishes, open the generated Office 365 connection in the Azure portal and
authorise it, then enable the generated Logic App. Send an email containing an image attachment and
`[AI-CHECK]` in its subject to the configured mailbox, then confirm that the sender receives one
screening-summary email. The portal step
cannot be completed by the unattended deployment because the connector requires an interactive
Microsoft 365 sign-in and consent.

Browser upload is enabled automatically for the local file backend and disabled by default for the
Azure backend. The Logic App uses the API-key-protected analysis endpoint.

## Configuration

The main settings are shown in [.env.example](.env.example):

- `DETECTOR_CLASSIFIER`: `onnx`, `mock`, or `disabled`.
- `DETECTOR_MODEL_PATH`: local path of the pinned ONNX artifact.
- `STORE_BACKEND`: `file` or `azure`.
- `LOCAL_STORE_DIR`: file-backend result directory.
- `RESULTS_RETENTION_DAYS`: automatic result-record retention; defaults to 30 days.
- `STORAGE_ACCOUNT_URL`: Azure Blob endpoint; the Table endpoint is derived from it.
- `BAND_LOW_THRESHOLD` / `BAND_HIGH_THRESHOLD`: screening bands.
- `C2PATOOL_PATH` / `C2PA_TRUST_FILE`: provenance verifier and trust anchors.
- `ENABLE_BROWSER_UPLOAD`: defaults to true locally and false with Azure storage.
- `DETECTOR_API_KEY`: protects `POST /analyze` when configured.

## Data and security notes

- The retained data is a 512 px thumbnail and an analysis/audit record, not the original image.
- ONNX inference and metadata processing use memory. Because `c2patool` accepts a file path, C2PA
  verification uses a randomly named operating-system temporary file and deletes it immediately in a
  `finally` block; it is never copied to the result store.
- Blob public access and storage-account shared-key access are disabled in Azure.
- The model and trust list are pinned for reproducibility; runtime model downloads are not used in
  the production container.
- The result remains probabilistic and must be validated on representative claim images before an
  operational threshold or automated decision is adopted.
