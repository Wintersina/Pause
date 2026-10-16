from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageChops, ImageStat


ROOT = Path(__file__).resolve().parent.parent
SHEET = ROOT / "wx_gust.png"
CELL = 512
PALETTE = {
    (0, 0, 0),
    (5, 6, 12),
    (47, 35, 27),
    (83, 61, 40),
    (143, 129, 96),
    (216, 176, 112),
    (243, 217, 160),
    (255, 241, 201),
}


def cell(im: Image.Image, row: int, col: int) -> Image.Image:
    return im.crop((col * CELL, row * CELL, (col + 1) * CELL, (row + 1) * CELL))


def visible_alpha_mean(im: Image.Image) -> float:
    values = [v for v in im.getchannel("A").getdata() if v > 0]
    return sum(values) / max(1, len(values)) / 255.0


def edge_delta(im: Image.Image) -> float:
    a = im.getchannel("A")
    return sum(abs(a.getpixel((0, y)) - a.getpixel((CELL - 1, y))) for y in range(CELL)) / CELL / 255.0


def bbox_size(im: Image.Image):
    box = im.getchannel("A").point(lambda v: 255 if v > 12 else 0).getbbox()
    return None if box is None else (box[2] - box[0], box[3] - box[1], box)


def block_fraction(im: Image.Image) -> float:
    """Repository-style share of visible texels copied by a 2/3/4x block."""
    pix = im.load()
    worst = 0.0
    for factor in (2, 3, 4):
        for phase in range(factor):
            visible = same = 0
            for y in range(phase, CELL - factor + 1):
                for x in range(phase, CELL - factor + 1):
                    if pix[x, y][3] <= 24:
                        continue
                    visible += 1
                    bx = x - ((x - phase) % factor)
                    by = y - ((y - phase) % factor)
                    first = pix[bx, by]
                    current = pix[x, y]
                    if first[:3] == current[:3] and abs(first[3] - current[3]) < 3:
                        same += 1
            if visible:
                worst = max(worst, same / visible)
    return worst


def main() -> None:
    im = Image.open(SHEET)
    assert im.size == (2048, 1024), f"wrong size: {im.size}"
    assert im.mode == "RGBA", f"wrong mode: {im.mode}"

    rgb = im.convert("RGB")
    colours = set(rgb.getdata())
    unexpected = colours - PALETTE
    assert not unexpected, f"unexpected/non-Desert colours: {sorted(unexpected)[:8]}"

    print(f"PASS size/mode: {im.size} {im.mode}")
    print(f"PASS palette: {len(colours)} exact RGB colours, no red/pink candidates")

    for row in range(2):
        for col in range(4):
            c = cell(im, row, col)
            size = bbox_size(c)
            assert size is not None, f"empty cell r{row}c{col}"
            print(f"PASS filled r{row}c{col}: bbox={size[0]}x{size[1]}")

            blocks = block_fraction(c)
            assert blocks < 0.97, f"possible 2/3/4x block upscale in r{row}c{col}: {blocks:.3f}"
            if row == 0:
                delta = edge_delta(c)
                assert delta < 0.055, f"row0 edge mismatch r{row}c{col}: {delta:.4f}"
                mean = visible_alpha_mean(c)
                assert 0.35 <= mean <= 0.50, f"ribbon alpha out of airy range r{row}c{col}: {mean:.3f}"
                y0, y1 = size[2][1], size[2][3]
                assert y0 >= 110 and y1 <= 402, f"ribbon outside central band r{row}c{col}: y={y0}..{y1}"
                print(f"PASS tile/alpha r{row}c{col}: edge={delta:.4f} visible-mean={mean:.3f}")
            if row == 1 and col in (2, 3):
                assert 30 <= size[0] <= 120 and 30 <= size[1] <= 120, f"debris size r{row}c{col}: {size[:2]}"
                print(f"PASS debris budget r{row}c{col}: {size[0]}x{size[1]}")

    print("RESULT: PASS")


if __name__ == "__main__":
    main()
