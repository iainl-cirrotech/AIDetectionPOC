import json
import os
from pathlib import Path

from fastapi import FastAPI, HTTPException
from fastapi.responses import HTMLResponse, Response
from fastapi.templating import Jinja2Templates
from starlette.requests import Request

ACCOUNT_URL = os.getenv("STORAGE_ACCOUNT_URL", "").rstrip("/")
STORE_BACKEND = os.getenv("STORE_BACKEND", "azure").strip().lower()
LOCAL_STORE_DIR = Path(os.getenv("LOCAL_STORE_DIR", "/tmp/aidetect-store"))
THUMBNAIL_CONTAINER = os.getenv("THUMBNAIL_CONTAINER", "thumbnails")
RESULTS_TABLE = os.getenv("RESULTS_TABLE", "detections")
REGION = os.getenv("AZURE_REGION", "uksouth")
MAX_RECORDS = int(os.getenv("MAX_RECORDS", "50"))

app = FastAPI(title="AI Image Detection - Results", version="1.0.0")
templates = Jinja2Templates(directory=os.path.join(os.path.dirname(__file__), "templates"))

_table = None
_blob = None
if STORE_BACKEND == "azure" and ACCOUNT_URL:
    from azure.data.tables import TableServiceClient
    from azure.identity import DefaultAzureCredential
    from azure.storage.blob import BlobServiceClient

    credential = DefaultAzureCredential()
    _table = TableServiceClient(endpoint=ACCOUNT_URL.replace(".blob.", ".table."), credential=credential)
    _blob = BlobServiceClient(account_url=ACCOUNT_URL, credential=credential)

STORAGE_ENABLED = STORE_BACKEND == "file" or _table is not None


def _json_list(value) -> list:
    if not value:
        return []
    try:
        parsed = json.loads(value)
        return parsed if isinstance(parsed, list) else []
    except Exception:
        return []


def _to_display(entity: dict) -> dict:
    return {
        "correlation_id": entity.get("RowKey") or entity.get("correlation_id", ""),
        "processed_at_utc": entity.get("processed_at_utc", ""),
        "received_at_utc": entity.get("received_at_utc", ""),
        "subject": entity.get("subject", ""),
        "sender": entity.get("sender", ""),
        "filename": entity.get("filename", ""),
        "image_sha256": entity.get("image_sha256", ""),
        "thumbnail_blob": entity.get("thumbnail_blob", ""),
        "ai_probability": entity.get("ai_probability"),
        "band": entity.get("band", ""),
        "overall_label": entity.get("overall_label", ""),
        "overall_severity": entity.get("overall_severity", ""),
        "overall_basis": entity.get("overall_basis", ""),
        "c2pa_present": entity.get("c2pa_present", False),
        "c2pa_state": entity.get("c2pa_state", ""),
        "digital_source_type": entity.get("digital_source_type", ""),
        "model_name": entity.get("model_name", ""),
        "model_version": entity.get("model_version", ""),
        "processing_region": entity.get("processing_region", ""),
        "evidence": _json_list(entity.get("evidence_json")),
        "findings": _json_list(entity.get("findings_json")),
    }


def fetch_records() -> list[dict]:
    if STORE_BACKEND == "file":
        path = LOCAL_STORE_DIR / "detections.jsonl"
        if not path.exists():
            return []
        entities = [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines() if line.strip()]
    elif _table is not None:
        client = _table.get_table_client(RESULTS_TABLE)
        entities = list(client.list_entities())
    else:
        return []
    records = [_to_display(entity) for entity in entities]
    records.sort(key=lambda item: item["processed_at_utc"], reverse=True)
    return records[:MAX_RECORDS]


@app.get("/", response_class=HTMLResponse)
def index(request: Request):
    records = fetch_records()
    for record in records:
        probability = record.get("ai_probability")
        record["probability_display"] = f"{probability:.0%}" if isinstance(probability, (int, float)) else "n/a"
    return templates.TemplateResponse(
        request=request,
        name="index.html",
        context={"records": records, "region": REGION, "storage_enabled": STORAGE_ENABLED},
    )


@app.get("/thumb/{blob_name:path}")
def thumbnail(blob_name: str):
    if STORE_BACKEND == "file":
        path = LOCAL_STORE_DIR / "thumbnails" / Path(blob_name).name
        if not path.exists():
            raise HTTPException(status_code=404, detail="thumbnail not found")
        return Response(content=path.read_bytes(), media_type="image/jpeg")
    if _blob is None:
        raise HTTPException(status_code=503, detail="storage not configured")
    client = _blob.get_blob_client(container=THUMBNAIL_CONTAINER, blob=blob_name)
    try:
        data = client.download_blob().readall()
    except Exception as exc:
        raise HTTPException(status_code=404, detail=str(exc)) from exc
    return Response(content=data, media_type="image/jpeg", headers={"Cache-Control": "private, max-age=300"})


@app.get("/health")
def health() -> dict:
    return {"status": "ok", "storage_enabled": STORAGE_ENABLED, "backend": STORE_BACKEND, "region": REGION}
