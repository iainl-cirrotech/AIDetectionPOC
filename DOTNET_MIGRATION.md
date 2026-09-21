# .NET migration record

Created: 2026-09-21
Status: Completed

This document records the working state of the proof of capability before the migration, the
intended .NET replacement, and the verified outcome. It was created before implementation changes
were started and will be updated when the migration is complete.

## What works before the migration

The repository currently implements the image-analysis demonstration as two Python 3.11 services:

- `detector/` is a FastAPI API. It accepts an image through `POST /analyze`, validates and decodes it,
  creates a thumbnail, extracts EXIF/XMP indicators, checks C2PA provenance, runs an AI-generation
  classifier, assigns Low/Medium/High bands, and records an evidence-based overall indicator.
- `web/` is a FastAPI/Jinja results site. It reads stored result records and thumbnails and displays
  the classifier score, C2PA status, evidence, model identity, and audit fields.
- Local mode stores thumbnails and flattened JSON Lines records below `LOCAL_STORE_DIR` (by default
  `/tmp/aidetect-store`). `scripts/local_demo.sh` starts both services and submits generated samples.
- Azure mode stores thumbnails in Blob Storage and results in Table Storage using managed identity.
- The real classifier is `Thermostatic/community-forensics-frontier-detector-2026-08`, loaded through
  PyTorch/timm from a pinned Hugging Face revision. Its published preprocessing is resize-short-edge
  440, centre-crop 384, ImageNet normalisation, followed by published logit calibration.
- C2PA is checked by `c2pa-python` against the pinned official trust list.
- Azure deployment uses two Python container images, Azure Container Apps, Storage, Application
  Insights/Log Analytics, a user-assigned managed identity, and a Logic App that reads a shared
  Exchange Online mailbox.
- The original image is processed in memory and is not persisted. A thumbnail and analysis record
  are retained.

Known boundaries of the current implementation:

- The default local script uses a deterministic mock score unless the heavyweight Python model
  dependencies are explicitly selected.
- Refreshing the results URL only reads the result store; placing an arbitrary image in a folder does
  not cause it to be analysed.
- The result is a probabilistic screening indicator, not proof that an image is genuine or fraudulent.
- C2PA is frequently absent and absence is not evidence of fakery.

## What we are changing it to

The target is one ASP.NET Core application and test project:

- One process will host the results UI, `POST /analyze`, health endpoint, thumbnails, and an explicit
  browser upload form for convenient local testing.
- C# services will implement image validation, thumbnailing, metadata indicators, result scoring,
  evidence generation, and file/Azure persistence.
- The real classifier will run the model's published ONNX artifact with ONNX Runtime. Image
  preprocessing and calibration will match the published model configuration.
- C2PA will be invoked through an optional bundled/configured `c2patool` executable because there is
  no official managed .NET C2PA SDK. A missing tool will be reported clearly and will not prevent the
  classifier from running.
- Local mode will require no Azure resources and will be runnable with a single script or `dotnet run`.
- Azure mode will use the same Blob/Table data design and managed identity. The container and Bicep
  assets will be updated for the .NET application. Existing mailbox ingestion may continue to call
  the compatible analysis endpoint.
- The current API/result field names will be retained where practical so the Logic App and stored
  audit data remain compatible.
- Automated tests will cover thresholds, overall indicator precedence, storage/result mapping, and
  deterministic or real-model inference paths that can be exercised locally.

## Acceptance checks

- [x] The solution restores and builds with the installed .NET SDK.
- [x] Automated tests pass.
- [x] A single local command starts the application.
- [x] The health endpoint responds.
- [x] An image can be uploaded locally and produces a visible stored result and thumbnail.
- [x] The supplied `samples/IMG_5615.jpeg` can be processed by the real ONNX classifier locally.
- [x] No original image is retained in the result store.
- [x] Docker/Azure deployment definitions point to and configure the .NET application.
- [x] User documentation accurately describes local and Azure operation.

