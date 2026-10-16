from __future__ import annotations

from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parent.parent
SOURCE = Path(__file__).resolve().parent / "candidate-3.png"
OUT = ROOT / "wx_gust.png"
PREVIEW = ROOT / "preview.png"

W = H = 512

# Desert-only ramp. The generated painting is quantized to these exact tones so
# fringe contamination cannot introduce red, pink, blue, or green pixels.
PALETTE = [
    (5, 6, 12),       # INK
    (47, 35, 27),     # deep dusty brown
    (83, 61, 40),     # dusty brown shade
    (143, 129, 96),   # Desert plan dark/ochre shadow
    (216, 176, 112),  # user-specified ochre
    (243, 217, 160),  # user-specified sun-gold sand
    (255, 241, 201),  # restrained warm highlight
]
BG = (11, 11, 26, 255)


def nearest_colour(rgb: tuple[int, int, int]) -> tuple[int, int, int]:
    return min(PALETTE, key=lambda c: sum((rgb[i] - c[i]) ** 2 for i in range(3)))


def quantize(im: Image.Image) -> Image.Image:
    """Keep alpha, but force every RGB value onto the Desert ramp."""
    src = im.convert("RGBA")
    out = Image.new("RGBA", src.size)
    sp = src.load()
    op = out.load()
    for y in range(src.height):
        for x in range(src.width):
            r, g, b, a = sp[x, y]
            if a < 10:
                op[x, y] = (0, 0, 0, 0)
                continue
            # Dark, nearly invisible generator noise is not useful wind art.
            if a < 24 and max(r, g, b) < 72:
                op[x, y] = (0, 0, 0, 0)
                continue
            c = nearest_colour((r, g, b))
            op[x, y] = (*c, a)
    return out


def source_cell(source: Image.Image, row: int, col: int) -> Image.Image:
    x0 = round(col * source.width / 4)
    x1 = round((col + 1) * source.width / 4)
    y0 = round(row * source.height / 2)
    y1 = round((row + 1) * source.height / 2)
    return source.crop((x0, y0, x1, y1)).resize((W, H), Image.Resampling.NEAREST)


def alpha_bbox(im: Image.Image, threshold: int = 12):
    a = im.getchannel("A").point(lambda v: 255 if v > threshold else 0)
    return a.getbbox()


def fit_isolated(im: Image.Image, max_w: int, max_h: int) -> Image.Image:
    """Extract a generated object, scale it down only, and centre it."""
    box = alpha_bbox(im)
    if not box:
        return Image.new("RGBA", im.size)
    obj = im.crop(box)
    scale = min(max_w / obj.width, max_h / obj.height, 1.0)
    nw = max(1, round(obj.width * scale))
    nh = max(1, round(obj.height * scale))
    obj = obj.resize((nw, nh), Image.Resampling.NEAREST)
    placed = Image.new("RGBA", im.size)
    placed.alpha_composite(obj, ((W - nw) // 2, (H - nh) // 2))
    return placed


def fit_ribbon(im: Image.Image) -> Image.Image:
    """Keep a full-width ribbon, but confine it to the middle 55% of its cell."""
    box = alpha_bbox(im)
    if not box:
        return Image.new("RGBA", im.size)
    # Preserve the full width for edge continuity; only use the painted y span.
    y0, y1 = max(0, box[1] - 8), min(H, box[3] + 8)
    ribbon = im.crop((0, y0, W, y1)).resize((W, 282), Image.Resampling.NEAREST)
    placed = Image.new("RGBA", im.size)
    placed.alpha_composite(ribbon, (0, 115))
    # Wind ribbons are deliberately translucent so gameplay remains readable.
    # Target roughly 35-45% mean alpha over painted ribbon pixels, while
    # retaining a brighter core and dithered low-alpha tails.
    alpha = placed.getchannel("A").point(lambda a: min(140, round(a * 0.55)))
    placed.putalpha(alpha)
    return placed


def make_tileable(im: Image.Image) -> Image.Image:
    """Make the two horizontal edge strips agree without retaining source seams."""
    out = im.copy()
    px = out.load()
    for y in range(H):
        for i in range(16):
            # The generator sometimes draws a faint atlas separator at x=0/511.
            # Borrow the adjacent painted strips, then mirror the same averaged
            # pixels to both edges so a repeated tile has no dark bar.
            left = px[16 + i, y]
            right = px[W - 17 - i, y]
            c = tuple(round((left[k] + right[k]) / 2) for k in range(4))
            px[i, y] = c
            px[W - 1 - i, y] = c
    return out


def build() -> Image.Image:
    source = Image.open(SOURCE).convert("RGBA")
    sheet = Image.new("RGBA", (W * 4, H * 2))

    for col in range(4):
        ribbon = make_tileable(fit_ribbon(quantize(source_cell(source, 0, col))))
        sheet.alpha_composite(ribbon, (col * W, 0))

    # The heads are telegraphs, not full-cell walls: keep them around 60% of a cell.
    for col in range(2):
        head = fit_isolated(quantize(source_cell(source, 1, col)), 330, 330)
        sheet.alpha_composite(head, (col * W, H))

    # Small carried pieces: keep the painted sprites crisp and within 30-120 px.
    grit = fit_isolated(quantize(source_cell(source, 1, 2)), 116, 116)
    twig = fit_isolated(quantize(source_cell(source, 1, 3)), 116, 116)
    sheet.alpha_composite(grit, (2 * W, H))
    sheet.alpha_composite(twig, (3 * W, H))

    # A final quantization also cleans the averaged tile seams.
    return quantize(sheet)


def main() -> None:
    final = build()
    final.save(OUT, format="PNG")
    preview = Image.new("RGBA", final.size, BG)
    preview.alpha_composite(final)
    preview.convert("RGB").save(PREVIEW, format="PNG")


if __name__ == "__main__":
    main()
