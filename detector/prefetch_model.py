import os
import sys

from config import get_settings


def main() -> int:
    settings = get_settings()
    if settings.classifier_mode != "hf":
        print(f"classifier mode is '{settings.classifier_mode}'; nothing to prefetch")
        return 0

    from transformers import AutoImageProcessor, AutoModelForImageClassification

    kwargs = {"revision": settings.model_revision} if settings.model_revision else {}
    print(f"prefetching {settings.model_id} into {os.getenv('HF_HOME', 'default cache')}")
    AutoImageProcessor.from_pretrained(settings.model_id, **kwargs)
    AutoModelForImageClassification.from_pretrained(settings.model_id, **kwargs)
    print("prefetch complete")
    return 0


if __name__ == "__main__":
    sys.exit(main())
