"""Reproducible test inputs; no photographic detections are claimed."""

from pathlib import Path
from PIL import Image, ImageDraw

from .model import Athlete, Rect


def athletes(count: int) -> list[Athlete]:
    result = []
    for i in range(count):
        height = 620 + (i * 37 % 220)
        result.append(Athlete(f"athlete-{i:03}", (400, 1000),
                              Rect(90, 1000 - height, 310, 970),
                              Rect(155, 1000 - height, 245, 1090 - height),
                              float(height), 1.0))
    return result


def write_sources(folder: Path, people: list[Athlete]) -> dict[str, Path]:
    folder.mkdir(parents=True, exist_ok=True)
    sources = {}
    for i, a in enumerate(people):
        path = folder / f"{a.id}.png"
        with Image.new("RGBA", a.image_size) as image:
            draw = ImageDraw.Draw(image)
            draw.rectangle((120, a.head.bottom - 4, 280, a.body.bottom),
                           fill=(40 + i * 29 % 180, 70 + i * 17 % 150, 160, 255))
            draw.ellipse((a.head.left, a.head.top, a.head.right, a.head.bottom), fill="#d7ab87")
            image.save(path)
        sources[a.id] = path
    return sources
