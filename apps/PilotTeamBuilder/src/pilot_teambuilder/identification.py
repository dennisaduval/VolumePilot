"""Adapted from Pilot-PanoBuilder print_layouts.py at 58da7ef (see docs)."""

from contextlib import closing
from dataclasses import dataclass
from PIL import Image, ImageDraw, ImageFont

LANDSCAPE_SIZE = (7800, 2400)
LANDSCAPE_FOOTER_WIDTH = FOOTER_HEIGHT = 600
LANDSCAPE_PRINT_WIDTH = 7200

@dataclass(frozen=True)
class JobProfile:
    footer_max_size: int = 180
    footer_font: str = ""


def _font_candidates(chosen: str = "") -> list[str]:
    return [
        chosen,
        "/System/Library/Fonts/Supplemental/Arial Bold.ttf",
        "/System/Library/Fonts/Helvetica.ttc",
        "/Library/Fonts/Arial Bold.ttf",
    ]


def _load_font(size: int, chosen: str = "") -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    for candidate in _font_candidates(chosen):
        if candidate:
            try:
                return ImageFont.truetype(candidate, size=size, index=1 if candidate.endswith(".ttc") else 0)
            except (OSError, ValueError):
                continue
    return ImageFont.load_default(size=max(10, size))


def _fit_font(
    label: str, max_width: int, max_height: int, preferred_max: int, chosen: str = "",
) -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    with closing(Image.new("RGB", (1, 1))) as scratch:
        draw = ImageDraw.Draw(scratch)
        low, high = 8, max(8, preferred_max)
        best = _load_font(low, chosen)
        while low <= high:
            size = (low + high) // 2
            font = _load_font(size, chosen)
            left, top, right, bottom = draw.textbbox((0, 0), label, font=font, stroke_width=0)
            if right - left <= max_width and bottom - top <= max_height:
                best = font
                low = size + 1
            else:
                high = size - 1
        return best


def _draw_centered_label(
    image: Image.Image, label: str, box: tuple[int, int, int, int],
    profile: JobProfile, preferred_max: int | None = None,
) -> None:
    left, top, right, bottom = box
    draw = ImageDraw.Draw(image)
    draw.rectangle(box, fill="white")
    margin_x = max(6, int((right - left) * 0.075))
    margin_y = max(6, int((bottom - top) * 0.125))
    font = _fit_font(
        label,
        max_width=max(1, right - left - margin_x * 2),
        max_height=max(1, bottom - top - margin_y * 2),
        preferred_max=preferred_max or profile.footer_max_size,
        chosen=profile.footer_font,
    )
    draw.text(((left + right) / 2, (top + bottom) / 2), label, fill="black", font=font, anchor="mm")


def draw_footer(image: Image.Image, label: str, profile: JobProfile) -> None:
    width, height = image.size
    _draw_centered_label(image, label, (0, height - FOOTER_HEIGHT, width, height), profile)


def draw_right_footer(image: Image.Image, label: str, profile: JobProfile) -> None:
    with closing(Image.new("RGB", (LANDSCAPE_SIZE[1], LANDSCAPE_FOOTER_WIDTH), "white")) as strip:
        _draw_centered_label(
            strip, label, (0, 0, strip.width, strip.height), profile,
            preferred_max=profile.footer_max_size,
        )
        with closing(strip.rotate(90, expand=True)) as rotated:
            image.paste(rotated, (LANDSCAPE_PRINT_WIDTH, 0))
