"""Development CLI: generate a synthetic composite and reusable layout record."""

import argparse
from dataclasses import asdict
import json
from pathlib import Path

from PIL import Image

from .composition import compose
from .model import CompositionSettings, Corrections
from .rendering import render
from .rows import RowTable
from .synthetic import athletes, write_sources


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--demo", type=int, required=True, metavar="ATHLETES")
    parser.add_argument("--output", type=Path, required=True, help="New demo directory")
    parser.add_argument("--rows", type=Path, default=Path(__file__).parent / "data/row_counts.csv")
    args = parser.parse_args()
    table = RowTable.load(args.rows)
    people = athletes(args.demo)
    layout = compose(people, table)
    args.output.mkdir(parents=True, exist_ok=False)
    sources = write_sources(args.output / "sources", people)
    background, foreground = args.output / "background.jpg", args.output / "foreground.png"
    with Image.new("RGB", (7200, 2400), "#0B1F3A") as image:
        image.save(background)
    with Image.new("RGBA", (7200, 2400)) as image:
        image.save(foreground)
    result = render(layout, sources, background, foreground, "SYNTHETIC DEMO TEAM",
                    args.output / "team.jpg")
    record = {"schema_version": 1, "input_kind": "synthetic",
              "athletes": [asdict(a) for a in people],
              "settings": asdict(CompositionSettings()), "corrections": asdict(Corrections()),
              "sources": {key: str(path.relative_to(args.output)) for key, path in sources.items()},
              "layout": asdict(layout), "render": result}
    (args.output / "layout.json").write_text(json.dumps(record, indent=2) + "\n")
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
