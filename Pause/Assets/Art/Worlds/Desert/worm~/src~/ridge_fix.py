"""Replace only sequence A with image-generated, low dune humps.

The mound uses ridge_candidate_d.png; the wake uses ridge_candidate_b.png.
Every resample and shift is nearest-neighbour. Originals live in originals_ridge/.
"""
from __future__ import annotations

import json
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "src~"
NN = Image.Resampling.NEAREST


def recolor(image: Image.Image) -> Image.Image:
    """Keep painted luminance detail within the Desert sand and indigo ramps."""
    p = np.asarray(image.convert("RGBA"), dtype=np.uint8).copy()
    rgb = p[..., :3].astype(np.float32) / 255.0
    maximum = rgb.max(axis=2)
    minimum = rgb.min(axis=2)
    saturation = np.divide(maximum - minimum, maximum, out=np.zeros_like(maximum), where=maximum > 0)
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    # Generated transparency occasionally has pure red and lemon fringes.
    fringe = (saturation > .55) & ((r > g * 1.24) | (g > r * 1.21))
    p[..., 3][fringe] = 0
    p[..., 3][p[..., 3] < 36] = 0
    shadow = (b > r * 1.06) & (b > g * 1.08) & (maximum < .71)
    hue = np.where(shadow, 238.0, 44.0 + np.clip((r - b) * 3.0, -2, 2))
    sat = np.where(shadow, np.clip(.29 + saturation * .24, .29, .43),
                   np.clip(.16 + saturation * .32, .16, .31))
    value = np.where(shadow, np.clip(.10 + maximum * .30, .11, .28),
                     np.clip(.20 + maximum * .33, .30, .51))
    crest = ~shadow & (maximum > .88) & (saturation < .26)
    value[crest] = np.minimum(.59, .54 + (maximum[crest] - .88) * .42)
    c = value * sat
    x = c * (1 - np.abs((hue / 60) % 2 - 1))
    m = value - c
    out = np.zeros_like(rgb)
    sand = ~shadow
    out[..., 0][sand], out[..., 1][sand], out[..., 2][sand] = (c + m)[sand], (x + m)[sand], m[sand]
    out[..., 0][shadow], out[..., 1][shadow], out[..., 2][shadow] = m[shadow], (x + m)[shadow], (c + m)[shadow]
    p[..., :3] = np.rint(np.clip(out, 0, 1) * 255).astype(np.uint8)
    return Image.fromarray(p, "RGBA")


def tint_lee(image: Image.Image) -> Image.Image:
    """Push the generated mound's lower right painted flank into dusty indigo."""
    p = np.asarray(image).copy()
    height, width = p.shape[:2]
    yy, xx = np.indices((height, width))
    value = p[..., :3].max(axis=2).astype(np.float32) / 255
    lee = ((p[..., 3] > 32) & (yy / height > .28)
           & (xx / width > .60 - .10 * (yy / height - .5)) & (value < .47))
    vv = np.clip(value * .57, .13, .28)
    chroma = vv * .36
    minor = chroma / 30
    base = vv - chroma
    p[..., 0][lee] = np.rint(base[lee] * 255).astype(np.uint8)
    p[..., 1][lee] = np.rint((base + minor)[lee] * 255).astype(np.uint8)
    p[..., 2][lee] = np.rint(vv[lee] * 255).astype(np.uint8)
    return Image.fromarray(p, "RGBA")


