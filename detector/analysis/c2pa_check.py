import io
import json
import os

_DEFAULT_TRUST_FILE = os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "c2pa_trust", "C2PA-TRUST-LIST.pem"
)
_NO_MANIFEST_HINTS = ("no jumbf", "no claim", "not found", "no manifest", "missing", "unrecognized", "no supported")
_cache: dict = {"loaded": False, "pem": None, "path": None, "error": None, "context": None, "trusted": False}


def _load_trust() -> dict:
    if _cache["loaded"]:
        return _cache
    _cache["loaded"] = True
    path = os.getenv("C2PA_TRUST_FILE") or _DEFAULT_TRUST_FILE
    _cache["path"] = path
    try:
        with open(path, encoding="utf-8") as handle:
            _cache["pem"] = handle.read()
    except Exception as exc:
        _cache["error"] = str(exc)
        return _cache
    try:
        from c2pa import Context, Settings

        settings = Settings.from_dict(
            {"verify": {"verify_cert_anchors": True}, "trust": {"trust_anchors": _cache["pem"]}}
        )
        _cache["context"] = Context(settings)
        _cache["trusted"] = True
    except Exception as exc:
        _cache["error"] = str(exc)
    return _cache


def _walk(obj, key: str) -> list:
    found: list = []
    if isinstance(obj, dict):
        for k, v in obj.items():
            if k == key:
                found.append(v)
            found.extend(_walk(v, key))
    elif isinstance(obj, list):
        for item in obj:
            found.extend(_walk(item, key))
    return found


def _collect_status_codes(obj) -> list[str]:
    codes: list[str] = []
    for entry in _walk(obj, "validation_status"):
        items = entry if isinstance(entry, list) else [entry]
        for item in items:
            if isinstance(item, dict) and item.get("code"):
                codes.append(str(item["code"]))
            elif isinstance(item, str):
                codes.append(item)
    return sorted(set(codes))


def verify(raw: bytes) -> dict:
    result = {
        "present": False,
        "validation_state": None,
        "digital_source_type": None,
        "issues": [],
        "trust_configured": False,
        "error": None,
    }

    try:
        from c2pa import Reader
    except Exception as exc:
        result["error"] = f"c2pa library unavailable: {exc}"
        return result

    trust = _load_trust()
    result["trust_configured"] = trust["trusted"]

    try:
        with Reader(io.BytesIO(raw), context=trust["context"]) as reader:
            data = json.loads(reader.json())
    except Exception as exc:
        message = str(exc)
        lowered = message.lower()
        if "manifestnotfound" in type(exc).__name__.lower() or any(h in lowered for h in _NO_MANIFEST_HINTS):
            return result
        result["error"] = message
        return result

    result["present"] = True
    result["validation_state"] = str(
        data.get("validation_state") or ("invalid" if data.get("validation_status") else "unknown")
    )
    source_types = _walk(data, "digitalSourceType")
    if source_types:
        result["digital_source_type"] = str(source_types[0])
    result["issues"] = _collect_status_codes(data)
    return result
