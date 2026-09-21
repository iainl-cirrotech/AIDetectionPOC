"""Inference adapter for Community Forensics-compatible checkpoints.

The upstream model uses a custom PyTorch class rather than the standard
Transformers image-classification API. Keeping the small architecture adapter
here loads compatible safetensors checkpoints without executing remote code.
"""

from __future__ import annotations

import json

from PIL import Image


class CommunityForensicsDetector:
    input_size = 384

    def __init__(self, model_id: str, revision: str = "") -> None:
        import timm
        import torch
        from huggingface_hub import hf_hub_download
        from safetensors.torch import load_file
        from torch import nn
        from torchvision import transforms

        self._torch = torch
        self._device = torch.device("cuda" if torch.cuda.is_available() else "cpu")

        class ViTClassifier(nn.Module):
            """Checkpoint-compatible subset of the authors' model class."""

            def __init__(self) -> None:
                super().__init__()
                # The complete checkpoint is loaded below, so downloading
                # timm's ImageNet weights is unnecessary at runtime.
                self.vit = timm.create_model(
                    "vit_small_patch16_384.augreg_in21k_ft_in1k",
                    pretrained=False,
                )
                self.vit.head = nn.Linear(in_features=384, out_features=1, bias=True)

            def forward(self, inputs):
                return self.vit(inputs)

        model = ViTClassifier()

        download_kwargs = {
            "repo_id": model_id,
            "filename": "model.safetensors",
        }
        if revision:
            download_kwargs["revision"] = revision
        checkpoint = hf_hub_download(**download_kwargs)
        state_dict = load_file(checkpoint, device="cpu")
        # The official upstream checkpoint includes the authors' ``vit.``
        # wrapper prefix. Some compatible fine-tunes publish the inner timm
        # state dict directly; support both without weakening strict loading.
        if state_dict and all(key.startswith("vit.") for key in state_dict):
            model.load_state_dict(state_dict, strict=True)
        else:
            model.vit.load_state_dict(state_dict, strict=True)
        self._model = model.to(self._device).eval()

        config_kwargs = {"repo_id": model_id, "filename": "config.json"}
        if revision:
            config_kwargs["revision"] = revision
        config_path = hf_hub_download(**config_kwargs)
        with open(config_path, encoding="utf-8") as handle:
            model_config = json.load(handle)
        calibration = model_config.get("calibration", {})
        self._calibration_slope = float(calibration.get("slope", 1.0))
        self._calibration_intercept = float(calibration.get("intercept", 0.0))

        # Exact evaluation transform from the official Community Forensics
        # dataloader: resize short edge to 440, centre crop 384, ImageNet norm.
        self._transform = transforms.Compose(
            [
                transforms.Resize(440),
                transforms.CenterCrop(self.input_size),
                transforms.ToTensor(),
                transforms.Normalize(
                    mean=[0.485, 0.456, 0.406],
                    std=[0.229, 0.224, 0.225],
                ),
            ]
        )

    @property
    def device(self) -> str:
        return str(self._device)

    def score(self, image: Image.Image) -> float:
        inputs = self._transform(image.convert("RGB")).unsqueeze(0).to(self._device)
        with self._torch.inference_mode():
            raw_logit = self._model(inputs).flatten()[0]
            calibrated_logit = raw_logit * self._calibration_slope + self._calibration_intercept
            probability = self._torch.sigmoid(calibrated_logit)
        return float(probability.cpu().item())
