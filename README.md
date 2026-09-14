# AI Image Detection - proof of capability

A minimal demonstration that an image received by email can be picked up automatically, analysed
for signs of AI generation or alteration, and shown with a detection result and audit evidence.

Email -> image handled safely in memory -> AI/alteration detection -> result displayed -> handling
explained and evidenced. Nothing else. There is no business process in this repository.

See [GOVERNANCE.md](GOVERNANCE.md) for the data-handling and governance position.

## How detection works

A multi-signal detector runs in the client's own Azure subscription:

| Signal | Implementation | Output |
|---|---|---|
| AI-generation classifier | Open-weights ViT image classifier, served by the detector container | `ai_probability` 0-1 mapped to Low / Medium / High |
| C2PA Content Credentials | `c2pa-python` local verification against the official C2PA trust list | present/absent, validation state (including `Trusted`), declared `digitalSourceType`, tamper codes |
| Metadata forensics | EXIF/XMP inspection | camera tags, editing/generation software tags, stripped metadata |

The official C2PA trust list is pinned at `detector/c2pa_trust/C2PA-TRUST-LIST.pem` and baked into
the image, so provenance verification works offline. Refresh it with `scripts/fetch_trust_list.sh`.

The classifier is configurable (`DETECTOR_MODEL_ID`). The default is
`capcheck/ai-human-generated-image-detection`. Validate accuracy on representative images before a
live demo; swap the model id if a different one performs better.

The detector also produces an **overall indicator** that weights cryptographic provenance above the
probabilistic classifier. A valid C2PA manifest declaring `trainedAlgorithmicMedia` or
`compositeWithTrainedAlgorithmicMedia` yields `AI-generated (provenance)` / `AI-modified (provenance)`
regardless of the classifier score; otherwise the classifier band is used. This prevents a weak
classifier from masking strong provenance evidence. The raw classifier score is still shown
separately and the evidence list explains the basis of the indicator.

## Architecture

```
Exchange Online shared mailbox
  -> Logic App (Office 365 Outlook connector)
     -> detector Container App (in-memory analysis; original not persisted)
        -> Blob Storage (thumbnail only) + Table Storage (result) + Log Analytics (audit)
           -> results Container App (simple table + thumbnail)
```

All Azure resources deploy to one region (default `uksouth`). The original image is never written to
storage; only a 512 px thumbnail and a result record are kept.

## Repository layout

```
detector/            FastAPI service: imaging, metadata, C2PA, classifier, storage
web/                 FastAPI results page (table + thumbnail)
logicapp/            Logic App workflow definition (mailbox pickup -> detector)
infra/main.bicep     All Azure resources (validated with the Bicep compiler)
scripts/             local demo, image build/push, deploy, result purge
GOVERNANCE.md        Data flow and governance statements
```

## Run the demo locally (no Azure)

Requires Python 3.11+ and curl. Uses a local file store and a deterministic placeholder classifier
so the pipeline can be seen end to end.

```
./scripts/local_demo.sh
```

Then open the printed results URL. To use the real classifier locally instead of the placeholder,
set `DETECTOR_CLASSIFIER=hf`, install `detector/requirements.txt`, and install the CPU build of
torch (`pip install torch --index-url https://download.pytorch.org/whl/cpu`). The placeholder mode
is clearly labelled and is not real detection.

You can also post an image directly:

```
curl -X POST http://127.0.0.1:8094/analyze \
  -F "file=@samples/sample-plain.png" \
  -H "x-email-subject: Test" -H "x-email-sender: alice@example.com"
```

## Deploy to Azure

Prerequisites: Azure CLI, an Azure subscription, and an internal-use shared mailbox.

```
export RESOURCE_GROUP=rg-aidetect-demo
export SHARED_MAILBOX=ai-image-demo@yourdomain.com
export LOCATION=uksouth
./scripts/deploy.sh
```

The script creates the platform, builds both images into the new Azure Container Registry with
`az acr build` (the detector image bakes the model in at build time), deploys the real images, and
prints the results URL.

Then:

1. In the Azure portal, open the API connection `<prefix>-office365` and authorise it with an
   account that can read the shared mailbox.
2. Enable the Logic App `<prefix>-mail-pickup`.
3. Send an email with an image attachment to the shared mailbox.

The results page shows, per image: subject and sender, received time, thumbnail, detection score and
Low/Medium/High band, C2PA status and metadata evidence, processing time, model name/version and
region, and the correlation id.

## Configuration

Key environment variables (see `.env.example`): `AZURE_REGION`, `STORE_BACKEND` (`azure` or `file`),
`STORAGE_ACCOUNT_URL`, `DETECTOR_MODEL_ID`, `DETECTOR_MODEL_VERSION`, `BAND_LOW_THRESHOLD`,
`BAND_HIGH_THRESHOLD`, `THUMBNAIL_MAX_PX`, `PERSIST_ORIGINAL_IMAGE`, `DETECTOR_API_KEY`.

Bicep parameters include `thumbnailRetentionDays`, `logicAppRetentionDays`, `logsRetentionDays`,
`detectorMinReplicas` and `detectorModelId`.

## Security notes

- The original image is processed in memory and not persisted (`PERSIST_ORIGINAL_IMAGE=false`).
- Blob public access is disabled and shared key access is disabled; the app uses a managed identity.
- The detector endpoint is protected by an API key stored as a Container App secret. For a hardened
  deployment, switch to internal ingress with VNet integration and private endpoints.
- Runtime model downloads are disabled (`HF_HUB_OFFLINE`, `TRANSFORMERS_OFFLINE`); weights are baked
  into the image at build time.
- The Logic App trigger and the detector call have secure inputs/outputs enabled, so the attachment
  payload is masked in run history and is not exported to Log Analytics. Run history retention is
  set to the 7-day minimum. If the Office 365 connector schema differs when you open the workflow,
  re-select the shared-mailbox trigger and the HTTP action, then re-enable Secure Inputs/Outputs in
  each one's Settings.

## Limitations

Detection is probabilistic and model-dependent. See the limitations section in
[GOVERNANCE.md](GOVERNANCE.md). Results are indicators only, not determinations.
