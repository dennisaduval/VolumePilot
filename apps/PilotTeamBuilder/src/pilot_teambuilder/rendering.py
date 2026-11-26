"""Bounded template caching and serial full-resolution compositing."""

from collections import OrderedDict
from contextlib import closing
import os
from pathlib import Path
import re
import tempfile
from time import perf_counter

from PIL import Image, ImageOps

from .identification import JobProfile, draw_right_footer
from .model import ART_SIZE, FINAL_SIZE, Layout, Rect


def discover_templates(root: Path) -> tuple[Path, Path]:
    layers = {}
    pattern = re.compile(r"^Team[ _-]+(Background|Foreground)_default$", re.I)
    for path in sorted(root.iterdir()):
        match = pattern.fullmatch(path.stem)
        if not match or not path.is_file():
            continue
        role = match[1].lower()
        if path.suffix.lower() not in ({".jpg", ".jpeg"} if role == "background" else {".png"}):
            continue
        if role in layers:
            raise ValueError(f"Duplicate default team {role}.")
        layers[role] = path
    if set(layers) != {"background", "foreground"}:
        raise ValueError("Select a Team Background_default.jpg and Team Foreground_default.png.")
    return layers["background"], layers["foreground"]


class TemplateCache:
    """Two decoded artwork layers, owned by one rendering worker."""

    def __init__(self):
        self.layers = OrderedDict()

    def load(self, path: Path, foreground: bool) -> Image.Image:
        stat = path.stat()
        key = (str(path.resolve()), stat.st_size, stat.st_mtime_ns, foreground)
        if key in self.layers:
            self.layers.move_to_end(key)
            return self.layers[key]
        with Image.open(path) as opened, closing(ImageOps.exif_transpose(opened)) as oriented:
            if oriented.width != oriented.height * 3:
                raise ValueError(f"{path.name}: expected 3:1 landscape artwork.")
            if foreground:
                if opened.format != "PNG" or "A" not in oriented.getbands():
                    raise ValueError("Foreground must be an alpha-channel PNG.")
                with closing(oriented.getchannel("A")) as alpha:
                    if alpha.getextrema()[0] == 255:
                        raise ValueError("Foreground has no transparent pixels.")
            elif opened.format != "JPEG":
                raise ValueError("Background must be JPEG.")
            converted = oriented.convert("RGBA")
            try:
                layer = converted.resize(ART_SIZE, Image.Resampling.LANCZOS)
            finally:
                converted.close()
        self.layers[key] = layer
        while len(self.layers) > 2:
            self.layers.popitem(last=False)[1].close()
        return layer

    def close(self):
        for image in self.layers.values():
            image.close()
        self.layers.clear()


def render(layout: Layout, sources: dict[str, Path], background: Path, foreground: Path,
           team_name: str, destination: Path, cache: TemplateCache | None = None,
           font: str = "") -> dict:
    """Render one draft/final artifact. Issues must block routing, not preview.

    Exclusive publication refuses overwrites. This milestone has no printer or
    hotfolder side effects. The caller owns any supplied cache.
    """
    if not team_name.strip() or len(team_name) > 240 or any(ord(c) < 32 for c in team_name):
        raise ValueError("Team name must contain 1–240 printable characters.")
    if set(sources) != {p.athlete_id for p in layout.placements}:
        raise ValueError("Source mapping must match the included athlete IDs exactly.")
    if destination.exists():
        raise FileExistsError(destination)
    own_cache = cache is None
    cache = cache or TemplateCache()
    start = perf_counter()
    timings = {}
    temporary = None
    try:
        bg, fg = cache.load(background, False), cache.load(foreground, True)
        timings["templates_seconds"] = perf_counter() - start
        with closing(Image.new("RGBA", FINAL_SIZE, "white")) as canvas:
            canvas.paste(bg, (0, 0))
            last = perf_counter()
            for p in layout.placements:
                with Image.open(sources[p.athlete_id]) as opened, closing(ImageOps.exif_transpose(opened)) as image:
                    if opened.format != "PNG" or "A" not in image.getbands():
                        raise ValueError(f"{p.athlete_id}: expected extracted alpha-channel PNG.")
                    if image.size != p.source_size:
                        raise ValueError(f"{p.athlete_id}: source dimensions changed; rebuild layout.")
                    with closing(image.getchannel("A")) as alpha:
                        bounds = alpha.getbbox()
                    if bounds is None or not p.source_body.contains(Rect(*bounds), tolerance=1):
                        raise ValueError(f"{p.athlete_id}: alpha bounds differ from layout; rebuild geometry.")
                    size = max(1, round(image.width * p.scale)), max(1, round(image.height * p.scale))
                    # One athlete at a time bounds memory independently of team size.
                    with closing(image.convert("RGBA")) as rgba, closing(rgba.resize(size, Image.Resampling.LANCZOS)) as resized:
                        canvas.alpha_composite(resized, (round(p.x), round(p.y)))
            canvas.alpha_composite(fg, (0, 0))
            timings["composite_seconds"] = perf_counter() - last
            draw_right_footer(canvas, team_name, JobProfile(footer_font=font))
            destination.parent.mkdir(parents=True, exist_ok=True)
            fd, temporary = tempfile.mkstemp(prefix=".team-", suffix=".jpg", dir=destination.parent)
            with os.fdopen(fd, "wb") as handle, closing(canvas.convert("RGB")) as rgb:
                last = perf_counter()
                rgb.save(handle, "JPEG", quality=95, subsampling=0, dpi=(300, 300), optimize=True)
                handle.flush()
                os.fsync(handle.fileno())
                timings["jpeg_seconds"] = perf_counter() - last
        with Image.open(temporary) as check:
            check.load()
            if check.size != FINAL_SIZE or check.format != "JPEG":
                raise ValueError("Output verification failed.")
        # Hard-link publication is atomic and refuses replacing an existing
        # file. Unsupported network filesystems fail explicitly for later retry.
        os.link(temporary, destination)
        timings["total_seconds"] = perf_counter() - start
        return {"size": FINAL_SIZE, "ppi": 300, "timings": timings,
                "review_required": bool(layout.issues), "issues": layout.issues}
    finally:
        if temporary is not None:
            Path(temporary).unlink(missing_ok=True)
        if own_cache:
            cache.close()
