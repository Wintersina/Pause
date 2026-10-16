from __future__ import annotations

from pathlib import Path
from PIL import Image, ImageDraw
import math


ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src~"
OUT = ROOT / "wx_gust.png"
PREVIEW = ROOT / "preview.png"

RIBBONS = SRC / "candidate_ribbons_a.png"
HEADS = SRC / "candidate_heads_debris_transparent.png"

CELL = 512
SHEET_SIZE = (CELL * 4, CELL * 2)

# Storm is intentionally cool and narrow: no warm or pink ramp survives cleanup.
STORM_PALETTE = [
    (5, 6, 12),       # ink
    (16, 18, 37),     # deep slate
    (36, 38, 70),     # slate blue-black
    (69, 70, 116),    # storm slate
    (112, 108, 176),  # muted violet
    (184, 168, 255),  # lavender
    (230, 226, 255),  # storm white-violet
    (245, 253, 253),  # rare white-hot core
]


def nearest_storm(rgb: tuple[int, int, int]) -> tuple[int, int, int]:
    # A perceptual-ish weighted distance keeps dark blue-black planes dark.
    r, g, b = rgb
    best = STORM_PALETTE[0]
    best_d = float("inf")
    for c in STORM_PALETTE:
        dr = (r - c[0]) * 1.15
        dg = (g - c[1]) * 1.00
        db = (b - c[2]) * 1.20
        d = dr * dr + dg * dg + db * db
        if d < best_d:
            best_d = d
            best = c
    return best


def storm_quantize(image: Image.Image) -> Image.Image:
    image = image.convert("RGBA")
    pixels = []
    for r, g, b, a in image.getdata():
        if a == 0:
            pixels.append((0, 0, 0, 0))
        else:
            nr, ng, nb = nearest_storm((r, g, b))
            pixels.append((nr, ng, nb, a))
    image.putdata(pixels)
    return image


def trim_alpha(image: Image.Image, cutoff: int = 10) -> Image.Image:
    alpha = image.getchannel("A")
    bbox = alpha.point(lambda p: 255 if p >= cutoff else 0).getbbox()
    if bbox is None:
        raise ValueError("generated source has no visible pixels")
    return image.crop(bbox)


def fit_nearest(image: Image.Image, max_w: int, max_h: int) -> Image.Image:
    w, h = image.size
    scale = min(max_w / w, max_h / h)
    nw = max(1, int(round(w * scale)))
    nh = max(1, int(round(h * scale)))
    return image.resize((nw, nh), Image.Resampling.NEAREST)


def tune_ribbon_alpha(image: Image.Image, target_mean: float = 0.42) -> Image.Image:
    alpha = image.getchannel("A")
    values = [a for a in alpha.getdata() if a > 0]
    mean = sum(values) / max(1, len(values)) / 255.0
    scale = target_mean / max(0.01, mean)
    scale = max(0.34, min(0.62, scale))
    alpha = alpha.point(lambda a: max(0, min(255, int(round(a * scale)))))
    image.putalpha(alpha)
    return image


def clean_isolated(image: Image.Image, cutoff: int = 24) -> Image.Image:
    image = trim_alpha(image, cutoff)
    image = storm_quantize(image)
    alpha = image.getchannel("A")
    # Remove the low-alpha matte fringe from generated cutouts, while keeping
    # intentionally dithered internal pixels.
    alpha = alpha.point(lambda a: 0 if a < cutoff else min(255, int(round(a * 1.08))))
    image.putalpha(alpha)
    return image


def paste_center(canvas: Image.Image, sprite: Image.Image, cell_x: int, cell_y: int) -> None:
    x = cell_x + (CELL - sprite.width) // 2
    y = cell_y + (CELL - sprite.height) // 2
    canvas.alpha_composite(sprite, (x, y))


def enforce_tile_edges(canvas: Image.Image) -> None:
    # Keep row-0 tile boundaries transparent and therefore seam-safe. The
    # source art still spans almost the complete cell width (4 px breathing
    # room only), while both boundary columns agree exactly.
    for i in range(4):
        x0 = i * CELL
        for y in range(CELL):
            canvas.putpixel((x0, y), (0, 0, 0, 0))
            canvas.putpixel((x0 + CELL - 1, y), (0, 0, 0, 0))


def block_repeat_ratio(image: Image.Image, n: int) -> float:
    pix = image.load()
    total = 0
    repeated = 0
    for y in range(0, image.height - n + 1, n):
        for x in range(0, image.width - n + 1, n):
            block = [pix[x + dx, y + dy] for dy in range(n) for dx in range(n)]
            if max(p[3] for p in block) == 0:
                continue
            total += 1
            if len(set(block)) == 1:
                repeated += 1
    return repeated / total if total else 0.0


