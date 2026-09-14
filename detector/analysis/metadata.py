from PIL import Image
from PIL.ExifTags import TAGS

EDITOR_HINTS = (
    "photoshop",
    "gimp",
    "lightroom",
    "affinity",
    "pixelmator",
    "capture one",
    "snapseed",
    "picsart",
    "canva",
    "midjourney",
    "stable diffusion",
    "dall",
    "firefly",
    "openai",
    "bing image creator",
    "generative",
    "diffusion",
)

C2PA_MARKERS = (b"c2pa", b"jumb", b"c2pa.claim")


def _tag_map(exif) -> dict:
    out = {}
    for tag_id, value in exif.items():
        out[TAGS.get(tag_id, str(tag_id))] = value
    return out


def analyse(img: Image.Image, raw: bytes) -> dict:
    findings: list[str] = []
    result = {
        "exif_present": False,
        "xmp_present": False,
        "c2pa_marker_present": False,
        "camera": None,
        "software": None,
        "findings": findings,
    }

    lowered = raw.lower()
    result["c2pa_marker_present"] = any(m in lowered for m in C2PA_MARKERS)

    info = getattr(img, "info", {}) or {}
    result["xmp_present"] = "xmp" in info or b"ns.adobe.com/xap" in raw

    try:
        exif = img.getexif()
    except Exception:
        exif = None

    if exif:
        tags = _tag_map(exif)
        result["exif_present"] = True
        make = tags.get("Make")
        model = tags.get("Model")
        software = tags.get("Software")
        if make or model:
            result["camera"] = " ".join(str(p) for p in (make, model) if p).strip()
        if software:
            result["software"] = str(software)

    if result["camera"]:
        findings.append(f"Camera metadata present: {result['camera']}.")
    elif result["exif_present"]:
        findings.append("EXIF present but no camera make/model recorded.")

    if result["software"]:
        software_l = result["software"].lower()
        if any(h in software_l for h in EDITOR_HINTS):
            findings.append(f"Editing or generation software tag detected: {result['software']}.")
        else:
            findings.append(f"Software tag present: {result['software']}.")

    if not result["exif_present"]:
        findings.append(
            "No EXIF metadata present; common for screenshots, messaging re-encodes, stripped files or generated images."
        )

    if result["xmp_present"]:
        findings.append("XMP metadata present.")

    if result["c2pa_marker_present"]:
        findings.append("C2PA/JUMBF marker found in file bytes; see provenance result.")

    return result
