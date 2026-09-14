import logging
import os
import uuid

from fastapi import FastAPI, Header, HTTPException, Request
from fastapi.responses import JSONResponse

from analysis import c2pa_check, metadata
from analysis.classifier import Classifier
from analysis.imaging import ImageRejected, make_thumbnail, open_image, sha256_hex, to_rgb
from config import get_settings
from storage import build_store, utcnow_iso

logging.basicConfig(level=os.getenv("LOG_LEVEL", "INFO"), format="%(message)s")
logger = logging.getLogger("detector")

try:
    if os.getenv("APPLICATIONINSIGHTS_CONNECTION_STRING"):
        from azure.monitor.opentelemetry import configure_azure_monitor

        configure_azure_monitor()
except Exception as exc:
    logger.warning("Application Insights configuration skipped: %s", exc)

settings = get_settings()
classifier = Classifier(settings)
store = build_store(settings)

app = FastAPI(title="AI Image Detection API", version="1.0.0")


def band_for(probability: float | None) -> str:
    if probability is None:
        return "Unknown"
    if probability >= settings.band_high:
        return "High"
    if probability >= settings.band_low:
        return "Medium"
    return "Low"


AI_SOURCE_TYPES = ("trainedalgorithmicmedia", "compositewithtrainedalgorithmicmedia")
TAMPER_CODES = ("datahash.mismatch", "claimsignature.mismatch", "boxeshash.mismatch")


def overall_indicator(ai_probability: float | None, band: str, c2pa: dict) -> dict:
    if c2pa.get("present"):
        state = (c2pa.get("validation_state") or "").lower()
        source = (c2pa.get("digital_source_type") or "").lower()
        issues = [str(item).lower() for item in c2pa.get("issues", [])]

        if any(token in source for token in AI_SOURCE_TYPES):
            if "compositewithtrainedalgorithmicmedia" in source:
                label = "AI-modified (provenance)"
            else:
                label = "AI-generated (provenance)"
            if "signingcredential.untrusted" in issues:
                label += " - signer unverified"
            return {"label": label, "severity": "High", "basis": "c2pa"}

        if state == "invalid" or any(token in issues for token in TAMPER_CODES):
            return {"label": "Provenance invalid - possible alteration", "severity": "Medium", "basis": "c2pa"}

    if ai_probability is None:
        return {"label": "Inconclusive", "severity": "Unknown", "basis": "none"}

    mapping = {
        "High": "Likely AI-generated (classifier)",
        "Medium": "Possible AI generation (classifier)",
        "Low": "No AI indication (classifier)",
    }
    return {"label": mapping.get(band, "Inconclusive"), "severity": band, "basis": "classifier"}


def build_evidence(ai_probability, band, overall, c2pa, meta, model_desc, thumbnail_stored) -> list[str]:
    evidence: list[str] = [
        f"Overall indicator: {overall['label']} (basis: {overall['basis']})."
    ]
    if ai_probability is None:
        evidence.append("AI-generation classifier did not return a score.")
    else:
        evidence.append(
            f"AI-generation classifier ({model_desc['name']}) scored "
            f"{ai_probability:.0%} likelihood of AI generation ({band})."
        )
    if c2pa.get("present"):
        line = f"C2PA Content Credentials present; validation state: {c2pa.get('validation_state')}."
        if c2pa.get("digital_source_type"):
            line += f" Declared digital source type: {c2pa['digital_source_type']}."
        if c2pa.get("issues"):
            line += " Validation codes: " + ", ".join(c2pa["issues"]) + "."
        evidence.append(line)
    elif c2pa.get("error"):
        evidence.append(f"C2PA Content Credentials could not be verified: {c2pa['error']}.")
    else:
        evidence.append("No C2PA Content Credentials present; provenance is not cryptographically embedded.")
    evidence.extend(meta.get("findings", []))
    if thumbnail_stored:
        evidence.append(
            "Only a downscaled thumbnail was persisted; the original image was processed in memory and discarded."
        )
    else:
        evidence.append("No image data was persisted by the detector.")
    return evidence


