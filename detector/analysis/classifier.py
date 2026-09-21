import hashlib

NEGATIVE_HINTS = ("human", "real", "authentic", "camera", "photo", "natural")
POSITIVE_HINTS = ("ai", "fake", "artificial", "synthetic", "generated", "deepfake")


class Classifier:
    def __init__(self, settings) -> None:
        self.mode = settings.classifier_mode
        self.model_id = settings.model_id
        self.model_version = settings.model_version
        self.model_revision = settings.model_revision
        self._pipe = None
        self._community_forensics = None

        if self.mode in {"community_forensics", "commfor"}:
            from analysis.community_forensics import CommunityForensicsDetector

            self._community_forensics = CommunityForensicsDetector(
                self.model_id,
                self.model_revision,
            )
        elif self.mode == "hf":
            from transformers import pipeline

            kwargs = {"model": self.model_id}
            if self.model_revision:
                kwargs["revision"] = self.model_revision
            self._pipe = pipeline("image-classification", **kwargs)

    @property
    def describe(self) -> dict:
        description = {
            "mode": self.mode,
            "name": self.model_id,
            "version": self.model_version,
            "revision": self.model_revision or "default",
        }
        if self._community_forensics is not None:
            description["device"] = self._community_forensics.device
            description["architecture"] = "Community Forensics ViT-S/16 (384px)"
        return description

    def score(self, image) -> float | None:
        if self.mode == "disabled":
            return None
        if self.mode == "mock":
            digest = hashlib.sha256(image.tobytes()[:4096]).digest()
            return int.from_bytes(digest[:4], "big") / 0xFFFFFFFF
        if self._community_forensics is not None:
            return self._community_forensics.score(image)
        predictions = self._pipe(image)
        return self._ai_probability(predictions)

    @staticmethod
    def _ai_probability(predictions) -> float | None:
        ai = 0.0
        human = 0.0
        for prediction in predictions:
            label = str(prediction.get("label", "")).lower()
            score = float(prediction.get("score", 0.0))
            if any(hint in label for hint in NEGATIVE_HINTS):
                human += score
            elif any(hint in label for hint in POSITIVE_HINTS):
                ai += score
        if ai and not human:
            return min(ai, 1.0)
        if human and not ai:
            return max(0.0, 1.0 - human)
        if ai + human > 0:
            return ai / (ai + human)
        return None