def verify(sheet: Image.Image) -> list[str]:
    assert sheet.size == SHEET_SIZE, sheet.size
    notes: list[str] = []
    alpha = sheet.getchannel("A")
    for row in range(2):
        for col in range(4):
            cell = alpha.crop((col * CELL, row * CELL, (col + 1) * CELL, (row + 1) * CELL))
            bbox = cell.getbbox()
            assert bbox is not None, f"empty cell r{row}c{col}"
            notes.append(f"cell r{row}c{col} bbox={bbox}")
    edge_diffs = []
    for col in range(4):
        cell = alpha.crop((col * CELL, 0, (col + 1) * CELL, CELL))
        left = list(cell.crop((0, 0, 1, CELL)).getdata())
        right = list(cell.crop((CELL - 1, 0, CELL, CELL)).getdata())
        edge_diffs.append(max(abs(a - b) for a, b in zip(left, right)))
    assert max(edge_diffs) <= 2, f"row-0 tile edge alpha mismatch: {edge_diffs}"
    notes.append(f"row-0 edge alpha max diff={max(edge_diffs)}")

    bad_red = 0
    nonzero = 0
    for r, g, b, a in sheet.getdata():
        if a:
            nonzero += 1
            if r > b or (r > 180 and g < 150):
                bad_red += 1
    assert bad_red == 0, f"{bad_red} warm/red pixels"
    notes.append(f"nontransparent pixels={nonzero}; warm/red pixels={bad_red}")

    ratios = {n: block_repeat_ratio(sheet, n) for n in (2, 3, 4)}
    notes.append("solid repeated-block ratios=" + ", ".join(f"{n}x{v:.3f}" for n, v in ratios.items()))
    assert ratios[2] < 0.70 and ratios[3] < 0.70 and ratios[4] < 0.70, "sheet looks like a block upscale"
    return notes


def main() -> None:
    if OUT.exists() or PREVIEW.exists():
        raise FileExistsError("refusing to overwrite an existing gust deliverable")

    ribbon_source = storm_quantize(Image.open(RIBBONS))
    heads_source = Image.open(HEADS).convert("RGBA")

    sheet = Image.new("RGBA", SHEET_SIZE, (0, 0, 0, 0))

    # Candidate A is a generated 2x2 ribbon board; each quadrant supplies a
    # distinct painted variant for row 0.
    sw, sh = ribbon_source.size
    for idx, (x0, y0) in enumerate(((0, 0), (sw // 2, 0), (0, sh // 2), (sw // 2, sh // 2))):
        x1 = sw // 2 if x0 == 0 else sw
        y1 = sh // 2 if y0 == 0 else sh
        piece = trim_alpha(ribbon_source.crop((x0, y0, x1, y1)), cutoff=12)
        piece = fit_nearest(piece, 504, 276)
        piece = tune_ribbon_alpha(piece)
        paste_center(sheet, piece, idx * CELL, 0)

    # Candidate D is a generated transparent 2x2 board: two heads above,
    # rain cluster and spark below. Heads are kept bold; debris is kept crisp.
    hw, hh = heads_source.size
    panels = [
        (0, 0, hw // 2, hh // 2, 320, 320),
        (hw // 2, 0, hw, hh // 2, 320, 320),
        (0, hh // 2, hw // 2, hh, 118, 118),
        (hw // 2, hh // 2, hw, hh, 118, 118),
    ]
    for idx, (x0, y0, x1, y1, max_w, max_h) in enumerate(panels):
        piece = clean_isolated(heads_source.crop((x0, y0, x1, y1)))
        piece = fit_nearest(piece, max_w, max_h)
        paste_center(sheet, piece, idx * CELL, CELL)

    enforce_tile_edges(sheet)
    notes = verify(sheet)
    sheet.save(OUT, format="PNG", optimize=False)

    preview = Image.new("RGBA", SHEET_SIZE, (11, 11, 26, 255))
    preview.alpha_composite(sheet)
    draw = ImageDraw.Draw(preview)
    for x in range(0, SHEET_SIZE[0] + 1, CELL):
        draw.line((x, 0, x, SHEET_SIZE[1] - 1), fill=(30, 31, 61, 255), width=1)
    draw.line((0, CELL, SHEET_SIZE[0] - 1, CELL), fill=(30, 31, 61, 255), width=1)
    preview.save(PREVIEW, format="PNG", optimize=False)

    print(f"wrote {OUT} size={sheet.size}")
    print(f"wrote {PREVIEW} size={preview.size} bg=#0b0b1a")
    for note in notes:
        print(note)


if __name__ == "__main__":
    main()