## Completion record

Completed on 2026-09-21.

### What was actually changed

- Added `AIDetectionPOC.slnx`, a .NET 10 ASP.NET Core project under `src/AiDetection.Web`, and an
  xUnit project under `tests/AiDetection.Tests`.
- Combined the API, health endpoint, results page, thumbnail endpoint, and local upload form into one
  process. `POST /analyze` retains the raw-body and multipart contract used by the Logic App.
- Reimplemented image limits, SHA-256, EXIF/XMP indicators, thumbnails, probability bands, C2PA
  precedence, evidence text, audit logging, file/Azure result storage, and scheduled result retention
  in C#.
- Replaced PyTorch runtime inference with the publisher's FP16-weight ONNX artifact through
  `Microsoft.ML.OnnxRuntime`. The model revision, calibration and published model SHA-256 are pinned.
- Added optional `c2patool` 0.27.15 integration with the pinned trust list. The production image
  includes it; local operation reports a clear unavailable state when it is not installed.
- Replaced the two-container deployment definition with one .NET Container App. It deliberately
  reuses the existing `<prefix>-detector` Azure resource name so the analysis URL migrates in place.
  A fresh deployment creates only one app. An upgrade of an existing deployment may leave the old
  scale-to-zero `<prefix>-web` resource; it should be removed manually after the new combined UI is
  verified because this migration does not delete existing Azure resources automatically.
- Updated the ACR build/deploy scripts for one image, corrected Logic App retention placement in the
  workflow definition, enabled Application Insights through Azure Monitor OpenTelemetry, and kept
  the existing managed-identity Blob/Table design.
- Replaced the Python local launcher with a one-command .NET launcher. It downloads and verifies the
  model on first use, retains local results between runs, seeds the supplied image only when the store
  is empty, and prints the actual folder it reads/writes.
- Updated `README.md`, `.env.example`, and `GOVERNANCE.md`. The legacy Python folders remain in the
  repository as migration reference but are no longer used by the local or Azure scripts.

### Deliberate implementation details and deviations

- `c2patool` has no stream-input verification interface used here, so C2PA verification creates a
  randomly named operating-system temporary file and deletes it in a `finally` block. The original
  is never retained in the result store.
- Browser upload defaults to enabled with file storage and disabled with Azure storage, preventing an
  unauthenticated public upload surface in the deployed demo. Mailbox intake continues through the
  API-key-protected endpoint.
- ImageSharp's antialiased resize is not pixel-identical to Pillow. On the supplied image the .NET
  ONNX path scored `0.9085184712` (High), while the previous PyTorch/Pillow path scored
  `0.9404265881` (High). The operational band agrees; exact probabilities should not be compared
  across the two runtimes as if they were identical measurements.

### Verification performed

- `dotnet test AIDetectionPOC.slnx --no-restore -m:1 -v:minimal`: 13 passed, 0 failed.
- `dotnet publish ... -c Release`: succeeded.
- Real-model local run: health HTTP 200, analysis HTTP 200, supplied image scored High, JSON Lines
  record written, and a 41,913-byte thumbnail written.
- `c2patool 0.27.15` was downloaded from the pinned official release and executed against the sample;
  both the CLI and application reported that no C2PA claim was present.
- The downloaded ONNX file matched published SHA-256
  `d75791ba2fa59146025d342cfaafa9ddeab24af117642a94f752ee4c1619375d`.
- Bicep CLI 0.47.16 compiled `infra/main.bicep` successfully with no diagnostics.
- Shell syntax, Logic App JSON parsing, and `git diff --check` passed.
- A local Docker build could not be executed because the current user cannot access
  `/var/run/docker.sock`. The Release publish, Docker dependency URLs/archive layout, model checksum,
  `c2patool` binary, and Dockerfile paths were verified separately. The ACR build remains the intended
  production image build path.
