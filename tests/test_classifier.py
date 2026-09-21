import sys
import unittest
from pathlib import Path
from types import SimpleNamespace


ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "detector"))

from analysis.classifier import Classifier  # noqa: E402


def settings(mode: str):
    return SimpleNamespace(
        classifier_mode=mode,
        model_id="test-model",
        model_version="test-version",
        model_revision="test-revision",
    )


class ClassifierTests(unittest.TestCase):
    def test_disabled_classifier_returns_no_score(self):
        classifier = Classifier(settings("disabled"))
        self.assertIsNone(classifier.score(object()))

    def test_mock_classifier_is_deterministic(self):
        classifier = Classifier(settings("mock"))
        image = SimpleNamespace(tobytes=lambda: b"representative image bytes")
        first = classifier.score(image)
        second = classifier.score(image)
        self.assertEqual(first, second)
        self.assertGreaterEqual(first, 0.0)
        self.assertLessEqual(first, 1.0)

    def test_huggingface_label_mapping_prefers_explicit_ai_label(self):
        predictions = [
            {"label": "AI-generated", "score": 0.8},
            {"label": "human", "score": 0.2},
        ]
        self.assertAlmostEqual(Classifier._ai_probability(predictions), 0.8)

    def test_huggingface_label_mapping_inverts_human_only_score(self):
        predictions = [{"label": "real", "score": 0.9}]
        self.assertAlmostEqual(Classifier._ai_probability(predictions), 0.1)


if __name__ == "__main__":
    unittest.main()