@app.get("/health")
def health() -> dict:
    return {
        "status": "ok",
        "processing_region": settings.region,
        "classifier": classifier.describe,
        "storage_enabled": store.enabled,
        "band_thresholds": {"low": settings.band_low, "high": settings.band_high},
        "original_persistence": settings.persist_original,
    }


@app.post("/analyze")
async def analyze(
    request: Request,
    x_filename: str | None = Header(default=None),
    x_email_subject: str | None = Header(default=None),
    x_email_sender: str | None = Header(default=None),
    x_email_received: str | None = Header(default=None),
    x_correlation_id: str | None = Header(default=None),
    x_api_key: str | None = Header(default=None),
):
    if settings.api_key and x_api_key != settings.api_key:
        raise HTTPException(status_code=401, detail="invalid or missing API key")

    content_type = request.headers.get("content-type", "")
    if content_type.startswith("multipart/form-data"):
        form = await request.form()
        upload = form.get("file")
        if upload is None or not hasattr(upload, "read"):
            raise HTTPException(status_code=400, detail="multipart body must include a 'file' field")
        raw = await upload.read()
        filename = getattr(upload, "filename", None) or x_filename or "attachment"
    else:
        raw = await request.body()
        filename = x_filename or "attachment"

    if not raw:
        raise HTTPException(status_code=400, detail="empty request body")
    if len(raw) > settings.max_image_bytes:
        raise HTTPException(status_code=413, detail="image exceeds the configured size limit")

    correlation_id = x_correlation_id or str(uuid.uuid4())
    processed_at = utcnow_iso()

    try:
        image = open_image(raw, settings.max_image_pixels)
    except ImageRejected as exc:
        raise HTTPException(status_code=422, detail=str(exc)) from exc

    width, height = image.size
    rgb = to_rgb(image)
    thumb_bytes, thumb_size = make_thumbnail(rgb, settings.thumbnail_max_px)
    image_sha256 = sha256_hex(raw)

    meta = metadata.analyse(image, raw)
    provenance = c2pa_check.verify(raw)
    try:
        ai_probability = classifier.score(rgb)
    except Exception as exc:
        logger.warning("classifier failure: %s", exc)
        ai_probability = None
    band = band_for(ai_probability)
    overall = overall_indicator(ai_probability, band, provenance)

    result = {
        "correlation_id": correlation_id,
        "processed_at_utc": processed_at,
        "processing_region": settings.region,
        "model": classifier.describe,
        "email": {
            "subject": x_email_subject,
            "sender": x_email_sender,
            "received_at_utc": x_email_received,
        },
        "image": {
            "filename": filename,
            "size_bytes": len(raw),
            "sha256": image_sha256,
            "width": width,
            "height": height,
            "thumbnail_size": {"width": thumb_size[0], "height": thumb_size[1]},
        },
        "detection": {
            "ai_probability": ai_probability,
            "band": band,
            "thresholds": {"low": settings.band_low, "high": settings.band_high},
        },
        "overall": overall,
        "provenance": {"c2pa": provenance},
        "metadata": meta,
        "storage": {"thumbnail_blob": None, "record_written": False, "original_persisted": False},
    }

    storage = result["storage"]
    try:
        storage["thumbnail_blob"] = store.save_thumbnail(image_sha256, thumb_bytes)
    except Exception as exc:
        logger.warning("thumbnail store failure: %s", exc)
        storage["error"] = str(exc)

    result["evidence"] = build_evidence(
        ai_probability, band, overall, provenance, meta, classifier.describe, bool(storage["thumbnail_blob"])
    )

    try:
        storage["record_written"] = store.write_record(result)
    except Exception as exc:
        logger.warning("record store failure: %s", exc)
        storage["error"] = str(exc)

    logger.info(
        "audit event correlation_id=%s processed_at=%s image_sha256=%s band=%s model=%s version=%s region=%s stored=%s",
        correlation_id,
        processed_at,
        image_sha256,
        band,
        classifier.describe["name"],
        classifier.describe["version"],
        settings.region,
        bool(storage["thumbnail_blob"]),
    )
    return JSONResponse(result)
