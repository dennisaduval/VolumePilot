"""Authoritative external row tables; Row 1 is always backmost."""

import csv
from pathlib import Path


def validate_rows(rows: tuple[int, ...], count: int) -> None:
    if not rows or any(type(n) is not int or not 1 <= n <= 16 for n in rows):
        raise ValueError("Every occupied row must contain 1–16 athletes.")
    if sum(rows) != count:
        raise ValueError(f"Row counts {rows} do not total {count} athletes.")


class RowTable:
    def __init__(self, entries: dict[int, tuple[int, ...]]):
        if not entries:
            raise ValueError("The row table is empty.")
        for count, rows in entries.items():
            if type(count) is not int or count < 1:
                raise ValueError("Athlete counts must be positive integers.")
            validate_rows(rows, count)
        self.entries = dict(entries)

    @classmethod
    def load(cls, path: Path) -> "RowTable":
        entries = {}
        with path.open(newline="", encoding="utf-8-sig") as stream:
            reader = csv.DictReader(stream)
            if reader.fieldnames != ["Athlete Count", "Row 1", "Row 2", "Row 3", "Row 4"]:
                raise ValueError("Expected Athlete Count,Row 1,Row 2,Row 3,Row 4 headers.")
            for line, record in enumerate(reader, 2):
                try:
                    if None in record:
                        raise ValueError("Extra CSV fields")
                    count = int(record["Athlete Count"])
                    values = tuple(int(record[f"Row {i}"]) for i in range(1, 5))
                    if count in entries or any(n < 0 for n in values):
                        raise ValueError("Duplicate count or negative row")
                    occupied = tuple(n for n in values if n)
                    if values[:len(occupied)] != occupied:
                        raise ValueError("Empty rows must be trailing")
                    validate_rows(occupied, count)
                    entries[count] = occupied
                except (ValueError, TypeError) as error:
                    raise ValueError(f"Invalid row table at line {line}: {error}") from error
        return cls(entries)

    def for_count(self, count: int) -> tuple[int, ...]:
        if type(count) is not int or count < 1:
            raise ValueError("A team must have at least one athlete.")
        if count in self.entries:
            return self.entries[count]
        maximum = max(self.entries)
        if count < maximum:
            raise ValueError(f"No CSV configuration for {count} athletes.")
        # Continue from the last exact entry. Balance by filling the shortest
        # row first (backmost breaks ties). Once all reach 16, add one row and
        # rebalance; this avoids a nearly empty extra row at each transition.
        rows = list(self.entries[maximum])
        for total in range(maximum + 1, count + 1):
            if min(rows) == 16:
                n = len(rows) + 1
                base, remainder = divmod(total, n)
                rows = [base + (i < remainder) for i in range(n)]
            else:
                index = min(range(len(rows)), key=lambda i: (rows[i], i))
                rows[index] += 1
        result = tuple(rows)
        validate_rows(result, count)
        return result
