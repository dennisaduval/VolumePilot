"""Serializable geometry contracts. Source coordinates are oriented pixels."""

from dataclasses import dataclass, field
from math import isfinite

PPI = 300
FINAL_SIZE = (7800, 2400)
ART_SIZE = (7200, 2400)


@dataclass(frozen=True)
class Rect:
    left: float
    top: float
    right: float
    bottom: float

    def __post_init__(self):
        if not all(isfinite(v) for v in (self.left, self.top, self.right, self.bottom)):
            raise ValueError("Rectangle coordinates must be finite.")
        if self.width <= 0 or self.height <= 0:
            raise ValueError("Rectangle must have positive dimensions.")

    @property
    def width(self):
        return self.right - self.left

    @property
    def height(self):
        return self.bottom - self.top

    @property
    def center_x(self):
        return (self.left + self.right) / 2

    def contains(self, other: "Rect", tolerance=1e-7):
        return (self.left - tolerance <= other.left and self.top - tolerance <= other.top
                and self.right + tolerance >= other.right and self.bottom + tolerance >= other.bottom)

    def overlaps(self, other: "Rect"):
        return (self.left < other.right and self.right > other.left
                and self.top < other.bottom and self.bottom > other.top)


@dataclass(frozen=True)
class Athlete:
    id: str
    image_size: tuple[int, int]
    body: Rect
    head: Rect | None
    relative_height: float | None
    # Pixels in this extraction / pixels in the common original capture.
    # None means crop/resizing history is unknown; never infer from PNG height.
    source_scale: float | None
    confidence: float = 1.0
    head_count: int = 1


@dataclass(frozen=True)
class CompositionSettings:
    left_in: float = 1.0
    top_in: float = 0.75
    width_in: float = 22.0
    height_in: float = 6.0
    head_spacing_in: float = 0.625
    row_separation_heads: float = 1.5
    v_step_in: float = 0.025
    v_max_drop_in: float = 0.25
    group_scale: float = 1.0
    x_offset_in: float = 0.0
    y_offset_in: float = 0.0

    def validate(self):
        if not all(isfinite(v) for v in vars(self).values()):
            raise ValueError("Composition settings must be finite.")
        if min(self.width_in, self.height_in, self.head_spacing_in, self.row_separation_heads) <= 0:
            raise ValueError("Placement dimensions and spacing must be positive.")
        if not 0 < self.group_scale <= 1 or min(self.v_step_in, self.v_max_drop_in) < 0:
            raise ValueError("Group scale must be in (0,1]; inverted-V values cannot be negative.")
        if not Rect(0, 0, *ART_SIZE).contains(self.area):
            raise ValueError("Athlete placement area must be inside the 24 × 8 artwork.")

    @property
    def area(self):
        return Rect(self.left_in * PPI, self.top_in * PPI,
                    (self.left_in + self.width_in) * PPI, (self.top_in + self.height_in) * PPI)


@dataclass(frozen=True)
class Corrections:
    # Explicit back-to-front, left-to-right IDs override automatic assignment.
    rows: tuple[tuple[str, ...], ...] | None = None
    excluded: tuple[str, ...] = ()
    offsets_in: dict[str, tuple[float, float]] = field(default_factory=dict)


@dataclass(frozen=True)
class Placement:
    athlete_id: str
    row: int
    slot: int
    scale: float
    x: float
    y: float
    body: Rect
    head: Rect
    source_size: tuple[int, int]
    source_body: Rect


@dataclass(frozen=True)
class Layout:
    placements: tuple[Placement, ...]  # In paint order, back to front.
    row_counts: tuple[int, ...]
    global_scale: float
    issues: tuple[str, ...]
    schema_version: int = 1


class ReviewRequired(ValueError):
    """An input cannot safely be laid out without correction."""
