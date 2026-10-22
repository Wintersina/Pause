#!/usr/bin/env python3
"""Validate the J2 exhaust sheets and print a compact per-cell audit table."""

from pathlib import Path
import sys

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
EXPECTED = {
    (184, 60, 192),   # violet
    (242, 134, 43),   # orange
    (244, 234, 212),  # bone-white
    (94, 26, 102),    # deep violet
    (58, 42, 92),     # indigo
    (0, 255, 0),      # key
}
KEY = (0, 255, 0)
PREVIEW_BG = (11, 11, 26)
CELL_W, CELL_H, FRAMES = 256, 1024, 8


def audit(path):
    image = Image.open(path).convert("RGB")
    assert image.size == (CELL_W * FRAMES, CELL_H), f"{path.name}: expected 2048x1024, got {image.size}"
    colors = set(image.get_flattened_data())
    assert colors <= EXPECTED, f"{path.name}: forbidden colors: {sorted(colors - EXPECTED)}"

    rows = []
    for i in range(FRAMES):
        cell = image.crop((i * CELL_W, 0, (i + 1) * CELL_W, CELL_H))
        data = list(cell.get_flattened_data())
        painted = [(x, y) for y in range(CELL_H) for x in range(CELL_W) if cell.getpixel((x, y)) != KEY]
        assert painted, f"{path.name} frame {i}: empty cell"
        xs = [p[0] for p in painted]
        ys = [p[1] for p in painted]
        bbox = (min(xs), min(ys), max(xs), max(ys))
        # All corners and the entire outer edge remain key green: no backdrop rectangle.
        edge = [cell.getpixel((x, y)) for x in range(CELL_W) for y in (0, CELL_H - 1)]
        edge += [cell.getpixel((x, y)) for y in range(CELL_H) for x in (0, CELL_W - 1)]
        assert all(px == KEY for px in edge), f"{path.name} frame {i}: non-key edge/backdrop pixel"
        assert (bbox[2] - bbox[0] + 1) * (bbox[3] - bbox[1] + 1) < CELL_W * CELL_H * 0.72, (
            f"{path.name} frame {i}: painted rectangle-like backdrop"
        )

        counts = {color: data.count(color) for color in EXPECTED if color != KEY}
        rows.append({
            "frame": i,
            "painted": len(painted),
            "tail": max(ys) / (CELL_H - 1) * 100.0,
            "bone": counts[(244, 234, 212)],
            "orange": counts[(242, 134, 43)],
            "violet": counts[(184, 60, 192)],
        })
    return rows


def print_table(name, rows):
    print(f"\n{name}")
    print("frame  painted  tail%  bone  orange  violet")
    for row in rows:
        print(f"{row['frame']:>5}  {row['painted']:>7}  {row['tail']:>5.1f}  {row['bone']:>4}  {row['orange']:>6}  {row['violet']:>6}")


def save_preview(cruise, boost):
    preview = Image.new("RGB", (CELL_W * FRAMES, CELL_H * 2), PREVIEW_BG)
    for y_offset, sheet in ((0, cruise), (CELL_H, boost)):
        layer = Image.new("RGB", sheet.size)
        layer.putdata([PREVIEW_BG if px == KEY else px for px in sheet.get_flattened_data()])
        preview.paste(layer, (0, y_offset))
    preview.save(ROOT / "preview_j2.png", format="PNG", optimize=False)


def main():
    cruise = audit(ROOT / "flame_cruise.png")
    boost = audit(ROOT / "flame_boost.png")
    save_preview(Image.open(ROOT / "flame_cruise.png").convert("RGB"), Image.open(ROOT / "flame_boost.png").convert("RGB"))
    print_table("flame_cruise.png", cruise)
    print_table("flame_boost.png", boost)

    cruise_bone = sum(row["bone"] for row in cruise)
    boost_bone = sum(row["bone"] for row in boost)
    cruise_hot = sum(row["bone"] + row["orange"] for row in cruise)
    boost_hot = sum(row["bone"] + row["orange"] for row in boost)
    cruise_tail = min(row["tail"] for row in cruise)
    boost_tail = min(row["tail"] for row in boost)
    assert boost_bone > cruise_bone, "boost is not brighter: bone-white count did not increase"
    assert boost_hot > cruise_hot, "boost is not hotter: bone/orange count did not increase"
    assert boost_tail > cruise_tail, "boost is not longer: shortest boost tail did not exceed cruise"
    assert cruise[2]["bone"] == max(row["bone"] for row in cruise), "cruise burst frame 2 is not brightest"
    assert boost[2]["bone"] == max(row["bone"] for row in boost), "boost burst frame 2 is not brightest"

    print("\nPASS: 8 equal 256x1024 cells, exact six-color palette, green edges, boost longer/hotter.")


if __name__ == "__main__":
    try:
        main()
    except (AssertionError, FileNotFoundError) as exc:
        print(f"FAIL: {exc}", file=sys.stderr)
        raise SystemExit(1)
