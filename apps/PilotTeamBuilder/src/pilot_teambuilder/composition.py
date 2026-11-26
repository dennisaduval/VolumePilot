"""Deterministic pure layout; never decodes images or changes source files."""

from math import isfinite
from statistics import median

from .model import (Athlete, CompositionSettings, Corrections, Layout, PPI,
                    Placement, Rect, ReviewRequired)
from .rows import RowTable, validate_rows


def center_out_slots(count: int) -> tuple[int, ...]:
    """Ranks 1,2,... map to center,left,right,...; two centers for even rows."""
    center = (count - 1) / 2
    return tuple(sorted(range(count), key=lambda i: (abs(i - center), i)))


def transform(box: Rect, scale: float, x: float, y: float) -> Rect:
    return Rect(x + box.left * scale, y + box.top * scale,
                x + box.right * scale, y + box.bottom * scale)


def compose(athletes: list[Athlete], table: RowTable,
            settings: CompositionSettings = CompositionSettings(),
            corrections: Corrections = Corrections()) -> Layout:
    settings.validate()
    ids = [a.id for a in athletes]
    if len(set(ids)) != len(ids) or any(not i for i in ids):
        raise ValueError("Athlete IDs must be unique and nonempty.")
    if len(set(corrections.excluded)) != len(corrections.excluded):
        raise ValueError("Duplicate exclusions.")
    if (set(corrections.excluded) | set(corrections.offsets_in)) - set(ids):
        raise ValueError("Corrections reference unknown athletes.")
    active = [a for a in athletes if a.id not in corrections.excluded]
    if not active:
        raise ReviewRequired("No included athletes.")
    for a in active:
        if (len(a.image_size) != 2 or any(type(v) is not int or v <= 0 for v in a.image_size)
                or not Rect(0, 0, *a.image_size).contains(a.body)):
            raise ValueError(f"{a.id}: invalid source dimensions/body bounds.")
        if a.head is None or a.head_count != 1:
            raise ReviewRequired(f"{a.id}: expected one head; correct detection or exclude explicitly.")
        if not a.body.contains(a.head):
            raise ReviewRequired(f"{a.id}: head lies outside visible body bounds.")
        if not isfinite(a.confidence) or not 0.8 <= a.confidence <= 1:
            raise ReviewRequired(f"{a.id}: unreliable detection confidence.")
        for value, label in ((a.source_scale, "capture-scale metadata"),
                             (a.relative_height, "relative height")):
            if value is None or not isfinite(value) or value <= 0:
                raise ReviewRequired(f"{a.id}: missing or invalid {label}.")
    ranked = sorted(active, key=lambda a: (-a.relative_height, a.id))
    if corrections.rows is not None:
        flat = [i for row in corrections.rows for i in row]
        if sorted(flat) != sorted(a.id for a in active):
            raise ValueError("Manual rows must include every active athlete exactly once.")
        counts = tuple(len(row) for row in corrections.rows)
        validate_rows(counts, len(active))
        by_id = {a.id: a for a in active}
        rows = [[by_id[i] for i in row] for row in corrections.rows]
    else:
        counts = table.for_count(len(active))
        rows = []
        start = 0
        for count in counts:
            ordered = [None] * count
            for slot, a in zip(center_out_slots(count), ranked[start:start + count]):
                ordered[slot] = a
            rows.append(ordered)
            start += count
    # Work in common capture coordinates. Extraction resizing is undone before
    # applying ONE team scale; no per-athlete head-size normalization occurs.
    head_height = median(a.head.height / a.source_scale for a in active)
    typical_body = median(a.body.height / a.source_scale for a in active)
    natural_scale = settings.height_in * PPI / typical_body
    # Treat requested head spacing as a minimum. Wide heads must not overlap
    # simply because the default spacing was chosen for a smaller age group.
    spacing = max(settings.head_spacing_in * PPI / natural_scale,
                  max(a.head.width / a.source_scale for a in active) * 1.1)
    equal_rows = len(rows) > 1 and len(set(counts)) == 1
    raw = []
    for row_index, row in enumerate(rows):
        center = (len(row) - 1) / 2
        stagger = (0.5 * spacing if row_index % 2 else 0) if equal_rows else 0
        for slot, a in enumerate(row):
            source_scale = 1 / a.source_scale
            head_x = (slot - center) * spacing + stagger
            edge_distance = abs(slot - center) - (0.5 if len(row) % 2 == 0 else 0)
            drop = min(settings.v_max_drop_in, edge_distance * settings.v_step_in) * PPI / natural_scale
            head_y = row_index * head_height * settings.row_separation_heads + drop
            x = head_x - a.head.center_x * source_scale
            y = head_y - a.head.top * source_scale
            raw.append((a, row_index, slot, source_scale, x, y,
                        transform(a.body, source_scale, x, y)))
    left = min(r[-1].left for r in raw)
    top = min(r[-1].top for r in raw)
    right = max(r[-1].right for r in raw)
    bottom = max(r[-1].bottom for r in raw)
    area = settings.area
    available_w = area.width - 2 * abs(settings.x_offset_in * PPI)
    available_h = area.height - 2 * abs(settings.y_offset_in * PPI)
    if min(available_w, available_h) <= 0:
        raise ValueError("Group offset leaves no placement space.")
    global_scale = min(natural_scale, available_w / (right - left),
                       available_h / (bottom - top)) * settings.group_scale
    origin_x = area.center_x + settings.x_offset_in * PPI - (left + right) / 2 * global_scale
    origin_y = (area.top + area.bottom) / 2 + settings.y_offset_in * PPI - (top + bottom) / 2 * global_scale
    placements = []
    issues = []
    # Outside first; lower height rank wins ties so the taller center paints last.
    raw.sort(key=lambda r: (r[1], -abs(r[2] - (counts[r[1]] - 1) / 2),
                            r[0].relative_height, r[0].id))
    for a, row_index, slot, source_scale, x, y, _ in raw:
        dx, dy = corrections.offsets_in.get(a.id, (0, 0))
        if not all(isfinite(v) for v in (dx, dy)):
            raise ValueError("Manual offsets must be finite inches.")
        scale = source_scale * global_scale
        x, y = origin_x + x * global_scale + dx * PPI, origin_y + y * global_scale + dy * PPI
        body, head = transform(a.body, scale, x, y), transform(a.head, scale, x, y)
        if not area.contains(body):
            issues.append(f"{a.id}: outside athlete placement area after manual correction.")
        placements.append(Placement(a.id, row_index, slot, scale, x, y, body, head,
                                    a.image_size, a.body))
    # Conservative preflight. Silhouette-aware visibility is a later adapter;
    # body boxes can overflag wide arms, but never certify an obscured face.
    for i, back in enumerate(placements):
        for front in placements[i + 1:]:
            if back.head.overlaps(front.body):
                issues.append(f"{back.athlete_id}: possible face obstruction by {front.athlete_id}.")
    return Layout(tuple(placements), counts, global_scale, tuple(issues))
