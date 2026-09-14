import hashlib
import io

from PIL import Image, UnidentifiedImageError


class ImageRejected(ValueError):
    pass


def sha256_hex(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def open_image(data: bytes, max_pixels: int) -> Image.Image:
    Image.MAX_IMAGE_PIXELS = max_pixels
    try:
        img = Image.open(io.BytesIO(data))
        img.load()
    except (UnidentifiedImageError, OSError, Image.DecompressionBombError, ValueError) as exc:
        raise ImageRejected(f"not a decodable image: {exc}") from exc
    width, height = img.size
    if width <= 0 or height <= 0:
        raise ImageRejected("image has zero dimension")
    if width * height > max_pixels:
        raise ImageRejected("image exceeds the configured pixel budget")
    return img


def to_rgb(img: Image.Image) -> Image.Image:
    return img.convert("RGB")


def make_thumbnail(img: Image.Image, max_px: int) -> tuple[bytes, tuple[int, int]]:
    thumb = img.copy()
    thumb.thumbnail((max_px, max_px), Image.LANCZOS)
    buf = io.BytesIO()
    thumb.save(buf, format="JPEG", quality=85, optimize=True)
    return buf.getvalue(), thumb.size