def make_frame(cell: int, scale: str, i: int, count: int, hump: Image.Image, wake: Image.Image) -> Image.Image:
    if scale == "s":
        width, height, wake_width, wake_height, top = 154, 86, 30, 100, 72
        offsets = [(0, 0), (2, -1), (4, -1), (5, 0), (3, 1), (1, 1), (-2, 0), (-1, -1)]
    elif scale == "m":
        width, height, wake_width, wake_height, top = 166, 91, 34, 103, 67
        offsets = [(0, 0), (2, -1), (5, -1), (6, 0), (4, 1), (1, 1), (-2, 0), (-1, -1)]
    else:
        width, height, wake_width, wake_height, top = 314, 176, 60, 196, 137
        offsets = [(0, 0), (8, -3), (4, 3), (-5, -1)]
    dx, dy = offsets[i]
    out = Image.new("RGBA", (cell, cell))
    mound = hump.resize((width, height), NN)
    # Pixel-step alpha bands break up the source cutout edge without blurring
    # any of the generated texture or its narrow crest.
    ma = np.asarray(mound).copy()
    inside = ma[..., 3] > 32
    factor = np.ones(inside.shape, dtype=np.float32)
    current = inside.copy()
    for step in range(4):
        eroded = current.copy()
        for dy0, dx0 in ((-1, 0), (1, 0), (0, -1), (0, 1)):
            eroded &= np.roll(current, (dy0, dx0), axis=(0, 1))
        band = current & ~eroded
        factor[band] = (.34, .53, .72, .88)[step]
        current = eroded
    ma[..., 3] = np.rint(ma[..., 3].astype(np.float32) * factor).astype(np.uint8)
    mound = Image.fromarray(ma, "RGBA")
    # A painted dust trace continues below the low dune mound. Its alpha fades
    # down-screen, and the final contact grains remain at the fixed anchor.
    trail = wake.resize((wake_width, wake_height), NN)
    a = np.asarray(trail).copy()
    y = np.linspace(0, 1, wake_height, dtype=np.float32)[:, None]
    a[..., 3] = np.rint(a[..., 3].astype(np.float32) * (.55 - .43 * y)).astype(np.uint8)
    trail = Image.fromarray(a, "RGBA")
    out.alpha_composite(trail, ((cell - wake_width) // 2, cell - 16 - wake_height))
    out.alpha_composite(mound, ((cell - width) // 2 + dx, top + dy))
    # Three sand grains at the bottom-centre origin make the shared ground
    # contact measurable without adding a standing tail silhouette.
    sand = (93, 84, 65, 132)
    anchor = cell - 20
    for x, y0 in ((cell // 2 - 2, anchor), (cell // 2, anchor - 1), (cell // 2 + 2, anchor)):
        out.putpixel((x, y0), sand)
    fixed = np.asarray(out).copy()
    r, g, b = fixed[..., 0], fixed[..., 1], fixed[..., 2]
    violet_blend = (b > r) & (r > g) & (fixed[..., 3] > 0)
    fixed[..., 1][violet_blend] = r[violet_blend]
    return Image.fromarray(fixed, "RGBA")


def main() -> None:
    hump = tint_lee(recolor(Image.open(SRC / "ridge_candidate_d.png").crop((5, 465, 1250, 846))))
    wake = recolor(Image.open(SRC / "ridge_candidate_b.png").crop((515, 702, 822, 1228)))
    original = SRC / "originals_ridge"
    small = Image.open(original / "worm.png").convert("RGBA")
    giant = Image.open(original / "worm_giant.png").convert("RGBA")
    for scale, sheet, cell, row, n in (("s", small, 256, 0, 8), ("m", small, 256, 4, 8), ("l", giant, 512, 0, 4)):
        for i in range(n):
            frame = make_frame(cell, scale, i, n, hump, wake)
            sheet.paste(frame, (i * cell, row * cell))
    small.save(ROOT / "worm.png", optimize=True)
    giant.save(ROOT / "worm_giant.png", optimize=True)
    manifest = json.loads((original / "manifest.json").read_text())
    manifest["ridge_fix"] = {
        "sources": ["src~/ridge_candidate_d.png", "src~/ridge_candidate_b.png"],
        "candidate_rejected": ["src~/ridge_candidate_a.png (triangular wake)",
                               "src~/ridge_candidate_c.png (diagonal leaf-like central crest)"],
        "method": "generated mound and wake, HSV ramp cleaning, lee-flank tint, nearest-neighbour composition",
        "hump_target_px": {"small": [154, 86], "medium": [166, 91], "giant": [314, 176]},
        "scope": "small row 0 cells 0-7; medium row 4 cells 0-7; giant row 0 cells 0-3",
    }
    (ROOT / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")


if __name__ == "__main__":
    main()
