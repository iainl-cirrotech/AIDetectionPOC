import json
from datetime import datetime, timezone
from pathlib import Path


def utcnow_iso() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def flatten_result(result: dict) -> dict:
    email = result.get("email", {})
    image = result.get("image", {})
    detection = result.get("detection", {})
    overall = result.get("overall", {})
    c2pa = result.get("provenance", {}).get("c2pa", {})
    model = result.get("model", {})
    meta = result.get("metadata", {})
    storage = result.get("storage", {})
    return {
        "correlation_id": result.get("correlation_id", ""),
        "processed_at_utc": result.get("processed_at_utc", ""),
        "received_at_utc": email.get("received_at_utc") or "",
        "subject": email.get("subject") or "",
        "sender": email.get("sender") or "",
        "filename": image.get("filename") or "",
        "image_sha256": image.get("sha256") or "",
        "thumbnail_blob": storage.get("thumbnail_blob") or "",
        "ai_probability": detection.get("ai_probability"),
        "band": detection.get("band") or "",
        "overall_label": overall.get("label") or "",
        "overall_severity": overall.get("severity") or "",
        "overall_basis": overall.get("basis") or "",
        "c2pa_present": bool(c2pa.get("present")),
        "c2pa_state": c2pa.get("validation_state") or "",
        "c2pa_trust_configured": bool(c2pa.get("trust_configured")),
        "digital_source_type": c2pa.get("digital_source_type") or "",
        "model_name": model.get("name") or "",
        "model_version": model.get("version") or "",
        "processing_region": result.get("processing_region") or "",
        "evidence_json": json.dumps(result.get("evidence", [])),
        "findings_json": json.dumps(meta.get("findings", [])),
    }


class NullStore:
    enabled = False

    def save_thumbnail(self, sha256: str, data: bytes) -> str | None:
        return None

    def write_record(self, result: dict) -> bool:
        return False


class FileStore:
    enabled = True

    def __init__(self, directory: str) -> None:
        self.directory = Path(directory)
        (self.directory / "thumbnails").mkdir(parents=True, exist_ok=True)

    def save_thumbnail(self, sha256: str, data: bytes) -> str | None:
        name = f"{sha256}.jpg"
        (self.directory / "thumbnails" / name).write_bytes(data)
        return name

    def write_record(self, result: dict) -> bool:
        flat = flatten_result(result)
        with (self.directory / "detections.jsonl").open("a", encoding="utf-8") as handle:
            handle.write(json.dumps(flat) + "\n")
        return True


class AzureStore:
    enabled = True

    def __init__(self, settings) -> None:
        from azure.data.tables import TableServiceClient
        from azure.identity import DefaultAzureCredential
        from azure.storage.blob import BlobServiceClient

        self.settings = settings
        credential = DefaultAzureCredential()
        self._blob = BlobServiceClient(account_url=settings.blob_endpoint, credential=credential)
        self._table = TableServiceClient(endpoint=settings.table_endpoint, credential=credential)

    def save_thumbnail(self, sha256: str, data: bytes) -> str | None:
        from azure.storage.blob import ContentSettings

        blob_name = f"{sha256}.jpg"
        client = self._blob.get_blob_client(container=self.settings.thumbnail_container, blob=blob_name)
        client.upload_blob(
            data,
            overwrite=True,
            content_settings=ContentSettings(content_type="image/jpeg"),
        )
        return blob_name

    def write_record(self, result: dict) -> bool:
        from azure.data.tables import UpdateMode

        entity = flatten_result(result)
        entity["PartitionKey"] = result["processed_at_utc"][:10].replace("-", "")
        entity["RowKey"] = result["correlation_id"]
        client = self._table.get_table_client(self.settings.results_table)
        client.upsert_entity(entity=entity, mode=UpdateMode.REPLACE)
        return True


def build_store(settings):
    backend = settings.store_backend
    if backend == "file":
        return FileStore(settings.local_store_dir)
    if backend == "azure" and settings.storage_account_url:
        return AzureStore(settings)
    return NullStore()
