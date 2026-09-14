from pathlib import Path

from PIL import Image, ImageDraw

OUT = Path(__file__).resolve().parent.parent / "samples"


def plain() -> Image.Image:
    img = Image.new("RGB", (640, 480), (40, 90, 160))
    draw = ImageDraw.Draw(img)
    draw.ellipse((180, 120, 460, 360), fill=(240, 200, 60))
    return img


def edited() -> Image.Image:
    img = Image.new("RGB", (480, 360), (30, 30, 30))
    draw = ImageDraw.Draw(img)
    draw.rectangle((60, 60, 420, 300), outline=(200, 200, 200), width=3)
    return img


def main() -> None:
    OUT.mkdir(exist_ok=True)
    plain().save(OUT / "sample-plain.png")

    exif = Image.Exif()
    exif[271] = "Canon"
    exif[272] = "EOS R5"
    exif[305] = "Adobe Photoshop 25.0"
    edited().save(OUT / "sample-edited.jpg", exif=exif)

    print(f"wrote samples to {OUT}")


if __name__ == "__main__":
    main()
