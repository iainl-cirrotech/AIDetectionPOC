import os
from functools import lru_cache


def _bool(name: str, default: bool = False) -> bool:
    return os.getenv(name, str(default)).strip().lower() in {"1", "true", "yes", "on"}


class Settings:
    def __init__(self) -> None:
        self.region = os.getenv("AZURE_REGION", "uksouth")
        self.classifier_mode = os.getenv("DETECTOR_CLASSIFIER", "community_forensics").strip().lower()
        self.model_id = os.getenv(
            "DETECTOR_MODEL_ID",
            "Thermostatic/community-forensics-frontier-detector-2026-08",
        )
        self.model_revision = os.getenv(
            "DETECTOR_MODEL_REVISION",
            "16db135220b318d811b207db576d90368980b595",
        )
        self.model_version = os.getenv("DETECTOR_MODEL_VERSION", "frontier-2026-08")
        self.band_low = float(os.getenv("BAND_LOW_THRESHOLD", "0.35"))
        self.band_high = float(os.getenv("BAND_HIGH_THRESHOLD", "0.65"))
        self.max_image_bytes = int(os.getenv("MAX_IMAGE_BYTES", str(20 * 1024 * 1024)))
        self.max_image_pixels = int(os.getenv("MAX_IMAGE_PIXELS", str(80_000_000)))
        self.thumbnail_max_px = int(os.getenv("THUMBNAIL_MAX_PX", "512"))
        self.storage_account_url = os.getenv("STORAGE_ACCOUNT_URL", "").rstrip("/")
        self.store_backend = os.getenv("STORE_BACKEND", "azure").strip().lower()
        self.local_store_dir = os.getenv("LOCAL_STORE_DIR", "/tmp/aidetect-store")
        self.thumbnail_container = os.getenv("THUMBNAIL_CONTAINER", "thumbnails")
        self.results_table = os.getenv("RESULTS_TABLE", "detections")
        self.persist_original = _bool("PERSIST_ORIGINAL_IMAGE", False)
        self.api_key = os.getenv("DETECTOR_API_KEY", "")
        self.correlation_header = os.getenv("CORRELATION_HEADER", "x-correlation-id")

    @property
    def blob_endpoint(self) -> str:
        return self.storage_account_url

    @property
    def table_endpoint(self) -> str:
        if not self.storage_account_url:
            return ""
        return self.storage_account_url.replace(".blob.", ".table.")


@lru_cache
def get_settings() -> Settings:
    return Settings()
